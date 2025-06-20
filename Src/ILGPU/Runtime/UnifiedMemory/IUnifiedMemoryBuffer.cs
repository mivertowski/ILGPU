// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: IUnifiedMemoryBuffer.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.UnifiedMemory
{
    /// <summary>
    /// Represents the access mode for unified memory.
    /// </summary>
    public enum UnifiedMemoryAccessMode
    {
        /// <summary>
        /// Memory is accessible by both CPU and GPU with automatic migration.
        /// </summary>
        Shared,

        /// <summary>
        /// Memory is optimized for GPU access.
        /// </summary>
        DevicePreferred,

        /// <summary>
        /// Memory is optimized for CPU access.
        /// </summary>
        HostPreferred,

        /// <summary>
        /// Memory migration is managed explicitly by the application.
        /// </summary>
        Explicit
    }

    /// <summary>
    /// Represents a unified memory buffer that can be accessed by both CPU and GPU.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public interface IUnifiedMemoryBuffer<T> : IDisposable
        where T : unmanaged
    {
        /// <summary>
        /// Gets the length of the buffer in elements.
        /// </summary>
        long Length { get; }

        /// <summary>
        /// Gets the size of the buffer in bytes.
        /// </summary>
        long LengthInBytes { get; }

        /// <summary>
        /// Gets the current access mode of the buffer.
        /// </summary>
        UnifiedMemoryAccessMode AccessMode { get; }

        /// <summary>
        /// Gets a value indicating whether the buffer is currently accessible from the CPU.
        /// </summary>
        bool IsAccessibleFromCPU { get; }

        /// <summary>
        /// Gets a value indicating whether the buffer is currently accessible from the GPU.
        /// </summary>
        bool IsAccessibleFromGPU { get; }

        /// <summary>
        /// Gets the associated accelerator.
        /// </summary>
        Accelerator Accelerator { get; }

        /// <summary>
        /// Prefetches the buffer to the specified device for optimized access.
        /// </summary>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="targetDevice">The target device (CPU or GPU).</param>
        void Prefetch(AcceleratorStream stream, UnifiedMemoryTarget targetDevice);

        /// <summary>
        /// Prefetches the buffer to the specified device asynchronously.
        /// </summary>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="targetDevice">The target device (CPU or GPU).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the async operation.</returns>
        Task PrefetchAsync(
            AcceleratorStream stream,
            UnifiedMemoryTarget targetDevice,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Advises the runtime about the expected usage pattern of the buffer.
        /// </summary>
        /// <param name="advice">The memory advice hint.</param>
        void Advise(UnifiedMemoryAdvice advice);

        /// <summary>
        /// Gets a CPU-accessible view of the buffer.
        /// </summary>
        /// <returns>A span representing the buffer data.</returns>
        Span<T> GetCPUView();

        /// <summary>
        /// Gets a GPU-accessible view of the buffer.
        /// </summary>
        /// <returns>An array view for GPU kernel access.</returns>
        ArrayView<T> GetGPUView();

        /// <summary>
        /// Synchronizes the buffer between CPU and GPU.
        /// </summary>
        /// <param name="stream">The accelerator stream to use.</param>
        void Synchronize(AcceleratorStream stream);

        /// <summary>
        /// Synchronizes the buffer between CPU and GPU asynchronously.
        /// </summary>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the async operation.</returns>
        Task SynchronizeAsync(
            AcceleratorStream stream,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies data from this buffer to another unified memory buffer.
        /// </summary>
        /// <param name="target">The target buffer.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        void CopyTo(IUnifiedMemoryBuffer<T> target, AcceleratorStream stream);

        /// <summary>
        /// Copies data from this buffer to a regular memory buffer.
        /// </summary>
        /// <param name="target">The target buffer.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        void CopyTo(MemoryBuffer1D<T, Stride1D.Dense> target, AcceleratorStream stream);

        /// <summary>
        /// Pins the buffer memory to prevent migration during an operation.
        /// </summary>
        /// <returns>A disposable scope that unpins the memory when disposed.</returns>
        IDisposable Pin();
    }

    /// <summary>
    /// Represents the target device for unified memory operations.
    /// </summary>
    public enum UnifiedMemoryTarget
    {
        /// <summary>
        /// Target the CPU/host.
        /// </summary>
        Host,

        /// <summary>
        /// Target the GPU/device.
        /// </summary>
        Device,

        /// <summary>
        /// Let the runtime decide the best target.
        /// </summary>
        Auto
    }

    /// <summary>
    /// Provides advice hints for unified memory usage patterns.
    /// </summary>
    public enum UnifiedMemoryAdvice
    {
        /// <summary>
        /// Default behavior with no specific advice.
        /// </summary>
        None,

        /// <summary>
        /// Data will be mostly read.
        /// </summary>
        ReadMostly,

        /// <summary>
        /// Data will be accessed by a specific processor.
        /// </summary>
        PreferredLocation,

        /// <summary>
        /// Data access pattern is random.
        /// </summary>
        RandomAccess,

        /// <summary>
        /// Data access pattern is sequential.
        /// </summary>
        SequentialAccess,

        /// <summary>
        /// Data will be accessed frequently.
        /// </summary>
        AccessedFrequently
    }
}