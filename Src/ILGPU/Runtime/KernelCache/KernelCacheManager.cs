// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: KernelCacheManager.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ILGPU.Backends;

namespace ILGPU.Runtime.KernelCache
{
    /// <summary>
    /// High-performance kernel cache implementation with LRU eviction and version management.
    /// </summary>
    public sealed class KernelCacheManager : IKernelCache
    {
        #region Fields

        private readonly ConcurrentDictionary<string, KernelCacheEntry> cache;
        private readonly ConcurrentDictionary<string, DateTime> accessTimes;
        private readonly KernelCacheOptions options;
        private readonly Timer? maintenanceTimer;
        private readonly object lockObject = new();

        private long totalHits;
        private long totalMisses;
        private long totalEvictions;
        private readonly List<double> accessTimeHistory = new();
        private bool disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="KernelCacheManager"/> class.
        /// </summary>
        /// <param name="options">Cache configuration options.</param>
        public KernelCacheManager(KernelCacheOptions? options = null)
        {
            this.options = options ?? new KernelCacheOptions();
            cache = new ConcurrentDictionary<string, KernelCacheEntry>();
            accessTimes = new ConcurrentDictionary<string, DateTime>();

            if (this.options.EnableAutomaticMaintenance)
            {
                maintenanceTimer = new Timer(
                    PerformMaintenanceCallback,
                    null,
                    this.options.MaintenanceInterval,
                    this.options.MaintenanceInterval);
            }

            // Initialize cache directory if persistent caching is enabled
            if (this.options.EnablePersistentCache && !string.IsNullOrEmpty(this.options.CacheDirectory))
            {
                Directory.CreateDirectory(this.options.CacheDirectory);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the maximum cache size.
        /// </summary>
        public int MaxSize => options.MaxSize;

        /// <summary>
        /// Gets the current cache size.
        /// </summary>
        public int CurrentSize => cache.Count;

        /// <summary>
        /// Gets or sets the default time-to-live for cache entries.
        /// </summary>
        public TimeSpan DefaultTTL
        {
            get => options.DefaultTTL;
            set => options.DefaultTTL = value;
        }

        #endregion

        #region IKernelCache Implementation

        /// <summary>
        /// Tries to get a cached kernel by key and version.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="version">The expected version.</param>
        /// <param name="entry">The cached entry if found.</param>
        /// <returns>True if the kernel was found and version matches.</returns>
        public bool TryGet(string key, string version, out KernelCacheEntry? entry)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));
            if (string.IsNullOrEmpty(version))
                throw new ArgumentException("Version cannot be null or empty", nameof(version));

            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (cache.TryGetValue(key, out entry))
                {
                    // Check version match
                    if (entry.Version == version)
                    {
                        // Check expiration
                        if (!entry.IsExpired(DefaultTTL))
                        {
                            entry.RecordAccess();
                            accessTimes[key] = DateTime.UtcNow;
                            Interlocked.Increment(ref totalHits);
                            return true;
                        }
                        else
                        {
                            // Remove expired entry
                            cache.TryRemove(key, out _);
                            accessTimes.TryRemove(key, out _);
                        }
                    }
                    else
                    {
                        // Version mismatch - remove old entry
                        cache.TryRemove(key, out _);
                        accessTimes.TryRemove(key, out _);
                    }
                }

                entry = null;
                Interlocked.Increment(ref totalMisses);
                return false;
            }
            finally
            {
                stopwatch.Stop();
                lock (lockObject)
                {
                    accessTimeHistory.Add(stopwatch.Elapsed.TotalMilliseconds);
                    if (accessTimeHistory.Count > 1000)
                    {
                        accessTimeHistory.RemoveAt(0);
                    }
                }
            }
        }

        /// <summary>
        /// Adds or updates a kernel in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="kernel">The compiled kernel.</param>
        /// <param name="version">The kernel version.</param>
        /// <param name="metadata">Optional metadata.</param>
        public void Put(string key, object kernel, string version, Dictionary<string, object>? metadata = null)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));
            if (kernel == null)
                throw new ArgumentNullException(nameof(kernel));
            if (string.IsNullOrEmpty(version))
                throw new ArgumentException("Version cannot be null or empty", nameof(version));

            var entry = new KernelCacheEntry(kernel, version, DateTime.UtcNow, metadata);
            
            // Check if cache is full and needs eviction
            if (cache.Count >= MaxSize * options.EvictionThreshold)
            {
                EvictLRUEntries();
            }

            cache.AddOrUpdate(key, entry, (k, oldEntry) => entry);
            accessTimes[key] = DateTime.UtcNow;
        }

        /// <summary>
        /// Removes a kernel from the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>True if the kernel was removed.</returns>
        public bool Remove(string key)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var removed = cache.TryRemove(key, out _);
            if (removed)
            {
                accessTimes.TryRemove(key, out _);
            }
            return removed;
        }

        /// <summary>
        /// Invalidates all cached kernels with a specific version.
        /// </summary>
        /// <param name="version">The version to invalidate.</param>
        /// <returns>The number of invalidated entries.</returns>
        public int InvalidateVersion(string version)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));
            if (string.IsNullOrEmpty(version))
                throw new ArgumentException("Version cannot be null or empty", nameof(version));

            var keysToRemove = cache
                .Where(kvp => kvp.Value.Version == version)
                .Select(kvp => kvp.Key)
                .ToList();

            int removedCount = 0;
            foreach (var key in keysToRemove)
            {
                if (Remove(key))
                {
                    removedCount++;
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Clears all cached kernels.
        /// </summary>
        public void Clear()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));

            cache.Clear();
            accessTimes.Clear();
        }

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        /// <returns>Current cache statistics.</returns>
        public KernelCacheStatistics GetStatistics()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));

            double averageAccessTime;
            lock (lockObject)
            {
                averageAccessTime = accessTimeHistory.Count > 0 ? 
                    accessTimeHistory.Average() : 0.0;
            }

            return new KernelCacheStatistics(
                totalHits,
                totalMisses,
                totalEvictions,
                CurrentSize,
                MaxSize,
                averageAccessTime);
        }

        /// <summary>
        /// Performs cache maintenance (removes expired entries, etc.).
        /// </summary>
        /// <returns>The number of entries removed during maintenance.</returns>
        public int PerformMaintenance()
        {
            if (disposed)
                return 0;

            var expiredKeys = cache
                .Where(kvp => kvp.Value.IsExpired(DefaultTTL))
                .Select(kvp => kvp.Key)
                .ToList();

            int removedCount = 0;
            foreach (var key in expiredKeys)
            {
                if (Remove(key))
                {
                    removedCount++;
                }
            }

            // Perform LRU eviction if cache is still too large
            if (cache.Count > MaxSize)
            {
                removedCount += EvictLRUEntries();
            }

            return removedCount;
        }

        /// <summary>
        /// Asynchronously preloads kernels from persistent storage.
        /// </summary>
        /// <returns>A task representing the preload operation.</returns>
        public async Task PreloadAsync()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));

            if (!options.EnablePersistentCache || string.IsNullOrEmpty(options.CacheDirectory))
                return;

            await Task.Run(() =>
            {
                try
                {
                    var cacheFiles = Directory.GetFiles(options.CacheDirectory, "*.cache");
                    foreach (var file in cacheFiles)
                    {
                        // Implementation would load serialized cache entries
                        // This is a placeholder for the actual deserialization logic
                    }
                }
                catch (Exception)
                {
                    // Log error but don't throw - preloading is best-effort
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously persists cache to storage.
        /// </summary>
        /// <returns>A task representing the persist operation.</returns>
        public async Task PersistAsync()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));

            if (!options.EnablePersistentCache || string.IsNullOrEmpty(options.CacheDirectory))
                return;

            await Task.Run(() =>
            {
                try
                {
                    foreach (var kvp in cache)
                    {
                        var fileName = Path.Combine(options.CacheDirectory, $"{kvp.Key}.cache");
                        // Implementation would serialize cache entries
                        // This is a placeholder for the actual serialization logic
                    }
                }
                catch (Exception)
                {
                    // Log error but don't throw - persistence is best-effort
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets all cache keys.
        /// </summary>
        /// <returns>A collection of all cache keys.</returns>
        public IEnumerable<string> GetKeys()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));

            return cache.Keys.ToList();
        }

        /// <summary>
        /// Checks if a key exists in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>True if the key exists.</returns>
        public bool ContainsKey(string key)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(KernelCacheManager));
            if (string.IsNullOrEmpty(key))
                return false;

            return cache.ContainsKey(key);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Evicts least recently used entries to make room in the cache.
        /// </summary>
        /// <returns>The number of evicted entries.</returns>
        private int EvictLRUEntries()
        {
            var targetSize = (int)(MaxSize * 0.7); // Evict to 70% capacity
            var entriesToRemove = cache.Count - targetSize;
            
            if (entriesToRemove <= 0)
                return 0;

            var lruEntries = accessTimes
                .OrderBy(kvp => kvp.Value)
                .Take(entriesToRemove)
                .Select(kvp => kvp.Key)
                .ToList();

            int evictedCount = 0;
            foreach (var key in lruEntries)
            {
                if (Remove(key))
                {
                    evictedCount++;
                    Interlocked.Increment(ref totalEvictions);
                }
            }

            return evictedCount;
        }

        /// <summary>
        /// Timer callback for automatic maintenance.
        /// </summary>
        /// <param name="state">Timer state (unused).</param>
        private void PerformMaintenanceCallback(object? state)
        {
            try
            {
                PerformMaintenance();
            }
            catch (Exception)
            {
                // Log error but don't throw from timer callback
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases all resources used by the <see cref="KernelCacheManager"/>.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                maintenanceTimer?.Dispose();
                
                // Attempt to persist cache before disposal
                try
                {
                    PersistAsync().Wait(TimeSpan.FromSeconds(10));
                }
                catch (Exception)
                {
                    // Best effort - don't throw in Dispose
                }

                Clear();
                disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for kernel caching functionality.
    /// </summary>
    public static class KernelCacheExtensions
    {
        /// <summary>
        /// Creates a cache key from kernel information.
        /// </summary>
        /// <param name="kernelName">The kernel name.</param>
        /// <param name="parameters">The kernel parameters.</param>
        /// <param name="deviceInfo">The device information.</param>
        /// <returns>A unique cache key.</returns>
        public static string CreateCacheKey(string kernelName, object[] parameters, string deviceInfo)
        {
            var keyBuilder = new System.Text.StringBuilder();
            keyBuilder.Append(kernelName);
            keyBuilder.Append('|');
            keyBuilder.Append(deviceInfo);
            keyBuilder.Append('|');
            
            foreach (var param in parameters)
            {
                keyBuilder.Append(param?.GetType().FullName ?? "null");
                keyBuilder.Append(',');
            }

            return keyBuilder.ToString();
        }

        /// <summary>
        /// Creates a version string from compilation parameters.
        /// </summary>
        /// <param name="compilerVersion">The compiler version.</param>
        /// <param name="optimizationLevel">The optimization level.</param>
        /// <param name="targetArchitecture">The target architecture.</param>
        /// <returns>A version string.</returns>
        public static string CreateVersionString(string compilerVersion, string optimizationLevel, string targetArchitecture) => $"{compilerVersion}_{optimizationLevel}_{targetArchitecture}";
    }
}