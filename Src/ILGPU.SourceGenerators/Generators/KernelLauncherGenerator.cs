// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: KernelLauncherGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.SourceGenerators.Analysis;
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
    /// Source generator that creates AOT-compatible kernel launchers to replace
    /// System.Reflection.Emit based runtime code generation.
    /// </summary>
    // [Generator] - Temporarily disabled due to generic type issues
    public sealed class KernelLauncherGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all methods marked with kernel attributes
            var kernelMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: IsKernelMethodCandidate,
                    transform: GetKernelMethodInfo)
                .Where(static m => m is not null);

            // Generate launchers for each kernel method
            context.RegisterSourceOutput(kernelMethods.Collect(), GenerateKernelLaunchers);
        }

        private static bool IsKernelMethodCandidate(SyntaxNode node, CancellationToken cancellationToken)
        {
            // Look for static methods that could be kernel methods
            if (node is not MethodDeclarationSyntax methodSyntax)
                return false;

            // Must be static
            if (!methodSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                return false;

            // Must return void
            if (methodSyntax.ReturnType is not PredefinedTypeSyntax returnType ||
                !returnType.Keyword.IsKind(SyntaxKind.VoidKeyword))
                return false;

            // Check for attributes that indicate this is a kernel method
            return methodSyntax.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => IsKernelAttribute(attr));
        }

        private static bool IsKernelAttribute(AttributeSyntax attribute)
        {
            var name = attribute.Name.ToString();
            return name.Contains("Kernel") || 
                   name.Contains("GPU") || 
                   name.Contains("ComputeKernel");
        }

        private static KernelMethodInfo? GetKernelMethodInfo(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var methodSyntax = (MethodDeclarationSyntax)context.Node;
            var semanticModel = context.SemanticModel;

            var analyzer = new KernelMethodAnalyzer(semanticModel.Compilation, semanticModel);
            var analysisResult = analyzer.AnalyzeKernelMethod(methodSyntax);

            if (!analysisResult.IsValid)
            {
                // Report diagnostic for invalid kernel
                var diagnostic = Diagnostic.Create(
                    Descriptors.InvalidKernelMethod,
                    methodSyntax.Identifier.GetLocation(),
                    analysisResult.Error);
                // Note: In a real implementation, we'd report this diagnostic
                return null;
            }

            return new KernelMethodInfo(
                methodSyntax,
                analysisResult.MethodSymbol!,
                analysisResult.ParameterAnalysis!,
                analysisResult.BodyAnalysis!);
        }

        private static void GenerateKernelLaunchers(SourceProductionContext context, ImmutableArray<KernelMethodInfo?> kernelMethods)
        {
            var validKernels = kernelMethods.Where(k => k is not null).Cast<KernelMethodInfo>().ToList();

            if (!validKernels.Any())
                return;

            // Group kernels by containing class
            var kernelsByClass = validKernels.GroupBy(k => k.MethodSymbol.ContainingType);

            foreach (var classGroup in kernelsByClass)
            {
                var containingType = classGroup.Key;
                var kernelsInClass = classGroup.ToList();

                var sourceCode = GenerateKernelLauncherClass(containingType, kernelsInClass);
                var fileName = $"{containingType.ToDisplayString().Replace('.', '_')}_Launchers.g.cs";

                context.AddSource(fileName, SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private static string GenerateKernelLauncherClass(INamedTypeSymbol containingType, List<KernelMethodInfo> kernels)
        {
            var sb = new StringBuilder();

            // File header
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// This file was generated by ILGPU.SourceGenerators.KernelLauncherGenerator");
            sb.AppendLine();

            // Usings
            sb.AppendLine("using System;");
            sb.AppendLine("using ILGPU;");
            sb.AppendLine("using ILGPU.Runtime;");
            sb.AppendLine();

            // Namespace
            var namespaceName = containingType.ContainingNamespace.ToDisplayString();
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // Launcher class
            var className = $"{containingType.Name}Launchers";
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// AOT-compatible kernel launchers for {containingType.Name}");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static partial class {className}");
            sb.AppendLine("    {");

            // Generate launcher methods for each kernel
            foreach (var kernel in kernels)
            {
                GenerateKernelLauncherMethod(sb, kernel);
                sb.AppendLine();
            }

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateKernelLauncherMethod(StringBuilder sb, KernelMethodInfo kernel)
        {
            var methodName = kernel.MethodSymbol.Name;
            var parameters = kernel.ParameterAnalysis.Parameters;

            // Method signature
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// AOT-compatible launcher for {methodName} kernel");
            sb.AppendLine($"        /// </summary>");
            sb.Append($"        public static void Launch{methodName}(");
            sb.Append("AcceleratorStream stream, KernelConfig config");

            // Add kernel parameters
            foreach (var param in parameters)
            {
                sb.Append($", {param.Type.ToDisplayString()} {param.Symbol.Name}");
            }
            sb.AppendLine(")");

            sb.AppendLine("        {");
            
            // Generate launcher body
            sb.AppendLine("            // AOT-compatible kernel launch implementation");
            sb.AppendLine($"            // This replaces the dynamic IL generation for {methodName}");
            sb.AppendLine();
            
            // For now, generate a placeholder that demonstrates the structure
            sb.AppendLine("            // TODO: Implement actual AOT kernel launch logic");
            sb.AppendLine($"            throw new NotImplementedException(\"AOT kernel launcher for {methodName} not yet implemented\");");
            
            sb.AppendLine("        }");
        }
    }

    /// <summary>
    /// Information about a kernel method for code generation.
    /// </summary>
    internal sealed class KernelMethodInfo
    {
        public MethodDeclarationSyntax MethodSyntax { get; }
        public IMethodSymbol MethodSymbol { get; }
        public ParameterAnalysisResult ParameterAnalysis { get; }
        public MethodBodyAnalysisResult BodyAnalysis { get; }

        public KernelMethodInfo(
            MethodDeclarationSyntax methodSyntax,
            IMethodSymbol methodSymbol,
            ParameterAnalysisResult parameterAnalysis,
            MethodBodyAnalysisResult bodyAnalysis)
        {
            MethodSyntax = methodSyntax;
            MethodSymbol = methodSymbol;
            ParameterAnalysis = parameterAnalysis;
            BodyAnalysis = bodyAnalysis;
        }
    }

    /// <summary>
    /// Diagnostic descriptors for the kernel launcher generator.
    /// </summary>
    internal static class Descriptors
    {
        public static readonly DiagnosticDescriptor InvalidKernelMethod = new DiagnosticDescriptor(
            "ILGPU001",
            "Invalid kernel method for AOT compilation",
            "Kernel method '{0}' cannot be used in AOT compilation: {1}",
            "ILGPU.AOT",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KernelLauncherGenerated = new DiagnosticDescriptor(
            "ILGPU002",
            "AOT kernel launcher generated",
            "Generated AOT-compatible launcher for kernel method '{0}'",
            "ILGPU.AOT",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);
    }
}