// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: IKernelCache.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ILGPU.Backends;

namespace ILGPU.Runtime.KernelCache
{
    /// <summary>
    /// Represents a kernel cache entry with version information.
    /// </summary>
    public sealed class KernelCacheEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="KernelCacheEntry"/> class.
        /// </summary>
        /// <param name="kernel">The cached kernel.</param>
        /// <param name="version">The kernel version.</param>
        /// <param name="timestamp">The cache timestamp.</param>
        /// <param name="metadata">Optional metadata.</param>
        public KernelCacheEntry(
            object kernel,
            string version,
            DateTime timestamp,
            Dictionary<string, object>? metadata = null)
        {
            Kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Timestamp = timestamp;
            Metadata = metadata ?? new Dictionary<string, object>();
            AccessCount = 0;
            LastAccess = timestamp;
        }

        /// <summary>
        /// Gets the cached kernel.
        /// </summary>
        public object Kernel { get; }

        /// <summary>
        /// Gets the kernel version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the cache timestamp.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Gets the last access time.
        /// </summary>
        public DateTime LastAccess { get; private set; }

        /// <summary>
        /// Gets the access count.
        /// </summary>
        public long AccessCount { get; private set; }

        /// <summary>
        /// Gets the metadata dictionary.
        /// </summary>
        public Dictionary<string, object> Metadata { get; }

        /// <summary>
        /// Records an access to this cache entry.
        /// </summary>
        internal void RecordAccess()
        {
            LastAccess = DateTime.UtcNow;
            AccessCount++;
        }

        /// <summary>
        /// Checks if this entry is expired based on the given TTL.
        /// </summary>
        /// <param name="ttl">The time-to-live duration.</param>
        /// <returns>True if the entry is expired.</returns>
        public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - Timestamp > ttl;
    }

    /// <summary>
    /// Represents cache statistics for monitoring and optimization.
    /// </summary>
    public sealed class KernelCacheStatistics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="KernelCacheStatistics"/> class.
        /// </summary>
        /// <param name="totalHits">Total cache hits.</param>
        /// <param name="totalMisses">Total cache misses.</param>
        /// <param name="totalEvictions">Total cache evictions.</param>
        /// <param name="currentSize">Current cache size.</param>
        /// <param name="maxSize">Maximum cache size.</param>
        /// <param name="averageAccessTime">Average access time in milliseconds.</param>
        public KernelCacheStatistics(
            long totalHits,
            long totalMisses,
            long totalEvictions,
            int currentSize,
            int maxSize,
            double averageAccessTime)
        {
            TotalHits = totalHits;
            TotalMisses = totalMisses;
            TotalEvictions = totalEvictions;
            CurrentSize = currentSize;
            MaxSize = maxSize;
            AverageAccessTime = averageAccessTime;
            HitRatio = totalHits + totalMisses > 0 ? 
                (double)totalHits / (totalHits + totalMisses) : 0.0;
        }

        /// <summary>
        /// Gets the total number of cache hits.
        /// </summary>
        public long TotalHits { get; }

        /// <summary>
        /// Gets the total number of cache misses.
        /// </summary>
        public long TotalMisses { get; }

        /// <summary>
        /// Gets the total number of cache evictions.
        /// </summary>
        public long TotalEvictions { get; }

        /// <summary>
        /// Gets the current cache size.
        /// </summary>
        public int CurrentSize { get; }

        /// <summary>
        /// Gets the maximum cache size.
        /// </summary>
        public int MaxSize { get; }

        /// <summary>
        /// Gets the cache hit ratio (0.0 to 1.0).
        /// </summary>
        public double HitRatio { get; }

        /// <summary>
        /// Gets the average access time in milliseconds.
        /// </summary>
        public double AverageAccessTime { get; }
    }

    /// <summary>
    /// Defines the interface for kernel caching with version management.
    /// </summary>
    public interface IKernelCache : IDisposable
    {
        /// <summary>
        /// Gets the maximum cache size.
        /// </summary>
        int MaxSize { get; }

        /// <summary>
        /// Gets the current cache size.
        /// </summary>
        int CurrentSize { get; }

        /// <summary>
        /// Gets or sets the default time-to-live for cache entries.
        /// </summary>
        TimeSpan DefaultTTL { get; set; }

        /// <summary>
        /// Tries to get a cached kernel by key and version.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="version">The expected version.</param>
        /// <param name="entry">The cached entry if found.</param>
        /// <returns>True if the kernel was found and version matches.</returns>
        bool TryGet(string key, string version, out KernelCacheEntry? entry);

        /// <summary>
        /// Adds or updates a kernel in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="kernel">The compiled kernel.</param>
        /// <param name="version">The kernel version.</param>
        /// <param name="metadata">Optional metadata.</param>
        void Put(string key, object kernel, string version, Dictionary<string, object>? metadata = null);

        /// <summary>
        /// Removes a kernel from the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>True if the kernel was removed.</returns>
        bool Remove(string key);

        /// <summary>
        /// Invalidates all cached kernels with a specific version.
        /// </summary>
        /// <param name="version">The version to invalidate.</param>
        /// <returns>The number of invalidated entries.</returns>
        int InvalidateVersion(string version);

        /// <summary>
        /// Clears all cached kernels.
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        /// <returns>Current cache statistics.</returns>
        KernelCacheStatistics GetStatistics();

        /// <summary>
        /// Performs cache maintenance (removes expired entries, etc.).
        /// </summary>
        /// <returns>The number of entries removed during maintenance.</returns>
        int PerformMaintenance();

        /// <summary>
        /// Asynchronously preloads kernels from persistent storage.
        /// </summary>
        /// <returns>A task representing the preload operation.</returns>
        Task PreloadAsync();

        /// <summary>
        /// Asynchronously persists cache to storage.
        /// </summary>
        /// <returns>A task representing the persist operation.</returns>
        Task PersistAsync();

        /// <summary>
        /// Gets all cache keys.
        /// </summary>
        /// <returns>A collection of all cache keys.</returns>
        IEnumerable<string> GetKeys();

        /// <summary>
        /// Checks if a key exists in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>True if the key exists.</returns>
        bool ContainsKey(string key);
    }

    /// <summary>
    /// Kernel cache configuration options.
    /// </summary>
    public sealed class KernelCacheOptions
    {
        /// <summary>
        /// Gets or sets the maximum cache size (default: 1000).
        /// </summary>
        public int MaxSize { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the default time-to-live for cache entries (default: 24 hours).
        /// </summary>
        public TimeSpan DefaultTTL { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// Gets or sets whether to enable persistent caching (default: true).
        /// </summary>
        public bool EnablePersistentCache { get; set; } = true;

        /// <summary>
        /// Gets or sets the cache directory path.
        /// </summary>
        public string? CacheDirectory { get; set; }

        /// <summary>
        /// Gets or sets the maintenance interval (default: 1 hour).
        /// </summary>
        public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Gets or sets whether to enable automatic maintenance (default: true).
        /// </summary>
        public bool EnableAutomaticMaintenance { get; set; } = true;

        /// <summary>
        /// Gets or sets the LRU eviction threshold as a percentage (default: 0.8).
        /// </summary>
        public double EvictionThreshold { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets whether to enable cache compression (default: true).
        /// </summary>
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable cache encryption (default: false).
        /// </summary>
        public bool EnableEncryption { get; set; } = false;
    }
}
