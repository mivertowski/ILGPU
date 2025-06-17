// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeDiscoveryGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace ILGPU.SourceGenerators.Generators
{
    /// <summary>
    /// Source generator that creates compile-time type discovery to replace
    /// runtime reflection-based type discovery in AOT scenarios.
    /// </summary>
    // [Generator] - Temporarily disabled due to generic type issues
    public sealed class TypeDiscoveryGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all types that might be used in kernels
            var kernelTypes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: IsKernelTypeCandidate,
                    transform: GetKernelTypeInfo)
                .Where(static t => t is not null);

            // Generate type discovery registry
            context.RegisterSourceOutput(kernelTypes.Collect(), GenerateTypeRegistry);
        }

        private static bool IsKernelTypeCandidate(SyntaxNode node, CancellationToken cancellationToken)
        {
            // Look for structs, classes, and enums that might be used in kernels
            return node is StructDeclarationSyntax ||
                   node is ClassDeclarationSyntax ||
                   node is EnumDeclarationSyntax;
        }

        private static KernelTypeInfo? GetKernelTypeInfo(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var typeSymbol = context.SemanticModel.GetDeclaredSymbol(context.Node);
            if (typeSymbol is not INamedTypeSymbol namedType)
                return null;

            // Check if this type is likely to be used in kernels
            if (IsKernelCompatibleType(namedType))
            {
                return new KernelTypeInfo(namedType, AnalyzeTypeForKernelUsage(namedType));
            }

            return null;
        }

        private static bool IsKernelCompatibleType(INamedTypeSymbol type)
        {
            // Check if type is suitable for GPU kernels
            if (type.TypeKind == TypeKind.Struct)
                return true; // Structs are generally kernel-compatible

            if (type.TypeKind == TypeKind.Enum)
                return true; // Enums are kernel-compatible

            // Check for specific ILGPU types
            var typeName = type.ToDisplayString();
            if (typeName.Contains("ArrayView") || 
                typeName.Contains("MemoryBuffer") ||
                typeName.Contains("Index") ||
                typeName.Contains("Stride"))
                return true;

            return false;
        }

        private static KernelTypeAnalysis AnalyzeTypeForKernelUsage(INamedTypeSymbol type)
        {
            var analysis = new KernelTypeAnalysis();

            // Analyze size and alignment
            if (type.TypeKind == TypeKind.Struct)
            {
                analysis.IsValueType = true;
                analysis.CanBePassedByValue = true;
                
                // Check for problematic members
                foreach (var member in type.GetMembers())
                {
                    if (member is IFieldSymbol field)
                    {
                        if (field.Type.TypeKind == TypeKind.Class && field.Type.SpecialType != SpecialType.System_String)
                        {
                            analysis.HasReferenceTypes = true;
                        }
                    }
                }
            }

            // Check for unmanaged constraint
            if (type.IsUnmanagedType)
            {
                analysis.IsUnmanaged = true;
            }

            return analysis;
        }

        private static void GenerateTypeRegistry(SourceProductionContext context, ImmutableArray<KernelTypeInfo?> types)
        {
            var validTypes = types.Where(t => t is not null).Cast<KernelTypeInfo>().ToList();

            if (!validTypes.Any())
                return;

            var sourceCode = GenerateTypeRegistryClass(validTypes);
            context.AddSource("KernelTypeRegistry.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }

        private static string GenerateTypeRegistryClass(IEnumerable<KernelTypeInfo> types)
        {
            var sb = new StringBuilder();

            // File header
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// This file was generated by ILGPU.SourceGenerators.TypeDiscoveryGenerator");
            sb.AppendLine();

            // Usings
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using ILGPU;");
            sb.AppendLine();

            // Namespace
            sb.AppendLine("namespace ILGPU.Runtime.Generated");
            sb.AppendLine("{");

            // Registry class
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// AOT-compatible type registry for kernel compilation");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class KernelTypeRegistry");
            sb.AppendLine("    {");

            // Generate type information
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Gets all kernel-compatible types discovered at compile time");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static IReadOnlyDictionary<Type, KernelTypeMetadata> RegisteredTypes { get; }");
            sb.AppendLine();

            // Static constructor
            sb.AppendLine("        static KernelTypeRegistry()");
            sb.AppendLine("        {");
            sb.AppendLine("            var registry = new Dictionary<Type, KernelTypeMetadata>();");
            sb.AppendLine();

            // Add each type to registry
            foreach (var type in types)
            {
                GenerateTypeRegistration(sb, type);
            }

            sb.AppendLine();
            sb.AppendLine("            RegisteredTypes = registry;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Helper methods
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Gets metadata for a specific type if it's kernel-compatible");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static KernelTypeMetadata? GetTypeMetadata(Type type)");
            sb.AppendLine("        {");
            sb.AppendLine("            return RegisteredTypes.TryGetValue(type, out var metadata) ? metadata : null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Checks if a type is kernel-compatible");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static bool IsKernelCompatible(Type type)");
            sb.AppendLine("        {");
            sb.AppendLine("            return RegisteredTypes.ContainsKey(type);");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine();

            // Metadata class
            GenerateKernelTypeMetadataClass(sb);

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void GenerateTypeRegistration(StringBuilder sb, KernelTypeInfo type)
        {
            var typeName = type.TypeSymbol.ToDisplayString();
            sb.AppendLine($"            // Register {typeName}");
            sb.AppendLine($"            registry[typeof({typeName})] = new KernelTypeMetadata");
            sb.AppendLine("            {");
            sb.AppendLine($"                TypeName = \"{typeName}\",");
            sb.AppendLine($"                IsValueType = {type.Analysis.IsValueType.ToString().ToLower()},");
            sb.AppendLine($"                IsUnmanaged = {type.Analysis.IsUnmanaged.ToString().ToLower()},");
            sb.AppendLine($"                CanBePassedByValue = {type.Analysis.CanBePassedByValue.ToString().ToLower()},");
            sb.AppendLine($"                HasReferenceTypes = {type.Analysis.HasReferenceTypes.ToString().ToLower()}");
            sb.AppendLine("            };");
            sb.AppendLine();
        }

        private static void GenerateKernelTypeMetadataClass(StringBuilder sb)
        {
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Metadata about a kernel-compatible type");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public sealed class KernelTypeMetadata");
            sb.AppendLine("    {");
            sb.AppendLine("        public required string TypeName { get; init; }");
            sb.AppendLine("        public required bool IsValueType { get; init; }");
            sb.AppendLine("        public required bool IsUnmanaged { get; init; }");
            sb.AppendLine("        public required bool CanBePassedByValue { get; init; }");
            sb.AppendLine("        public required bool HasReferenceTypes { get; init; }");
            sb.AppendLine("    }");
        }
    }

    /// <summary>
    /// Information about a type for kernel usage analysis.
    /// </summary>
    internal sealed class KernelTypeInfo
    {
        public INamedTypeSymbol TypeSymbol { get; }
        public KernelTypeAnalysis Analysis { get; }

        public KernelTypeInfo(INamedTypeSymbol typeSymbol, KernelTypeAnalysis analysis)
        {
            TypeSymbol = typeSymbol;
            Analysis = analysis;
        }
    }

    /// <summary>
    /// Analysis result for kernel type compatibility.
    /// </summary>
    internal sealed class KernelTypeAnalysis
    {
        public bool IsValueType { get; set; }
        public bool IsUnmanaged { get; set; }
        public bool CanBePassedByValue { get; set; }
        public bool HasReferenceTypes { get; set; }
    }
}