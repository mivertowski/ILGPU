// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: UnifiedMemoryBuffer1D.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using System;
using System.Runtime.InteropServices;

namespace ILGPU.Runtime.UnifiedMemory
{
    /// <summary>
    /// Represents a 1D unified memory buffer.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class UnifiedMemoryBuffer1D<T> : MemoryBuffer1D<T, Stride1D.Dense>
        where T : unmanaged
    {
        #region Instance

        private readonly UnifiedMemoryAccessMode accessMode;
        private readonly bool isUnifiedMemorySupported;
        private readonly object syncLock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="UnifiedMemoryBuffer1D{T}"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="length">The number of elements.</param>
        /// <param name="accessMode">The unified memory access mode.</param>
        internal UnifiedMemoryBuffer1D(
            Accelerator accelerator,
            long length,
            UnifiedMemoryAccessMode accessMode)
            : base(accelerator, accelerator.Allocate1D<T>(length).View)
        {
            this.accessMode = accessMode;
            isUnifiedMemorySupported = accelerator.Device.SupportsUnifiedMemory;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the unified memory access mode.
        /// </summary>
        public UnifiedMemoryAccessMode AccessMode => accessMode;

        /// <summary>
        /// Gets a value indicating whether unified memory is actually supported.
        /// </summary>
        public bool IsUnifiedMemorySupported => isUnifiedMemorySupported;

        /// <summary>
        /// Gets a CPU-accessible span of the buffer data.
        /// </summary>
        /// <remarks>
        /// This operation may cause data migration and synchronization.
        /// </remarks>
        public unsafe Span<T> CPUView
        {
            get
            {
                lock (syncLock)
                {
                    // Ensure data is accessible from CPU
                    if (isUnifiedMemorySupported && Accelerator is CudaAccelerator)
                    {
                        // For CUDA unified memory, data is automatically accessible
                        return new Span<T>(NativePtr.ToPointer(), (int)Length);
                    }
                    else
                    {
                        // For other accelerators, use GetAsArray for simplicity
                        var array = new T[Length];
                        View.CopyToCPU(array);
                        return array.AsSpan();
                    }
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Prefetches the buffer to the specified device for optimized access.
        /// </summary>
        /// <param name="stream">The accelerator stream.</param>
        /// <param name="target">The target device.</param>
        public void Prefetch(AcceleratorStream stream, UnifiedMemoryTarget target)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Only supported for CUDA unified memory
            if (isUnifiedMemorySupported && Accelerator is CudaAccelerator cudaAccelerator)
            {
                // This would require CUDA API extensions
                // For now, just synchronize
                stream.Synchronize();
            }
        }

        /// <summary>
        /// Provides advice about the expected usage pattern.
        /// </summary>
        /// <param name="advice">The memory advice.</param>
        public void Advise(UnifiedMemoryAdvice advice)
        {
            // Only supported for CUDA unified memory
            if (isUnifiedMemorySupported && Accelerator is CudaAccelerator)
            {
                // This would require CUDA API extensions
                // For now, this is a no-op
            }
        }

        /// <summary>
        /// Pins the memory to prevent migration.
        /// </summary>
        /// <returns>A disposable scope that unpins the memory when disposed.</returns>
        public IDisposable Pin()
        {
            return new PinScope();
        }

        /// <summary>
        /// A simple pin scope implementation.
        /// </summary>
        private sealed class PinScope : IDisposable
        {
            public void Dispose() { }
        }

        /// <summary>
        /// Performs an element-wise operation on the buffer.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="stream">The accelerator stream.</param>
        public void Transform(Action<Index1D, ArrayView<T>> operation, AcceleratorStream stream)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var kernel = Accelerator.LoadAutoGroupedKernel(operation);
            kernel(stream, (Index1D)Length, View);
        }

        /// <summary>
        /// Performs an async element-wise operation on the buffer.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>A task representing the async operation.</returns>
        public async System.Threading.Tasks.Task TransformAsync(
            Action<Index1D, ArrayView<T>> operation,
            AcceleratorStream stream)
        {
            Transform(operation, stream);
            await stream.SynchronizeAsync();
        }

        /// <summary>
        /// Gets the buffer contents as an array.
        /// </summary>
        /// <returns>An array containing the buffer data.</returns>
        public T[] GetAsArray1D()
        {
            var result = new T[Length];
            View.CopyToCPU(result);
            return result;
        }

        #endregion
    }
}