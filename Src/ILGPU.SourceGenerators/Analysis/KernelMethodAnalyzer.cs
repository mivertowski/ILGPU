// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: KernelMethodAnalyzer.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace ILGPU.SourceGenerators.Analysis
{
    /// <summary>
    /// Analyzes kernel methods for AOT compatibility and code generation requirements.
    /// </summary>
    internal sealed class KernelMethodAnalyzer
    {
        private readonly Compilation compilation;
        private readonly SemanticModel semanticModel;

        public KernelMethodAnalyzer(Compilation compilation, SemanticModel semanticModel)
        {
            this.compilation = compilation;
            this.semanticModel = semanticModel;
        }

        /// <summary>
        /// Analyzes a method to determine if it's a valid kernel method for AOT compilation.
        /// </summary>
        public KernelAnalysisResult AnalyzeKernelMethod(MethodDeclarationSyntax methodSyntax)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax);
            if (methodSymbol == null)
                return KernelAnalysisResult.Failed("Method symbol not found");

            // Check if method is static
            if (!methodSymbol.IsStatic)
                return KernelAnalysisResult.Failed("Kernel methods must be static");

            // Check return type (should be void)
            if (!methodSymbol.ReturnsVoid)
                return KernelAnalysisResult.Failed("Kernel methods must return void");

            // Analyze parameters for AOT compatibility
            var parameterAnalysis = AnalyzeParameters(methodSymbol.Parameters);
            if (!parameterAnalysis.IsValid)
                return KernelAnalysisResult.Failed(parameterAnalysis.Error);

            // Analyze method body for unsupported features
            var bodyAnalysis = AnalyzeMethodBody(methodSyntax);
            if (!bodyAnalysis.IsValid)
                return KernelAnalysisResult.Failed(bodyAnalysis.Error);

            return KernelAnalysisResult.Success(methodSymbol, parameterAnalysis, bodyAnalysis);
        }

        /// <summary>
        /// Analyzes kernel method parameters for AOT compatibility.
        /// </summary>
        private ParameterAnalysisResult AnalyzeParameters(IEnumerable<IParameterSymbol> parameters)
        {
            var analyzedParameters = new List<AnalyzedParameter>();

            foreach (var parameter in parameters)
            {
                var parameterType = parameter.Type;
                
                // Check for supported parameter types
                if (IsArrayViewType(parameterType))
                {
                    analyzedParameters.Add(new AnalyzedParameter(
                        parameter, 
                        ParameterKind.ArrayView, 
                        GetArrayViewElementType(parameterType)));
                }
                else if (IsPrimitiveType(parameterType))
                {
                    analyzedParameters.Add(new AnalyzedParameter(
                        parameter, 
                        ParameterKind.Primitive, 
                        parameterType));
                }
                else if (IsStructType(parameterType))
                {
                    analyzedParameters.Add(new AnalyzedParameter(
                        parameter, 
                        ParameterKind.Struct, 
                        parameterType));
                }
                else
                {
                    return ParameterAnalysisResult.Failed(
                        $"Unsupported parameter type '{parameterType}' for parameter '{parameter.Name}'");
                }
            }

            return ParameterAnalysisResult.Success(analyzedParameters);
        }

        /// <summary>
        /// Analyzes the method body for AOT-incompatible features.
        /// </summary>
        private MethodBodyAnalysisResult AnalyzeMethodBody(MethodDeclarationSyntax methodSyntax)
        {
            var bodyAnalysis = new MethodBodyAnalysisResult();

            // Check for dynamic allocations (new keyword)
            var dynamicAllocations = methodSyntax.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .ToList();

            if (dynamicAllocations.Any())
            {
                bodyAnalysis.AddWarning("Dynamic object allocation found - may not be AOT compatible");
            }

            // Check for reflection usage
            var memberAccess = methodSyntax.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(ma => IsReflectionAPI(ma))
                .ToList();

            if (memberAccess.Any())
            {
                return MethodBodyAnalysisResult.Failed("Reflection API usage not allowed in AOT kernels");
            }

            // Check for delegate creation
            var delegateCreations = methodSyntax.DescendantNodes()
                .OfType<AnonymousFunctionExpressionSyntax>()
                .ToList();

            if (delegateCreations.Any())
            {
                return MethodBodyAnalysisResult.Failed("Anonymous functions not supported in AOT kernels");
            }

            return bodyAnalysis;
        }

        private bool IsArrayViewType(ITypeSymbol type)
        {
            // Check if type is ArrayView<T> or similar ILGPU view types
            return type.Name.Contains("ArrayView") || type.Name.Contains("MemoryBuffer");
        }

        private ITypeSymbol? GetArrayViewElementType(ITypeSymbol arrayViewType)
        {
            // Extract T from ArrayView<T>
            if (arrayViewType is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
                return namedType.TypeArguments[0];
            return null;
        }

        private bool IsPrimitiveType(ITypeSymbol type)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Boolean => true,
                SpecialType.System_Byte => true,
                SpecialType.System_SByte => true,
                SpecialType.System_Int16 => true,
                SpecialType.System_UInt16 => true,
                SpecialType.System_Int32 => true,
                SpecialType.System_UInt32 => true,
                SpecialType.System_Int64 => true,
                SpecialType.System_UInt64 => true,
                SpecialType.System_Single => true,
                SpecialType.System_Double => true,
                _ => false
            };
        }

        private bool IsStructType(ITypeSymbol type)
        {
            return type.TypeKind == TypeKind.Struct && !IsPrimitiveType(type);
        }

        private bool IsReflectionAPI(MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.ValueText;
            return memberName.Contains("GetType") || 
                   memberName.Contains("GetMethod") || 
                   memberName.Contains("GetField") ||
                   memberName.Contains("CreateInstance");
        }
    }
}
