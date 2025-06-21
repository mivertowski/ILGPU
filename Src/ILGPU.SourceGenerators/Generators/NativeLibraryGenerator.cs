// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: NativeLibraryGenerator.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace ILGPU.SourceGenerators.Generators
{
    /// <summary>
    /// Source generator that creates LibraryImport-based native bindings
    /// to replace DllImport for AOT compatibility.
    /// </summary>
    [Generator]
    public sealed class NativeLibraryGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                // Generate CUDA API LibraryImport bindings
                var cudaXml = LoadEmbeddedResource("CudaAPI.xml");
                if (!string.IsNullOrEmpty(cudaXml))
                {
                    GenerateLibraryImportsFromXmlContent(context, cudaXml, "CudaAPI");
                }

                // Generate OpenCL API LibraryImport bindings
                var openclXml = LoadEmbeddedResource("CLAPI.xml");
                if (!string.IsNullOrEmpty(openclXml))
                {
                    GenerateLibraryImportsFromXmlContent(context, openclXml, "CLAPI");
                }
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "ILGPU0001",
                        "Native library generation failed",
                        "Failed to generate native library imports: {0}",
                        "CodeGeneration",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None,
                    ex.Message));
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization needed
        }

        private static string? LoadEmbeddedResource(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourcePath = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith(resourceName));

                if (resourcePath == null)
                    return null;

                using var stream = assembly.GetManifestResourceStream(resourcePath);
                if (stream == null)
                    return null;

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        private static void GenerateLibraryImportsFromXmlContent(GeneratorExecutionContext context, string xmlContent, string apiName)
        {
            try
            {
                var xmlDoc = XDocument.Parse(xmlContent);
                var imports = xmlDoc.Root;
                
                if (imports?.Name != "Imports")
                    return;

                var namespaceName = imports.Attribute("Namespace")?.Value ?? "ILGPU.Runtime.Generated";
                var className = imports.Attribute("ClassName")?.Value ?? "GeneratedAPI";
                var defaultReturnType = imports.Attribute("DefaultReturnType")?.Value ?? "int";
                var notSupportedException = imports.Attribute("NotSupportedException")?.Value ?? "\"API not supported\"";

                var libraryNames = ParseLibraryNames(imports.Element("LibraryNames"));
                var entryPoints = ParseEntryPoints(imports.Elements("Import"), defaultReturnType);

                var sourceCode = GenerateLibraryImportClass(
                    namespaceName, 
                    className, 
                    libraryNames, 
                    entryPoints,
                    notSupportedException);

                context.AddSource($"{className}.AOT.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "ILGPU0002",
                        "XML parsing failed",
                        "Failed to parse XML content for {0}: {1}",
                        "CodeGeneration",
                        DiagnosticSeverity.Warning,
                        true),
                    Location.None,
                    apiName,
                    ex.Message));
            }
        }

        private static Dictionary<string, string> ParseLibraryNames(XElement? libraryNamesElement)
        {
            var result = new Dictionary<string, string>();
            
            if (libraryNamesElement == null)
                return result;

            var windows = libraryNamesElement.Element("Windows")?.Value;
            var linux = libraryNamesElement.Element("Linux")?.Value;
            var macOS = libraryNamesElement.Element("MacOS")?.Value;

            if (!string.IsNullOrEmpty(windows)) result["Windows"] = windows;
            if (!string.IsNullOrEmpty(linux)) result["Linux"] = linux;
            if (!string.IsNullOrEmpty(macOS)) result["MacOS"] = macOS;

            return result;
        }

        private static List<EntryPointInfo> ParseEntryPoints(IEnumerable<XElement> importElements, string defaultReturnType)
        {
            var entryPoints = new List<EntryPointInfo>();

            foreach (var import in importElements)
            {
                var name = import.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                var returnType = import.Attribute("ReturnType")?.Value ?? defaultReturnType;
                var stringMarshalling = import.Attribute("StringMarshalling")?.Value;
                var isUnsafe = bool.Parse(import.Attribute("Unsafe")?.Value ?? "false");

                var parameters = import.Elements("Parameter")
                    .Select(p => new ParameterInfo
                    {
                        Name = p.Attribute("Name")?.Value ?? "",
                        Type = p.Attribute("Type")?.Value ?? "int",
                        Flags = p.Attribute("Flags")?.Value,
                        DllFlags = p.Attribute("DllFlags")?.Value
                    })
                    .ToList();

                entryPoints.Add(new EntryPointInfo
                {
                    Name = name,
                    ReturnType = returnType,
                    StringMarshalling = stringMarshalling,
                    Parameters = parameters,
                    IsUnsafe = isUnsafe
                });
            }

            return entryPoints;
        }

        private static string GenerateLibraryImportClass(
            string namespaceName,
            string className,
            Dictionary<string, string> libraryNames,
            List<EntryPointInfo> entryPoints,
            string notSupportedException)
        {
            var sb = new StringBuilder();

            // File header
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// This file was generated by ILGPU.SourceGenerators.NativeLibraryGenerator");
            sb.AppendLine("// to provide AOT-compatible LibraryImport declarations");
            sb.AppendLine();

            // Usings
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using ILGPU.Resources;");
            sb.AppendLine();

            sb.AppendLine("#pragma warning disable IDE1006 // Naming");
            sb.AppendLine("#pragma warning disable CA2101 // Specify marshaling for P/Invoke string arguments");
            sb.AppendLine("#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute'");
            sb.AppendLine();

            // Namespace
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");

            // Note: Abstract method declarations already exist in DllImports.cs
            // We only need to generate the AOT-compatible implementations

            // Platform-specific implementations (these will replace the existing ones)
            GeneratePlatformImplementations(sb, className, libraryNames, entryPoints);

            // NotSupported implementation (this will replace the existing one)
            GenerateNotSupportedImplementation(sb, className, entryPoints, notSupportedException);

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute'");
            sb.AppendLine("#pragma warning restore CA2101 // Specify marshaling for P/Invoke string arguments");
            sb.AppendLine("#pragma warning restore IDE1006 // Naming");

            return sb.ToString();
        }

        private static void GenerateLibraryConstants(StringBuilder sb, Dictionary<string, string> libraryNames)
        {
            sb.AppendLine("        #region Constants");
            sb.AppendLine();

            // Generate unique constants based on library names, not platforms
            var uniqueLibraries = new HashSet<string>();
            var libraryToConstantName = new Dictionary<string, string>();
            
            foreach (var pair in libraryNames)
            {
                var libName = pair.Value;
                if (uniqueLibraries.Add(libName))
                {
                    // Create a safe constant name based on the library name
                    var constantName = $"LibName_{libName.Replace(".", "_").Replace("-", "_")}";
                    libraryToConstantName[libName] = constantName;
                    
                    var platforms = libraryNames.Where(p => p.Value == libName).Select(p => p.Key);
                    
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Library name: {libName}");
                    sb.AppendLine($"        /// Platforms: {string.Join(", ", platforms)}");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        public const string {constantName} = \"{libName}\";");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("        #endregion");
            sb.AppendLine();
        }

        private static void GenerateAbstractMethods(StringBuilder sb, List<EntryPointInfo> entryPoints)
        {
            sb.AppendLine("        #region Abstract Methods");
            sb.AppendLine();

            foreach (var entryPoint in entryPoints)
            {
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// {entryPoint.Name} native method");
                sb.AppendLine($"        /// </summary>");
                
                var parameterList = string.Join(", ", entryPoint.Parameters.Select(p => 
                    $"{GetManagedParameterDeclaration(p)} {SanitizeParameterName(p.Name)}"));
                
                sb.AppendLine($"        internal abstract {entryPoint.ReturnType} {entryPoint.Name}({parameterList});");
                sb.AppendLine();
            }

            sb.AppendLine("        #endregion");
            sb.AppendLine();
        }

        private static void GenerateStaticInitialization(StringBuilder sb, string className, Dictionary<string, string> libraryNames)
        {
            sb.AppendLine("        #region Static");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Returns the driver API for the current platform.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static {className} CurrentAPI {{ get; }} =");
            sb.AppendLine($"            LoadRuntimeAPI<");
            sb.AppendLine($"            {className},");

            var libraryToImplementation = GetLibraryToImplementationMapping(libraryNames);
            foreach (var kvp in libraryToImplementation)
            {
                var implementation = kvp.Value;
                sb.AppendLine($"            {className}_{implementation.Id},");
            }
            sb.AppendLine($"            {className}_NotSupported>();");
            sb.AppendLine();
            sb.AppendLine("        #endregion");
        }

        private static void GeneratePlatformImplementations(StringBuilder sb, string className, Dictionary<string, string> libraryNames, List<EntryPointInfo> entryPoints)
        {
            sb.AppendLine("    // AOT-compatible platform implementations");
            sb.AppendLine();

            var libraryToImplementation = GetLibraryToImplementationMapping(libraryNames);
            
            foreach (var kvp in libraryToImplementation)
            {
                var libraryName = kvp.Key;
                var implementation = kvp.Value;
                var id = implementation.Id;
                var platforms = implementation.Platforms;

                sb.AppendLine("    /// <summary>");
                sb.AppendLine($"    /// AOT-compatible implementation of {className} for {string.Join(", ", platforms)}");
                sb.AppendLine($"    /// Uses library: {libraryName}");
                sb.AppendLine("    /// </summary>");
                sb.AppendLine("    [System.Diagnostics.CodeAnalysis.SuppressMessage(");
                sb.AppendLine("        \"Security\",");
                sb.AppendLine("        \"CA5393:Do not use unsafe DllImportSearchPath value\")]");
                sb.AppendLine($"    sealed unsafe partial class {className}_AOT_{id} : {className}");
                sb.AppendLine("    {");

                // LibraryImport methods
                sb.AppendLine("        #region LibraryImport Methods");
                sb.AppendLine();

                foreach (var entryPoint in entryPoints)
                {
                    var constantName = GetLibraryConstantReference(libraryName);
                    GenerateLibraryImportMethod(sb, entryPoint, constantName);
                }

                sb.AppendLine("        #endregion");
                sb.AppendLine();

                // RuntimeAPI implementation
                sb.AppendLine("        #region RuntimeAPI");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// Returns true.");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        public override bool IsSupported => true;");
                sb.AppendLine();
                sb.AppendLine("        #endregion");
                sb.AppendLine();

                // Method implementations
                sb.AppendLine("        #region Implementations");
                sb.AppendLine();

                foreach (var entryPoint in entryPoints)
                {
                    GenerateMethodImplementation(sb, entryPoint);
                }

                sb.AppendLine("        #endregion");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        private static void GenerateLibraryImportMethod(StringBuilder sb, EntryPointInfo entryPoint, string libraryConstant)
        {
            var dllImportParameterList = string.Join(", ", entryPoint.Parameters.Select(p => 
                $"{GetDllImportParameterDeclaration(p)} {SanitizeParameterName(p.Name)}"));

            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// AOT-compatible native import for {entryPoint.Name}");
            sb.AppendLine($"        /// </summary>");
            
            var dllImportAttributes = new List<string> { $"EntryPoint = \"{entryPoint.Name}\"" };
            if (!string.IsNullOrEmpty(entryPoint.StringMarshalling))
            {
                dllImportAttributes.Add($"CharSet = CharSet.Ansi");
            }
            
            sb.AppendLine($"        [DllImport({libraryConstant}, {string.Join(", ", dllImportAttributes)})]");
            sb.AppendLine("        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]");
            sb.AppendLine($"        private static extern {entryPoint.ReturnType} {entryPoint.Name}_Import({dllImportParameterList});");
            sb.AppendLine();
        }

        private static void GenerateMethodImplementation(StringBuilder sb, EntryPointInfo entryPoint)
        {
            var parameterList = string.Join(", ", entryPoint.Parameters.Select(p => 
                $"{GetManagedParameterDeclaration(p)} {SanitizeParameterName(p.Name)}"));
            
            var callParameterList = string.Join(", ", entryPoint.Parameters.Select(p => 
            {
                var paramName = SanitizeParameterName(p.Name);
                var flags = p.Flags;
                
                if (!string.IsNullOrEmpty(flags))
                {
                    if (flags.Contains("Out"))
                        return $"out {paramName}";
                    if (flags.Contains("Ref"))
                        return $"ref {paramName}";
                }
                
                return paramName;
            }));

            sb.AppendLine($"        /// <inheritdoc />");
            sb.AppendLine($"        internal sealed override {entryPoint.ReturnType} {entryPoint.Name}({parameterList})");
            sb.AppendLine($"            => {entryPoint.Name}_Import({callParameterList});");
            sb.AppendLine();
        }

        private static void GenerateNotSupportedImplementation(StringBuilder sb, string className, List<EntryPointInfo> entryPoints, string notSupportedException)
        {
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// The AOT-compatible NotSupported implementation of the {className} wrapper.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    sealed unsafe class {className}_AOT_NotSupported : {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        #region RuntimeAPI");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Returns false.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public override bool IsSupported => false;");
            sb.AppendLine();
            sb.AppendLine("        #endregion");
            sb.AppendLine();
            sb.AppendLine("        #region Implementations");
            sb.AppendLine();

            foreach (var entryPoint in entryPoints)
            {
                var parameterList = string.Join(", ", entryPoint.Parameters.Select(p => 
                    $"{GetManagedParameterDeclaration(p)} {SanitizeParameterName(p.Name)}"));

                sb.AppendLine($"        /// <inheritdoc />");
                sb.AppendLine($"        internal sealed override {entryPoint.ReturnType} {entryPoint.Name}({parameterList})");
                sb.AppendLine($"            => throw new NotSupportedException({notSupportedException});");
                sb.AppendLine();
            }

            sb.AppendLine("        #endregion");
            sb.AppendLine("    }");
        }

        private static string GetLibraryConstantReference(string libraryName)
        {
            // Reference the existing constants from the original partial class
            return libraryName switch
            {
                "nvcuda" => "LibNameWindows",
                "cuda" => "LibNameLinux", 
                "opencl" => "LibNameWindows", // OpenCL on Windows
                "OpenCL" => "LibNameLinux",   // OpenCL on Linux/macOS
                _ => $"\"{libraryName}\""     // Fallback to literal string
            };
        }

        private static string GetManagedParameterDeclaration(ParameterInfo parameter)
        {
            var type = parameter.Type;
            var flags = parameter.Flags;

            if (!string.IsNullOrEmpty(flags))
            {
                if (flags.Contains("Out"))
                    return $"out {type}";
                if (flags.Contains("Ref"))
                    return $"ref {type}";
            }

            return type;
        }

        private static string GetNativeParameterDeclaration(ParameterInfo parameter)
        {
            var type = parameter.Type;
            var flags = parameter.Flags;
            var dllFlags = parameter.DllFlags;

            var attributes = new List<string>();

            if (!string.IsNullOrEmpty(dllFlags))
            {
                if (dllFlags.Contains("In"))
                    attributes.Add("In");
                if (dllFlags.Contains("Out"))
                    attributes.Add("Out");
            }

            var attributeString = attributes.Count > 0 ? $"[{string.Join(", ", attributes)}] " : "";

            if (!string.IsNullOrEmpty(flags))
            {
                if (flags.Contains("Out"))
                    return $"{attributeString}out {type}";
                if (flags.Contains("Ref"))
                    return $"{attributeString}ref {type}";
            }

            return $"{attributeString}{type}";
        }

        private static string GetDllImportParameterDeclaration(ParameterInfo parameter)
        {
            var type = parameter.Type;
            var flags = parameter.Flags;

            // Match the parameter directions from the abstract method declarations
            if (!string.IsNullOrEmpty(flags))
            {
                if (flags.Contains("Out"))
                    return $"out {type}";
                if (flags.Contains("Ref"))
                    return $"ref {type}";
            }

            return type;
        }

        private static string SanitizeParameterName(string parameterName)
        {
            // Handle C# keywords and special characters
            if (parameterName == "event")
                return "@event";
            if (parameterName == "object")
                return "@object";
            if (parameterName == "string")
                return "@string";
            
            return parameterName;
        }

        private static Dictionary<string, LibraryImplementation> GetLibraryToImplementationMapping(Dictionary<string, string> libraryNames)
        {
            var result = new Dictionary<string, LibraryImplementation>();
            var idCounter = 0;
            
            // Group platforms by library name
            var libraryToPlatforms = new Dictionary<string, List<string>>();
            
            foreach (var pair in libraryNames)
            {
                var platformName = pair.Key;
                var libName = pair.Value;
                
                if (!libraryToPlatforms.ContainsKey(libName))
                {
                    libraryToPlatforms[libName] = new List<string>();
                }
                libraryToPlatforms[libName].Add(platformName);
            }
            
            // Create implementation for each unique library
            foreach (var kvp in libraryToPlatforms)
            {
                var libName = kvp.Key;
                var platforms = kvp.Value;
                var representativePlatform = platforms.First(); // Use first platform as representative
                
                result[libName] = new LibraryImplementation
                {
                    Id = idCounter++,
                    LibraryName = libName,
                    Platforms = platforms,
                    RepresentativePlatform = representativePlatform
                };
            }

            return result;
        }
    }

    /// <summary>
    /// Information about a native API entry point.
    /// </summary>
    internal class EntryPointInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public string? StringMarshalling { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        public bool IsUnsafe { get; set; }
    }

    /// <summary>
    /// Information about a parameter in a native API method.
    /// </summary>
    internal class ParameterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Flags { get; set; }
        public string? DllFlags { get; set; }
    }

    /// <summary>
    /// Information about a library implementation that combines multiple platforms.
    /// </summary>
    internal class LibraryImplementation
    {
        public int Id { get; set; }
        public string LibraryName { get; set; } = string.Empty;
        public List<string> Platforms { get; set; } = new List<string>();
        public string RepresentativePlatform { get; set; } = string.Empty;
    }
}
