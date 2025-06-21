// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: AdaptiveMemoryPool.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.MemoryPooling
{
    /// <summary>
    /// An adaptive memory pool that efficiently manages GPU memory buffers with intelligent size bucketing.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class AdaptiveMemoryPool<T> : IMemoryPool<T> where T : unmanaged
    {
        private readonly Accelerator accelerator;
        private readonly MemoryPoolConfiguration config;
        private readonly ConcurrentDictionary<long, ConcurrentQueue<PooledBuffer>> sizeBuckets;
        private readonly ConcurrentDictionary<PooledBuffer, DateTime> rentedBuffers;
        private readonly Timer trimTimer;
        private readonly object statsLock = new();
        
        private long totalAllocations;
        private long totalDeallocations;
        private long totalHits;
        private long totalMisses;
        private long currentPoolSize;
        private volatile bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdaptiveMemoryPool{T}"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator to allocate buffers on.</param>
        /// <param name="configuration">The pool configuration.</param>
        public AdaptiveMemoryPool(Accelerator accelerator, MemoryPoolConfiguration? configuration = null)
        {
            this.accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            config = configuration ?? new MemoryPoolConfiguration();
            config.Validate();

            sizeBuckets = new ConcurrentDictionary<long, ConcurrentQueue<PooledBuffer>>();
            rentedBuffers = new ConcurrentDictionary<PooledBuffer, DateTime>();

            // Set up periodic trimming
            trimTimer = new Timer(TrimCallback, null, config.BufferTrimInterval, config.BufferTrimInterval);

            // Pre-warm common sizes if enabled
            if (config.PrewarmCommonSizes)
            {
                PrewarmCommonSizes();
            }
        }

        /// <inheritdoc/>
        public long MaxPoolSizeBytes => config.MaxPoolSizeBytes;

        /// <inheritdoc/>
        public double UtilizationPercentage => MaxPoolSizeBytes > 0 ? 
            (double)Interlocked.Read(ref currentPoolSize) / MaxPoolSizeBytes * 100.0 : 0.0;

        /// <inheritdoc/>
        public bool IsFull => Interlocked.Read(ref currentPoolSize) >= MaxPoolSizeBytes;

        /// <inheritdoc/>
        public IPooledMemoryBuffer<T> Rent(long minLength)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AdaptiveMemoryPool<T>));

            if (minLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(minLength), "Must be positive");

            if (minLength * Unsafe.SizeOf<T>() > config.MaxBufferSizeBytes)
                throw new ArgumentException($"Requested size exceeds maximum buffer size");

            var bucketSize = GetBucketSize(minLength);
            var queue = sizeBuckets.GetOrAdd(bucketSize, _ => new ConcurrentQueue<PooledBuffer>());

            // Try to get an existing buffer from the pool
            if (queue.TryDequeue(out var pooledBuffer))
            {
                Interlocked.Increment(ref totalHits);
                var rentTime = DateTime.UtcNow;
                rentedBuffers.TryAdd(pooledBuffer, rentTime);
                pooledBuffer.MarkAsRented();
                return pooledBuffer;
            }

            // Pool miss - create a new buffer
            Interlocked.Increment(ref totalMisses);
            Interlocked.Increment(ref totalAllocations);

            var buffer = accelerator.Allocate1D<T>(bucketSize);
            var newPooledBuffer = new PooledBuffer(this, buffer, bucketSize);
            
            Interlocked.Add(ref currentPoolSize, bucketSize * Unsafe.SizeOf<T>());
            
            var newRentTime = DateTime.UtcNow;
            rentedBuffers.TryAdd(newPooledBuffer, newRentTime);
            newPooledBuffer.MarkAsRented();
            
            return newPooledBuffer;
        }

        /// <inheritdoc/>
        public async Task<IPooledMemoryBuffer<T>> RentAsync(long minLength, CancellationToken ct = default) =>
            // For now, the async version is the same as sync since GPU allocation is typically fast
            // In the future, this could implement queuing for when the pool is full
            await Task.Run(() => Rent(minLength), ct).ConfigureAwait(false);

        /// <inheritdoc/>
        public void Return(IPooledMemoryBuffer<T> buffer, bool clearBuffer = false)
        {
            if (buffer is not PooledBuffer pooledBuffer || pooledBuffer.Pool != this)
                throw new ArgumentException("Buffer does not belong to this pool");

            if (pooledBuffer.IsReturned)
                return; // Already returned

            pooledBuffer.MarkAsReturned();
            rentedBuffers.TryRemove(pooledBuffer, out _);

            if (clearBuffer && pooledBuffer.Buffer.Length > 0)
            {
                // Clear the buffer on the GPU
                accelerator.DefaultStream.Synchronize();
                pooledBuffer.Buffer.MemSet(accelerator.DefaultStream, 0);
            }

            var bucketSize = pooledBuffer.ActualLength;
            var queue = sizeBuckets.GetOrAdd(bucketSize, _ => new ConcurrentQueue<PooledBuffer>());

            // Check if we should keep this buffer based on retention policy
            if (ShouldKeepBuffer(bucketSize, queue.Count))
            {
                queue.Enqueue(pooledBuffer);
            }
            else
            {
                // Dispose the buffer and update pool size
                pooledBuffer.Buffer.Dispose();
                Interlocked.Add(ref currentPoolSize, -bucketSize * Unsafe.SizeOf<T>());
                Interlocked.Increment(ref totalDeallocations);
            }
        }

        /// <inheritdoc/>
        public void Trim()
        {
            if (disposed) return;

            var now = DateTime.UtcNow;
            var trimThreshold = now - config.BufferTrimInterval;

            foreach (var kvp in sizeBuckets.ToArray())
            {
                var bucketSize = kvp.Key;
                var queue = kvp.Value;
                var toKeep = new List<PooledBuffer>();

                // Determine how many buffers to keep for this size
                var minToKeep = GetMinBuffersToKeep(bucketSize);
                var keptCount = 0;

                while (queue.TryDequeue(out var buffer) && keptCount < minToKeep)
                {
                    toKeep.Add(buffer);
                    keptCount++;
                }

                // Dispose remaining buffers
                while (queue.TryDequeue(out var buffer))
                {
                    buffer.Buffer.Dispose();
                    Interlocked.Add(ref currentPoolSize, -buffer.ActualLength * Unsafe.SizeOf<T>());
                    Interlocked.Increment(ref totalDeallocations);
                }

                // Re-enqueue kept buffers
                foreach (var buffer in toKeep)
                {
                    queue.Enqueue(buffer);
                }
            }
        }

        /// <inheritdoc/>
        public MemoryPoolStatistics GetStatistics()
        {
            if (!config.EnableStatistics)
            {
                return new MemoryPoolStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0.0, TimeSpan.Zero);
            }

            lock (statsLock)
            {
                var totalBuffers = sizeBuckets.Values.Sum(q => q.Count);
                var rentedCount = rentedBuffers.Count;
                var availableCount = totalBuffers;
                var totalOps = Interlocked.Read(ref totalHits) + Interlocked.Read(ref totalMisses);
                var hitRatio = totalOps > 0 ? (double)Interlocked.Read(ref totalHits) / totalOps : 0.0;
                
                var avgRentTime = TimeSpan.Zero;
                if (rentedBuffers.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    var totalRentTime = rentedBuffers.Values.Sum(startTime => (now - startTime).Ticks);
                    avgRentTime = new TimeSpan(totalRentTime / rentedBuffers.Count);
                }

                var poolSize = Interlocked.Read(ref currentPoolSize);
                
                return new MemoryPoolStatistics(
                    totalPoolSizeBytes: config.MaxPoolSizeBytes,
                    usedSizeBytes: poolSize,
                    availableSizeBytes: config.MaxPoolSizeBytes - poolSize,
                    totalBuffers: totalBuffers + rentedCount,
                    rentedBuffers: rentedCount,
                    availableBuffers: availableCount,
                    totalAllocations: Interlocked.Read(ref totalAllocations),
                    totalDeallocations: Interlocked.Read(ref totalDeallocations),
                    hitRatio: hitRatio,
                    averageRentTime: avgRentTime
                );
            }
        }

        /// <inheritdoc/>
        public void SetRetentionPolicy(PoolRetentionPolicy policy) => config.RetentionPolicy = policy;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            trimTimer?.Dispose();

            // Dispose all pooled buffers
            foreach (var queue in sizeBuckets.Values)
            {
                while (queue.TryDequeue(out var buffer))
                {
                    buffer.Buffer.Dispose();
                }
            }

            // Dispose rented buffers (this is unusual but ensures cleanup)
            foreach (var buffer in rentedBuffers.Keys.ToArray())
            {
                buffer.Buffer.Dispose();
            }

            sizeBuckets.Clear();
            rentedBuffers.Clear();
        }

        private long GetBucketSize(long requestedLength)
        {
            // Align to allocation boundary
            var aligned = AlignUp(requestedLength * Unsafe.SizeOf<T>(), config.AllocationAlignment) / Unsafe.SizeOf<T>();
            
            // Use exponential bucketing for efficient memory usage
            var factor = config.SizeBucketFactor;
            var bucketSize = aligned;
            
            // Round up to the next bucket size
            var logSize = Math.Log(bucketSize) / Math.Log(factor);
            var bucketIndex = (long)Math.Ceiling(logSize);
            
            return (long)Math.Pow(factor, bucketIndex);
        }

        private static long AlignUp(long value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

        private bool ShouldKeepBuffer(long bucketSize, int currentCount) => config.RetentionPolicy switch
        {
            PoolRetentionPolicy.KeepAll => currentCount < config.MaxBuffersPerSize,
            PoolRetentionPolicy.Adaptive => currentCount < GetAdaptiveMaxCount(bucketSize),
            PoolRetentionPolicy.Aggressive => currentCount < config.MinBuffersToKeep,
            PoolRetentionPolicy.Custom => currentCount < config.MaxBuffersPerSize,
            _ => currentCount < config.MaxBuffersPerSize
        };

        private int GetAdaptiveMaxCount(long bucketSize)
        {
            // Larger buffers get fewer slots in the pool
            if (bucketSize * Unsafe.SizeOf<T>() >= config.LargeBufferThreshold)
            {
                return Math.Max(1, config.MaxBuffersPerSize / 4);
            }
            
            return config.MaxBuffersPerSize;
        }

        private int GetMinBuffersToKeep(long bucketSize) => config.RetentionPolicy switch
        {
            PoolRetentionPolicy.KeepAll => config.MaxBuffersPerSize,
            PoolRetentionPolicy.Adaptive => GetAdaptiveMaxCount(bucketSize) / 2,
            PoolRetentionPolicy.Aggressive => 0,
            PoolRetentionPolicy.Custom => config.MinBuffersToKeep,
            _ => config.MinBuffersToKeep
        };

        private void PrewarmCommonSizes()
        {
            // Pre-allocate common buffer sizes
            var commonSizes = new[] { 1024, 4096, 16384, 65536, 262144, 1048576 };
            
            foreach (var size in commonSizes)
            {
                if (size * Unsafe.SizeOf<T>() <= config.MaxBufferSizeBytes)
                {
                    var buffer = Rent(size);
                    Return(buffer);
                }
            }
        }

        private void TrimCallback(object? state)
        {
            try
            {
                if (!disposed)
                {
                    Trim();
                }
            }
            catch
            {
                // Ignore errors in background trimming
            }
        }

        private sealed class PooledBuffer : IPooledMemoryBuffer<T>
        {
            private volatile bool isReturned;

            public PooledBuffer(AdaptiveMemoryPool<T> pool, MemoryBuffer1D<T, Stride1D.Dense> buffer, long actualLength)
            {
                Pool = pool;
                Buffer = buffer;
                ActualLength = actualLength;
            }

            public IMemoryPool<T> Pool { get; }
            public MemoryBuffer1D<T, Stride1D.Dense> Buffer { get; }
            public long ActualLength { get; }
            public bool IsReturned => isReturned;

            // IMemoryBuffer implementation
            public long Length => Buffer.Length;
            public long LengthInBytes => Buffer.LengthInBytes;
            public IntPtr NativePtr => Buffer.NativePtr;
            public bool IsDisposed => Buffer.IsDisposed;
            public Type ElementType => Buffer.ElementType;
            public int Dimensions => Buffer.Dimensions;
            public MemoryBufferStatus Status => Buffer.Status;
            public Accelerator Accelerator => Buffer.Accelerator;
            public int ElementSize => Buffer.ElementSize;

            // ITypedMemoryBuffer<T> implementation
            public ArrayView<T> View => Buffer.AsContiguous();

            public Task<T[]> GetAsArrayAsync(CancellationToken cancellationToken = default)
            {
                var result = new T[Buffer.Length];
                Buffer.AsContiguous().CopyToCPU(result);
                return Task.FromResult(result);
            }
            
            public Task CopyToAsync(ITypedMemoryBuffer<T> destination, CancellationToken cancellationToken = default) =>
                Buffer.CopyToAsync(destination, cancellationToken);
            
            public Task CopyFromAsync(T[] source, CancellationToken cancellationToken = default) =>
                Buffer.CopyFromAsync(source, cancellationToken);

            // IMemoryBuffer async operations
            public Task CopyToAsync(IMemoryBuffer destination, CancellationToken cancellationToken = default) =>
                Buffer.CopyToAsync(destination, cancellationToken);
            
            public Task CopyFromAsync(Array source, CancellationToken cancellationToken = default) =>
                Buffer.CopyFromAsync(source, cancellationToken);
            
            public Task MemSetAsync(byte value, CancellationToken cancellationToken = default) =>
                Buffer.MemSetAsync(value, cancellationToken);
            
            public MemoryUsageInfo GetUsageInfo() => Buffer.GetUsageInfo();
            
            public ArrayView<byte> AsRawArrayView() => Buffer.AsRawArrayView();

            public void MarkAsRented() => isReturned = false;
            public void MarkAsReturned() => isReturned = true;

            public void ReturnToPool(bool clearBuffer = false)
            {
                if (!IsReturned && Pool is AdaptiveMemoryPool<T> pool)
                {
                    pool.Return(this, clearBuffer);
                }
            }

            public void Dispose() => ReturnToPool();
        }
    }
}
