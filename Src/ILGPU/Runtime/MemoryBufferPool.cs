// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryBufferPool.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ILGPU.Runtime
{
    /// <summary>
    /// High-performance memory buffer pool that reduces GC pressure and improves
    /// allocation performance through sophisticated buffer reuse strategies.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class MemoryBufferPool<T> : DisposeBase
        where T : unmanaged
    {
        #region Constants

        /// <summary>
        /// Standard bucket sizes for optimal memory usage patterns.
        /// </summary>
        private static readonly long[] BucketSizes = new long[]
        {
            1024,       // 1KB
            4096,       // 4KB  
            16384,      // 16KB
            65536,      // 64KB
            262144,     // 256KB
            1048576,    // 1MB
            4194304,    // 4MB
            16777216,   // 16MB
            67108864,   // 64MB
        };

        /// <summary>
        /// Maximum number of buffers to keep in each bucket.
        /// </summary>
        private const int MaxBuffersPerBucket = 8;

        /// <summary>
        /// Interval for automatic pool trimming (in milliseconds).
        /// </summary>
        private const int TrimIntervalMs = 30000; // 30 seconds

        #endregion

        #region Instance

        private readonly Accelerator accelerator;
        private readonly ConcurrentQueue<PooledBuffer>[] buckets;
        private readonly Timer trimTimer;
        private readonly object trimLock = new object();

        // Performance tracking
        private long totalAllocations;
        private long totalHits;
        private long totalMisses;
        private long totalBytesAllocated;
        private long totalBytesReused;

        /// <summary>
        /// Constructs a new memory buffer pool for the specified accelerator.
        /// </summary>
        /// <param name="accelerator">The accelerator to allocate buffers for.</param>
        public MemoryBufferPool(Accelerator accelerator)
        {
            this.accelerator = accelerator;
            buckets = new ConcurrentQueue<PooledBuffer>[BucketSizes.Length];
            
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new ConcurrentQueue<PooledBuffer>();
            }

            // Set up automatic pool trimming
            trimTimer = new Timer(TrimPoolCallback, null, TrimIntervalMs, TrimIntervalMs);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the pool hit ratio (0.0 to 1.0).
        /// </summary>
        public double HitRatio
        {
            get
            {
                var total = Volatile.Read(ref totalAllocations);
                if (total == 0) return 0.0;
                return (double)Volatile.Read(ref totalHits) / total;
            }
        }

        /// <summary>
        /// Returns the total number of allocations requested.
        /// </summary>
        public long TotalAllocations => Volatile.Read(ref totalAllocations);

        /// <summary>
        /// Returns the total bytes allocated (including pooled and new).
        /// </summary>
        public long TotalBytesAllocated => Volatile.Read(ref totalBytesAllocated);

        /// <summary>
        /// Returns the total bytes reused from the pool.
        /// </summary>
        public long TotalBytesReused => Volatile.Read(ref totalBytesReused);

        #endregion

        #region Methods

        /// <summary>
        /// Rents a memory buffer of the specified size from the pool.
        /// </summary>
        /// <param name="length">The minimum number of elements required.</param>
        /// <returns>A pooled memory buffer that must be returned via <see cref="Return"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PooledMemoryBuffer<T> Rent(long length)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            Interlocked.Increment(ref totalAllocations);
            var bytesRequested = length * Interop.SizeOf<T>();
            Interlocked.Add(ref totalBytesAllocated, bytesRequested);

            var bucketIndex = GetBucketIndex(length);
            if (bucketIndex >= 0 && buckets[bucketIndex].TryDequeue(out var pooledBuffer))
            {
                // Pool hit - reuse existing buffer
                Interlocked.Increment(ref totalHits);
                Interlocked.Add(ref totalBytesReused, pooledBuffer.Buffer.LengthInBytes);
                
                pooledBuffer.Reset();
                return new PooledMemoryBuffer<T>(this, pooledBuffer.Buffer, bucketIndex);
            }

            // Pool miss - allocate new buffer
            Interlocked.Increment(ref totalMisses);
            var actualSize = bucketIndex >= 0 ? BucketSizes[bucketIndex] : length;
            var newBuffer = accelerator.Allocate1D<T>(actualSize);
            
            return new PooledMemoryBuffer<T>(this, newBuffer, bucketIndex);
        }

        /// <summary>
        /// Returns a buffer to the pool for reuse.
        /// </summary>
        /// <param name="buffer">The buffer to return.</param>
        /// <param name="bucketIndex">The bucket index this buffer belongs to.</param>
        internal void Return(MemoryBuffer1D<T, Stride1D.Dense> buffer, int bucketIndex)
        {
            if (bucketIndex < 0 || bucketIndex >= buckets.Length)
            {
                // Buffer doesn't fit in any bucket - dispose immediately
                buffer.Dispose();
                return;
            }

            var bucket = buckets[bucketIndex];
            if (GetBucketCount(bucket) < MaxBuffersPerBucket)
            {
                // Add to pool for reuse
                bucket.Enqueue(new PooledBuffer(buffer, Environment.TickCount64));
            }
            else
            {
                // Bucket is full - dispose the buffer
                buffer.Dispose();
            }
        }

        /// <summary>
        /// Trims the pool by removing old or unused buffers.
        /// </summary>
        public void TrimPool()
        {
            if (!Monitor.TryEnter(trimLock, 100))
                return; // Another trim is in progress

            try
            {
                var currentTime = Environment.TickCount64;
                const long maxAge = 60000; // 1 minute

                for (int bucketIndex = 0; bucketIndex < buckets.Length; bucketIndex++)
                {
                    var bucket = buckets[bucketIndex];
                    var itemsToKeep = new List<PooledBuffer>();

                    // Examine all items in the bucket
                    while (bucket.TryDequeue(out var item))
                    {
                        if (currentTime - item.LastUsed < maxAge && itemsToKeep.Count < MaxBuffersPerBucket / 2)
                        {
                            itemsToKeep.Add(item);
                        }
                        else
                        {
                            // Item is too old or we have enough items - dispose it
                            item.Buffer.Dispose();
                        }
                    }

                    // Put the items we're keeping back into the bucket
                    foreach (var item in itemsToKeep)
                    {
                        bucket.Enqueue(item);
                    }
                }
            }
            finally
            {
                Monitor.Exit(trimLock);
            }
        }

        /// <summary>
        /// Gets statistics about the memory pool usage.
        /// </summary>
        /// <returns>Pool usage statistics.</returns>
        public MemoryPoolStatistics GetStatistics()
        {
            var bucketCounts = new int[buckets.Length];
            var bucketSizes = new long[buckets.Length];
            long totalPooledBytes = 0;

            for (int i = 0; i < buckets.Length; i++)
            {
                bucketCounts[i] = GetBucketCount(buckets[i]);
                bucketSizes[i] = BucketSizes[i];
                totalPooledBytes += bucketCounts[i] * bucketSizes[i] * Interop.SizeOf<T>();
            }

            return new MemoryPoolStatistics(
                TotalAllocations,
                Volatile.Read(ref totalHits),
                Volatile.Read(ref totalMisses),
                HitRatio,
                TotalBytesAllocated,
                TotalBytesReused,
                totalPooledBytes,
                bucketCounts,
                bucketSizes);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the appropriate bucket index for a given size.
        /// </summary>
        /// <param name="length">The requested length.</param>
        /// <returns>The bucket index, or -1 if no suitable bucket exists.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucketIndex(long length)
        {
            for (int i = 0; i < BucketSizes.Length; i++)
            {
                if (length <= BucketSizes[i])
                    return i;
            }
            return -1; // Too large for any bucket
        }

        /// <summary>
        /// Gets the approximate count of items in a bucket.
        /// </summary>
        /// <param name="bucket">The bucket to count.</param>
        /// <returns>The approximate count.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucketCount(ConcurrentQueue<PooledBuffer> bucket) =>
            // Note: ConcurrentQueue.Count can be expensive, but we use it for pool management
            bucket.Count;

        /// <summary>
        /// Callback for the automatic pool trimming timer.
        /// </summary>
        private void TrimPoolCallback(object? state)
        {
            try
            {
                TrimPool();
            }
            catch (Exception ex)
            {
                // Log the exception but don't let it crash the timer
                Debug.WriteLine($"MemoryBufferPool trim failed: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes the memory buffer pool and all pooled buffers.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trimTimer?.Dispose();

                // Dispose all pooled buffers
                for (int i = 0; i < buckets.Length; i++)
                {
                    var bucket = buckets[i];
                    while (bucket.TryDequeue(out var item))
                    {
                        item.Buffer.Dispose();
                    }
                }
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents a buffer stored in the pool.
        /// </summary>
        private readonly struct PooledBuffer
        {
            public PooledBuffer(MemoryBuffer1D<T, Stride1D.Dense> buffer, long lastUsed)
            {
                Buffer = buffer;
                LastUsed = lastUsed;
            }

            public MemoryBuffer1D<T, Stride1D.Dense> Buffer { get; }
            public long LastUsed { get; }

            public void Reset()
            {
                // Could implement buffer clearing here if needed for security
            }
        }

        #endregion
    }

    /// <summary>
    /// A memory buffer that is automatically returned to the pool when disposed.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public readonly struct PooledMemoryBuffer<T> : IDisposable
        where T : unmanaged
    {
        private readonly MemoryBufferPool<T>? pool;
        private readonly MemoryBuffer1D<T, Stride1D.Dense> buffer;
        private readonly int bucketIndex;

        internal PooledMemoryBuffer(
            MemoryBufferPool<T> pool, 
            MemoryBuffer1D<T, Stride1D.Dense> buffer, 
            int bucketIndex)
        {
            this.pool = pool;
            this.buffer = buffer;
            this.bucketIndex = bucketIndex;
        }

        /// <summary>
        /// Returns the underlying memory buffer.
        /// </summary>
        public MemoryBuffer1D<T, Stride1D.Dense> Buffer => buffer;

        /// <summary>
        /// Returns the buffer to the pool.
        /// </summary>
        public void Dispose()
        {
            if (pool != null)
            {
                pool.Return(buffer, bucketIndex);
            }
            else
            {
                buffer.Dispose();
            }
        }

        /// <summary>
        /// Implicit conversion to the underlying buffer.
        /// </summary>
        public static implicit operator MemoryBuffer1D<T, Stride1D.Dense>(PooledMemoryBuffer<T> pooled) => pooled.buffer;
    }

    /// <summary>
    /// Statistics about memory pool usage.
    /// </summary>
    public readonly struct MemoryPoolStatistics
    {
        public MemoryPoolStatistics(
            long totalAllocations,
            long totalHits,
            long totalMisses,
            double hitRatio,
            long totalBytesAllocated,
            long totalBytesReused,
            long totalPooledBytes,
            int[] bucketCounts,
            long[] bucketSizes)
        {
            TotalAllocations = totalAllocations;
            TotalHits = totalHits;
            TotalMisses = totalMisses;
            HitRatio = hitRatio;
            TotalBytesAllocated = totalBytesAllocated;
            TotalBytesReused = totalBytesReused;
            TotalPooledBytes = totalPooledBytes;
            BucketCounts = bucketCounts;
            BucketSizes = bucketSizes;
        }

        public long TotalAllocations { get; }
        public long TotalHits { get; }
        public long TotalMisses { get; }
        public double HitRatio { get; }
        public long TotalBytesAllocated { get; }
        public long TotalBytesReused { get; }
        public long TotalPooledBytes { get; }
        public int[] BucketCounts { get; }
        public long[] BucketSizes { get; }
    }
}