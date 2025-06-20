// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: CompiledKernelLauncherBuilder.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.IL;
using ILGPU.Resources;
using ILGPU.Util;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace ILGPU.Runtime
{
    /// <summary>
    /// AOT-compatible builder methods for kernel launchers that replace
    /// System.Reflection.Emit with compile-time source generation.
    /// </summary>
    public static class CompiledKernelLauncherBuilder
    {
        #region Methods

        /// <summary>
        /// Generates code to load a 3D dimension of a grid or a group index.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="indexType">
        /// The index type (can be Index1D, Index2D or Index3D).
        /// </param>
        /// <param name="emitter">The target source code emitter.</param>
        /// <param name="loadIndexExpression">
        /// The expression to load the referenced index value.
        /// </param>
        /// <param name="manipulateIdx">
        /// A callback to manipulate the loaded index of a given dimension.
        /// </param>
        public static void GenerateLoadDimensions<TEmitter>(
            Type indexType,
            in TEmitter emitter,
            string loadIndexExpression,
            Action<int, StringBuilder> manipulateIdx)
            where TEmitter : struct, ISourceEmitter
        {
            var indexProperties = new string[]
            {
                nameof(Index3D.X),
                nameof(Index3D.Y),
                nameof(Index3D.Z),
            };

            // Load field indices
            int offset = 0;
            for (int e = indexProperties.Length; offset < e; ++offset)
            {
                var propertyName = indexProperties[offset];
                if (!HasIndexProperty(indexType, propertyName))
                    break;

                emitter.EmitStatement($"var dim{offset} = {loadIndexExpression}.{propertyName};");
                manipulateIdx(offset, emitter.SourceBuilder);
            }

            // Fill empty dimensions with 1
            for (; offset < indexProperties.Length; ++offset)
            {
                emitter.EmitStatement($"var dim{offset} = 1;");
            }
        }

        /// <summary>
        /// Generates code for loading a <see cref="SharedMemorySpecification"/> instance.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The entry point for code generation.</param>
        /// <param name="emitter">The target source code emitter.</param>
        public static void GenerateSharedMemorySpecification<TEmitter>(
            EntryPoint entryPoint,
            in TEmitter emitter)
            where TEmitter : struct, ISourceEmitter
        {
            emitter.EmitStatement($"var sharedMemSpec = new SharedMemorySpecification({entryPoint.SharedMemory.StaticSize}, {(entryPoint.SharedMemory.HasDynamicMemory ? "true" : "false")});");
        }

        /// <summary>
        /// Generates a kernel-dimension configuration for AOT compilation.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="emitter">The target source code emitter.</param>
        /// <param name="dimensionArgument">
        /// The argument name of the provided launch-dimension index.
        /// </param>
        /// <param name="maxGridSize">The max grid dimensions.</param>
        /// <param name="maxGroupSize">The max group dimensions.</param>
        /// <param name="customGroupSize">
        /// The custom group size used for automatic blocking.
        /// </param>
        public static void GenerateLoadKernelConfig<TEmitter>(
            EntryPoint entryPoint,
            TEmitter emitter,
            string dimensionArgument,
            in Index3D maxGridSize,
            in Index3D maxGroupSize,
            int customGroupSize = 0)
            where TEmitter : struct, ISourceEmitter
        {
            if (entryPoint.IsImplicitlyGrouped)
            {
                Debug.Assert(customGroupSize >= 0, "Invalid custom group size");

                GenerateLoadDimensions(
                    entryPoint.KernelIndexType,
                    emitter,
                    dimensionArgument,
                    (dimIdx, sb) =>
                    {
                        if (dimIdx != 0 || customGroupSize < 1)
                            return;
                        // Convert requested index range to blocked range
                        sb.AppendLine($"            dim{dimIdx} = (dim{dimIdx} + {customGroupSize - 1}) / {customGroupSize};");
                    });

                // Custom grouping
                var groupSize = Math.Max(customGroupSize, 1);
                emitter.EmitStatement($"var groupDim = new Index3D({groupSize}, 1, 1);");
                emitter.EmitStatement($"var gridDim = new Index3D(dim0, dim1, dim2);");

                // Verify the grid and group dimensions
                GenerateVerifyKernelLaunchBounds(emitter, maxGridSize, maxGroupSize);

                // Create the KernelConfig
                emitter.EmitStatement("var kernelConfig = new KernelConfig(gridDim, groupDim);");
            }
            else
            {
                Debug.Assert(customGroupSize == 0, "Invalid custom group size");

                emitter.EmitStatement($"var kernelConfig = {dimensionArgument};");

                // Verify the grid and group dimensions using the values from the KernelConfig
                emitter.EmitStatement("var gridDim = kernelConfig.GridDim;");
                emitter.EmitStatement("var groupDim = kernelConfig.GroupDim;");
                GenerateVerifyKernelLaunchBounds(emitter, maxGridSize, maxGroupSize);
            }
        }

        /// <summary>
        /// Generates code to verify the kernel launch bounds.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="emitter">The target source code emitter.</param>
        /// <param name="maxGridSize">The max grid dimensions.</param>
        /// <param name="maxGroupSize">The max group dimensions.</param>
        private static void GenerateVerifyKernelLaunchBounds<TEmitter>(
            TEmitter emitter,
            in Index3D maxGridSize,
            in Index3D maxGroupSize)
            where TEmitter : struct, ISourceEmitter
        {
            emitter.EmitStatement($"var maxGridSize = new Index3D({maxGridSize.X}, {maxGridSize.Y}, {maxGridSize.Z});");
            emitter.EmitStatement($"var maxGroupSize = new Index3D({maxGroupSize.X}, {maxGroupSize.Y}, {maxGroupSize.Z});");
            emitter.EmitStatement("VerifyKernelLaunchBounds(gridDim, groupDim, maxGridSize, maxGroupSize);");
        }

        /// <summary>
        /// Helper function used to verify the kernel launch dimensions.
        /// </summary>
        /// <param name="gridDim">Kernel launch grid dimensions.</param>
        /// <param name="groupDim">Kernel launch group dimensions.</param>
        /// <param name="maxGridSize">Accelerator max grid dimensions.</param>
        /// <param name="maxGroupSize">Accelerator max group dimensions.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void VerifyKernelLaunchBounds(
            Index3D gridDim,
            Index3D groupDim,
            Index3D maxGridSize,
            Index3D maxGroupSize)
        {
            if (!gridDim.InBoundsInclusive(maxGridSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gridDim),
                    string.Format(
                        RuntimeErrorMessages.InvalidKernelLaunchGridDimension,
                        gridDim,
                        maxGridSize));
            }
            if (!groupDim.InBoundsInclusive(maxGroupSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groupDim),
                    string.Format(
                        RuntimeErrorMessages.InvalidKernelLaunchGroupDimension,
                        groupDim,
                        maxGroupSize));
            }
        }

        /// <summary>
        /// Generates a new runtime kernel configuration.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="emitter">The target source code emitter.</param>
        /// <param name="dimensionArgument">
        /// The argument name of the provided launch-dimension index.
        /// </param>
        /// <param name="maxGridSize">The max grid dimensions.</param>
        /// <param name="maxGroupSize">The max group dimensions.</param>
        /// <param name="customGroupSize">
        /// The custom group size used for automatic blocking.
        /// </param>
        public static void GenerateLoadRuntimeKernelConfig<TEmitter>(
            EntryPoint entryPoint,
            TEmitter emitter,
            string dimensionArgument,
            in Index3D maxGridSize,
            in Index3D maxGroupSize,
            int customGroupSize = 0)
            where TEmitter : struct, ISourceEmitter
        {
            GenerateLoadKernelConfig(
                entryPoint,
                emitter,
                dimensionArgument,
                maxGridSize,
                maxGroupSize,
                customGroupSize);
            GenerateSharedMemorySpecification(entryPoint, emitter);

            emitter.EmitStatement("var runtimeKernelConfig = new RuntimeKernelConfig(kernelConfig, sharedMemSpec);");
        }

        /// <summary>
        /// Generates code for loading a typed kernel from a generic kernel instance.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <typeparam name="T">The kernel type.</typeparam>
        /// <param name="kernelArgumentName">
        /// The name of the launcher parameter.
        /// </param>
        /// <param name="emitter">The target source code emitter.</param>
        public static void GenerateLoadKernelArgument<T, TEmitter>(
            string kernelArgumentName,
            in TEmitter emitter)
            where T : Kernel
            where TEmitter : struct, ISourceEmitter
        {
            emitter.EmitStatement($"var typedKernel = ({typeof(T).Name}){kernelArgumentName};");
        }

        /// <summary>
        /// Generates code for loading a typed accelerator stream from a generic
        /// accelerator-stream instance.
        /// </summary>
        /// <typeparam name="T">The stream type.</typeparam>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="streamArgumentName">The name of the stream parameter.</param>
        /// <param name="emitter">The target source code emitter.</param>
        public static void GenerateLoadAcceleratorStream<T, TEmitter>(
            string streamArgumentName,
            in TEmitter emitter)
            where T : AcceleratorStream
            where TEmitter : struct, ISourceEmitter
        {
            emitter.EmitStatement($"var typedStream = ({typeof(T).Name}){streamArgumentName};");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if the index type has the specified property.
        /// </summary>
        private static bool HasIndexProperty(Type indexType, string propertyName)
        {
            return indexType.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance) != null;
        }

        #endregion
    }

    /// <summary>
    /// Interface for source code emitters used in AOT kernel generation.
    /// </summary>
    public interface ISourceEmitter
    {
        /// <summary>
        /// Returns the underlying source builder.
        /// </summary>
        StringBuilder SourceBuilder { get; }

        /// <summary>
        /// Emits a statement with proper indentation.
        /// </summary>
        /// <param name="statement">The statement to emit.</param>
        void EmitStatement(string statement);
    }

    /// <summary>
    /// Simple source code emitter implementation.
    /// </summary>
    public readonly struct SourceEmitter : ISourceEmitter
    {
        private readonly StringBuilder sourceBuilder;
        private readonly int indentLevel;

        /// <summary>
        /// Constructs a new source emitter.
        /// </summary>
        /// <param name="sourceBuilder">The source builder.</param>
        /// <param name="indentLevel">The indentation level.</param>
        public SourceEmitter(StringBuilder sourceBuilder, int indentLevel = 3)
        {
            this.sourceBuilder = sourceBuilder;
            this.indentLevel = indentLevel;
        }

        /// <summary>
        /// Returns the underlying source builder.
        /// </summary>
        public StringBuilder SourceBuilder => sourceBuilder;

        /// <summary>
        /// Emits a statement with proper indentation.
        /// </summary>
        public void EmitStatement(string statement)
        {
            var indent = new string(' ', indentLevel * 4);
            sourceBuilder.AppendLine($"{indent}{statement}");
        }
    }
}