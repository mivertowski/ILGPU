// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: IMemoryBuffer.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Defines the status of a memory buffer.
    /// </summary>
    public enum MemoryBufferStatus
    {
        /// <summary>
        /// The buffer is allocated and ready for use.
        /// </summary>
        Allocated,

        /// <summary>
        /// The buffer is currently being transferred.
        /// </summary>
        Transferring,

        /// <summary>
        /// The buffer has been disposed.
        /// </summary>
        Disposed
    }

    /// <summary>
    /// Represents a unified interface for all memory buffer types in ILGPU.
    /// This interface enables generic programming across different buffer dimensions
    /// and provides modern async operations for .NET 9.
    /// </summary>
    /// <remarks>
    /// This interface addresses the critical issue where different buffer types
    /// (MemoryBuffer1D, MemoryBuffer2D, MemoryBuffer3D) don't share a common interface,
    /// making generic programming difficult.
    /// </remarks>
    public interface IMemoryBuffer : IDisposable
    {
        /// <summary>
        /// Gets the length of this buffer in elements.
        /// </summary>
        long Length { get; }

        /// <summary>
        /// Gets the length of this buffer in bytes.
        /// </summary>
        long LengthInBytes { get; }

        /// <summary>
        /// Gets the native pointer of this buffer.
        /// </summary>
        IntPtr NativePtr { get; }

        /// <summary>
        /// Gets a value indicating whether this buffer has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Gets the element type of this buffer.
        /// </summary>
        Type ElementType { get; }

        /// <summary>
        /// Gets the number of dimensions of this buffer (1, 2, or 3).
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Gets the current status of this buffer.
        /// </summary>
        MemoryBufferStatus Status { get; }

        /// <summary>
        /// Gets the accelerator that owns this buffer.
        /// </summary>
        Accelerator Accelerator { get; }

        /// <summary>
        /// Gets the element size in bytes.
        /// </summary>
        int ElementSize { get; }

        /// <summary>
        /// Asynchronously copies data from this buffer to the destination buffer.
        /// </summary>
        /// <param name="destination">The destination buffer.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        Task CopyToAsync(IMemoryBuffer destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously copies data from the source array to this buffer.
        /// </summary>
        /// <param name="source">The source array.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        Task CopyFromAsync(Array source, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously sets the contents of this buffer to the specified byte value.
        /// </summary>
        /// <param name="value">The byte value to set.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous set operation.</returns>
        Task MemSetAsync(byte value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets usage information about this buffer including allocation time and access patterns.
        /// </summary>
        /// <returns>Memory usage information.</returns>
        MemoryUsageInfo GetUsageInfo();

        /// <summary>
        /// Returns a raw array view of the whole buffer.
        /// </summary>
        /// <returns>The raw array view.</returns>
        ArrayView<byte> AsRawArrayView();
    }

    /// <summary>
    /// Represents a strongly-typed memory buffer interface.
    /// </summary>
    /// <typeparam name="T">The element type of the buffer.</typeparam>
    public interface ITypedMemoryBuffer<T> : IMemoryBuffer where T : unmanaged
    {
        /// <summary>
        /// Gets the array view associated with this buffer.
        /// </summary>
        ArrayView<T> View { get; }

        /// <summary>
        /// Asynchronously retrieves the contents of this buffer as an array.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation that returns the buffer contents as an array.</returns>
        Task<T[]> GetAsArrayAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously copies data from this buffer to the destination buffer with type safety.
        /// </summary>
        /// <param name="destination">The destination buffer.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        Task CopyToAsync(ITypedMemoryBuffer<T> destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously copies data from the source array to this buffer with type safety.
        /// </summary>
        /// <param name="source">The source array.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        Task CopyFromAsync(T[] source, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Provides information about memory buffer usage patterns and performance.
    /// </summary>
    public sealed record MemoryUsageInfo
    {
        /// <summary>
        /// Gets the time when this buffer was allocated.
        /// </summary>
        public DateTime AllocationTime { get; init; }

        /// <summary>
        /// Gets the number of times this buffer has been accessed.
        /// </summary>
        public long AccessCount { get; init; }

        /// <summary>
        /// Gets the last time this buffer was accessed.
        /// </summary>
        public DateTime LastAccessTime { get; init; }

        /// <summary>
        /// Gets the total bytes transferred to/from this buffer.
        /// </summary>
        public long TotalBytesTransferred { get; init; }

        /// <summary>
        /// Gets the peak memory usage of this buffer in bytes.
        /// </summary>
        public long PeakMemoryUsage { get; init; }

        /// <summary>
        /// Gets whether this buffer is currently pinned in memory.
        /// </summary>
        public bool IsPinned { get; init; }
    }
}