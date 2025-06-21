// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: UnifiedMemoryExtensions.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using System;

namespace ILGPU.Runtime.UnifiedMemory
{
    /// <summary>
    /// Extension methods for unified memory support.
    /// </summary>
    public static class UnifiedMemoryExtensions
    {
        /// <summary>
        /// Allocates a unified memory buffer accessible by both CPU and GPU.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="length">The number of elements to allocate.</param>
        /// <param name="accessMode">The initial access mode.</param>
        /// <returns>A unified memory buffer.</returns>
        public static UnifiedMemoryBuffer1D<T> AllocateUnified<T>(
            this Accelerator accelerator,
            long length,
            UnifiedMemoryAccessMode accessMode = UnifiedMemoryAccessMode.Shared)
            where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            // Always create unified memory buffer (fallback handling is internal)
            return new UnifiedMemoryBuffer1D<T>(accelerator, length, accessMode);
        }

        /// <summary>
        /// Checks if an accelerator supports unified memory.
        /// </summary>
        /// <param name="accelerator">The accelerator to check.</param>
        /// <returns>True if unified memory is supported; otherwise, false.</returns>
        public static bool SupportsUnifiedMemory(this Accelerator accelerator) => accelerator?.Device?.SupportsUnifiedMemory ?? false;

        /// <summary>
        /// Creates a unified memory view from a memory buffer.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="buffer">The memory buffer.</param>
        /// <param name="mode">The unified memory access mode.</param>
        /// <returns>A unified memory view.</returns>
        public static UnifiedArrayView<T> AsUnifiedView<T>(
            this MemoryBuffer1D<T, Stride1D.Dense> buffer,
            UnifiedMemoryAccessMode mode = UnifiedMemoryAccessMode.Shared)
            where T : unmanaged
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            return new UnifiedArrayView<T>(buffer, mode);
        }

        /// <summary>
        /// Performs element-wise operation on unified memory buffers.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="result">The result buffer.</param>
        /// <param name="operation">The operation to perform.</param>
        public static void UnifiedTransform<T>(
            this Accelerator accelerator,
            MemoryBuffer1D<T, Stride1D.Dense> left,
            MemoryBuffer1D<T, Stride1D.Dense> right,
            MemoryBuffer1D<T, Stride1D.Dense> result,
            Func<T, T, T> operation)
            where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (left == null)
                throw new ArgumentNullException(nameof(left));
            if (right == null)
                throw new ArgumentNullException(nameof(right));
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var length = Math.Min(Math.Min(left.Length, right.Length), result.Length);
            
            // Use unified memory operation with automatic CPU/GPU selection
            UnifiedMemoryOperations.Transform(
                left,
                result,
                (index, input, output) =>
                {
                    if (index < length)
                    {
                        output[index] = operation(input[index], right.View[index]);
                    }
                },
                accelerator);
        }

        /// <summary>
        /// Copies data between unified memory buffers with optimizations.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The source buffer.</param>
        /// <param name="destination">The destination buffer.</param>
        /// <param name="stream">The accelerator stream.</param>
        public static void UnifiedCopy<T>(
            this MemoryBuffer1D<T, Stride1D.Dense> source,
            MemoryBuffer1D<T, Stride1D.Dense> destination,
            AcceleratorStream stream)
            where T : unmanaged => UnifiedMemoryOperations.OptimizedCopy(source, destination, stream);
    }
}
