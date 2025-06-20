// ---------------------------------------------------------------------------------------
//                                   ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryPooling.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

#if NET6_0_OR_GREATER

using Microsoft.Extensions.Options;
using ILGPU.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.DependencyInjection
{
    /// <summary>
    /// Interface for memory pool management.
    /// </summary>
    public interface IMemoryPool<T> : IDisposable where T : unmanaged
    {
        /// <summary>
        /// Rents a memory buffer from the pool.
        /// </summary>
        /// <param name="minLength">The minimum required length.</param>
        /// <returns>A memory buffer from the pool.</returns>
        MemoryBuffer1D<T, Stride1D.Dense> Rent(long minLength);

        /// <summary>
        /// Returns a memory buffer to the pool.
        /// </summary>
        /// <param name="buffer">The buffer to return.</param>
        /// <param name="clearBuffer">Whether to clear the buffer contents.</param>
        void Return(MemoryBuffer1D<T, Stride1D.Dense> buffer, bool clearBuffer = false);

        /// <summary>
        /// Trims excess buffers from the pool.
        /// </summary>
        void Trim();

        /// <summary>
        /// Gets statistics about the pool usage.
        /// </summary>
        /// <returns>Pool statistics.</returns>
        MemoryPoolStatistics GetStatistics();

        /// <summary>
        /// Rents a memory buffer from the pool asynchronously.
        /// </summary>
        /// <param name="minLength">The minimum required length.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A task containing a memory buffer from the pool.</returns>
        Task<MemoryBuffer1D<T, Stride1D.Dense>> RentAsync(long minLength, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for memory pool manager.
    /// </summary>
    public interface IMemoryPoolManager
    {
        /// <summary>
        /// Gets a memory pool for the specified type and accelerator.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The target accelerator.</param>
        /// <returns>The memory pool.</returns>
        IMemoryPool<T> GetPool<T>(Accelerator accelerator) where T : unmanaged;

        /// <summary>
        /// Gets global pool statistics.
        /// </summary>
        /// <returns>Global pool statistics.</returns>
        GlobalPoolStatistics GetGlobalStatistics();
    }

    /// <summary>
    /// Memory pool statistics.
    /// </summary>
    public sealed class MemoryPoolStatistics
    {
        /// <summary>
        /// Gets or sets the total number of buffers in the pool.
        /// </summary>
        public int TotalBuffers { get; set; }

        /// <summary>
        /// Gets or sets the number of buffers currently rented.
        /// </summary>
        public int RentedBuffers { get; set; }

        /// <summary>
        /// Gets or sets the total memory allocated in bytes.
        /// </summary>
        public long TotalMemoryBytes { get; set; }

        /// <summary>
        /// Gets or sets the number of pool hits.
        /// </summary>
        public long PoolHits { get; set; }

        /// <summary>
        /// Gets or sets the number of pool misses.
        /// </summary>
        public long PoolMisses { get; set; }

        /// <summary>
        /// Gets the pool hit ratio.
        /// </summary>
        public double HitRatio => PoolHits + PoolMisses > 0 ? (double)PoolHits / (PoolHits + PoolMisses) : 0.0;
    }

    /// <summary>
    /// Global pool statistics.
    /// </summary>
    public sealed class GlobalPoolStatistics
    {
        /// <summary>
        /// Gets or sets the total number of active pools.
        /// </summary>
        public int ActivePools { get; set; }

        /// <summary>
        /// Gets or sets the total memory allocated across all pools.
        /// </summary>
        public long TotalMemoryBytes { get; set; }

        /// <summary>
        /// Gets or sets the global pool hit ratio.
        /// </summary>
        public double GlobalHitRatio { get; set; }
    }

    /// <summary>
    /// Default implementation of memory pool.
    /// </summary>
    internal sealed class DefaultMemoryPool<T> : DisposeBase, IMemoryPool<T> where T : unmanaged
    {
        private readonly Accelerator _accelerator;
        private readonly IOptions<MemoryPoolOptions> _options;
        private readonly ConcurrentQueue<PooledBuffer> _buffers = new();
        private readonly object _lock = new();
        private readonly Timer _trimTimer;
        
        private long _poolHits;
        private long _poolMisses;
        private long _totalMemoryBytes;

        public DefaultMemoryPool(Accelerator accelerator, IOptions<MemoryPoolOptions> options)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Setup trim timer
            var trimInterval = _options.Value.BufferTrimInterval;
            _trimTimer = new Timer(TrimCallback, null, trimInterval, trimInterval);
        }

        public MemoryBuffer1D<T, Stride1D.Dense> Rent(long minLength)
        {
            if (minLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(minLength));

            var maxBufferSize = _options.Value.MaxBufferSizeBytes / (long)Interop.SizeOf<T>();
            if (minLength > maxBufferSize)
            {
                // Buffer too large for pool, allocate directly
                Interlocked.Increment(ref _poolMisses);
                return _accelerator.Allocate1D<T>(minLength);
            }

            // Try to find a suitable buffer in the pool
            if (TryGetFromPool(minLength, out var buffer))
            {
                Interlocked.Increment(ref _poolHits);
                return buffer;
            }

            // No suitable buffer found, allocate new one
            Interlocked.Increment(ref _poolMisses);
            var newBuffer = _accelerator.Allocate1D<T>(minLength);
            Interlocked.Add(ref _totalMemoryBytes, newBuffer.LengthInBytes);
            return newBuffer;
        }

        public void Return(MemoryBuffer1D<T, Stride1D.Dense> buffer, bool clearBuffer = false)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            var maxBufferSize = _options.Value.MaxBufferSizeBytes / (long)Interop.SizeOf<T>();
            if (buffer.Length > maxBufferSize)
            {
                // Buffer too large for pool, dispose directly
                buffer.Dispose();
                return;
            }

            // Check if pool is full
            lock (_lock)
            {
                var currentPoolSize = _buffers.Count * buffer.LengthInBytes;
                if (currentPoolSize >= _options.Value.MaxPoolSizeBytes)
                {
                    // Pool is full, dispose buffer
                    buffer.Dispose();
                    return;
                }
            }

            // Clear buffer if requested
            if (clearBuffer)
            {
                using var stream = _accelerator.CreateStream();
                buffer.MemSetToZero(stream);
                stream.Synchronize();
            }

            // Add to pool
            _buffers.Enqueue(new PooledBuffer(buffer, DateTime.UtcNow));
        }

        public void Trim()
        {
            var retentionPolicy = _options.Value.RetentionPolicy;
            var trimInterval = _options.Value.BufferTrimInterval;
            var cutoffTime = DateTime.UtcNow - trimInterval;

            var buffersToKeep = new List<PooledBuffer>();
            var buffersToDispose = new List<PooledBuffer>();

            // Collect all buffers and decide which to keep
            while (_buffers.TryDequeue(out var pooledBuffer))
            {
                bool shouldKeep = retentionPolicy switch
                {
                    PoolRetentionPolicy.Immediate => false,
                    PoolRetentionPolicy.Fixed => pooledBuffer.ReturnTime > cutoffTime,
                    PoolRetentionPolicy.Adaptive => ShouldKeepBufferAdaptive(pooledBuffer, cutoffTime),
                    _ => true
                };

                if (shouldKeep)
                    buffersToKeep.Add(pooledBuffer);
                else
                    buffersToDispose.Add(pooledBuffer);
            }

            // Re-queue buffers to keep
            foreach (var buffer in buffersToKeep)
                _buffers.Enqueue(buffer);

            // Dispose buffers to remove
            foreach (var buffer in buffersToDispose)
            {
                buffer.Buffer.Dispose();
                Interlocked.Add(ref _totalMemoryBytes, -buffer.Buffer.LengthInBytes);
            }
        }

        public MemoryPoolStatistics GetStatistics() => new()
        {
            TotalBuffers = _buffers.Count,
            RentedBuffers = 0, // This would require additional tracking
            TotalMemoryBytes = Interlocked.Read(ref _totalMemoryBytes),
            PoolHits = Interlocked.Read(ref _poolHits),
            PoolMisses = Interlocked.Read(ref _poolMisses)
        };

        public Task<MemoryBuffer1D<T, Stride1D.Dense>> RentAsync(long minLength, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Rent(minLength);
            }, cancellationToken);
        }

        private bool TryGetFromPool(long minLength, out MemoryBuffer1D<T, Stride1D.Dense> buffer)
        {
            buffer = null!;
            
            while (_buffers.TryDequeue(out var pooledBuffer))
            {
                if (pooledBuffer.Buffer.Length >= minLength)
                {
                    buffer = pooledBuffer.Buffer;
                    return true;
                }
                
                // Buffer too small, put it back
                _buffers.Enqueue(pooledBuffer);
                break; // Avoid infinite loop
            }

            return false;
        }

        private bool ShouldKeepBufferAdaptive(PooledBuffer pooledBuffer, DateTime cutoffTime)
        {
            // Adaptive policy: keep recently used buffers and commonly sized buffers
            var timeSinceReturn = DateTime.UtcNow - pooledBuffer.ReturnTime;
            var hitRatio = GetStatistics().HitRatio;

            // Keep buffers that are recently returned or if hit ratio is high
            return timeSinceReturn < TimeSpan.FromMinutes(2) || hitRatio > 0.7;
        }

        private void TrimCallback(object? state)
        {
            try
            {
                Trim();
            }
            catch
            {
                // Ignore trim errors
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trimTimer?.Dispose();
                
                // Dispose all pooled buffers
                while (_buffers.TryDequeue(out var pooledBuffer))
                {
                    pooledBuffer.Buffer.Dispose();
                }
            }
        }

        private sealed record PooledBuffer(MemoryBuffer1D<T, Stride1D.Dense> Buffer, DateTime ReturnTime);
    }

    /// <summary>
    /// Default implementation of memory pool manager.
    /// </summary>
    internal sealed class DefaultMemoryPoolManager : IMemoryPoolManager
    {
        private readonly ConcurrentDictionary<(Type, Accelerator), object> _pools = new();
        private readonly IOptions<MemoryPoolOptions> _options;

        public DefaultMemoryPoolManager(IOptions<MemoryPoolOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IMemoryPool<T> GetPool<T>(Accelerator accelerator) where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));

            var key = (typeof(T), accelerator);
            return (IMemoryPool<T>)_pools.GetOrAdd(key, _ => new DefaultMemoryPool<T>(accelerator, _options));
        }

        public GlobalPoolStatistics GetGlobalStatistics()
        {
            var activePoolCount = _pools.Count;
            var totalMemory = 0L;
            var totalHits = 0L;
            var totalMisses = 0L;

            foreach (var pool in _pools.Values)
            {
                if (pool is IMemoryPool<int> intPool) // Use a concrete type for statistics
                {
                    var stats = intPool.GetStatistics();
                    totalMemory += stats.TotalMemoryBytes;
                    totalHits += stats.PoolHits;
                    totalMisses += stats.PoolMisses;
                }
            }

            return new GlobalPoolStatistics
            {
                ActivePools = activePoolCount,
                TotalMemoryBytes = totalMemory,
                GlobalHitRatio = totalHits + totalMisses > 0 ? (double)totalHits / (totalHits + totalMisses) : 0.0
            };
        }
    }
}

#endif