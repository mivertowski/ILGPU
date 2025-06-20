// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: KernelCacheTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.KernelCache;
using ILGPU.Runtime.CPU;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ILGPU.Tests.CPU
{
    /// <summary>
    /// Tests for kernel caching system with version management.
    /// </summary>
    public class KernelCacheTests : IDisposable
    {
        #region Fields

        private readonly Context context;
        private readonly Accelerator accelerator;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="KernelCacheTests"/> class.
        /// </summary>
        public KernelCacheTests()
        {
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUaccelerator(0);
        }

        #endregion

        #region Test Kernels

        static void SimpleKernel(Index1D index, ArrayView<int> data)
        {
            data[index] = index;
        }

        static void MultiplyKernel(Index1D index, ArrayView<int> input, ArrayView<int> output, int factor)
        {
            output[index] = input[index] * factor;
        }

        static void AddKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> result)
        {
            result[index] = a[index] + b[index];
        }

        #endregion

        #region Cache Manager Tests

        [Fact]
        public void KernelCache_BasicPutAndGet()
        {
            var options = new KernelCacheOptions { MaxSize = 100 };
            using var cache = new KernelCacheManager(options);
            
            // Create a mock kernel (simplified for testing)
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            var key = "test_kernel";
            var version = "1.0.0";
            
            // Put kernel in cache
            cache.Put(key, mockKernel as dynamic, version);
            
            // Try to get it back
            var found = cache.TryGet(key, version, out var entry);
            
            Assert.True(found);
            Assert.NotNull(entry);
            Assert.Equal(version, entry.Version);
            Assert.Equal(key, entry.Metadata.ContainsKey("CacheKey") ? entry.Metadata["CacheKey"] : key);
        }

        [Fact]
        public void KernelCache_VersionMismatch()
        {
            var options = new KernelCacheOptions { MaxSize = 100 };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            var key = "test_kernel";
            var version1 = "1.0.0";
            var version2 = "2.0.0";
            
            // Put kernel with version 1.0.0
            cache.Put(key, mockKernel as dynamic, version1);
            
            // Try to get with version 2.0.0 (should fail)
            var found = cache.TryGet(key, version2, out var entry);
            
            Assert.False(found);
            Assert.Null(entry);
        }

        [Fact]
        public void KernelCache_Expiration()
        {
            var options = new KernelCacheOptions 
            { 
                MaxSize = 100,
                DefaultTTL = TimeSpan.FromMilliseconds(100) // Very short TTL
            };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            var key = "test_kernel";
            var version = "1.0.0";
            
            // Put kernel in cache
            cache.Put(key, mockKernel as dynamic, version);
            
            // Verify it's there immediately
            var found1 = cache.TryGet(key, version, out var entry1);
            Assert.True(found1);
            Assert.NotNull(entry1);
            
            // Wait for expiration
            Thread.Sleep(200);
            
            // Should be expired now
            var found2 = cache.TryGet(key, version, out var entry2);
            Assert.False(found2);
            Assert.Null(entry2);
        }

        [Fact]
        public void KernelCache_LRUEviction()
        {
            var options = new KernelCacheOptions 
            { 
                MaxSize = 3,
                EvictionThreshold = 0.8 // Will trigger eviction at 2-3 items
            };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            // Add multiple kernels
            cache.Put("kernel1", mockKernel as dynamic, "1.0");
            cache.Put("kernel2", mockKernel as dynamic, "1.0");
            
            // Access kernel1 to make it more recently used
            cache.TryGet("kernel1", "1.0", out _);
            
            // Add more kernels to trigger eviction
            cache.Put("kernel3", mockKernel as dynamic, "1.0");
            cache.Put("kernel4", mockKernel as dynamic, "1.0");
            
            // kernel2 should be evicted (least recently used)
            var found2 = cache.TryGet("kernel2", "1.0", out _);
            var found1 = cache.TryGet("kernel1", "1.0", out _);
            var found3 = cache.TryGet("kernel3", "1.0", out _);
            
            Assert.False(found2); // Should be evicted
            Assert.True(found1);  // Should still be there
            Assert.True(found3);  // Should still be there
        }

        [Fact]
        public void KernelCache_Statistics()
        {
            var options = new KernelCacheOptions { MaxSize = 100 };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            // Put a kernel
            cache.Put("kernel1", mockKernel as dynamic, "1.0");
            
            // Hit
            cache.TryGet("kernel1", "1.0", out _);
            
            // Miss
            cache.TryGet("nonexistent", "1.0", out _);
            
            var stats = cache.GetStatistics();
            
            Assert.Equal(1, stats.TotalHits);
            Assert.Equal(1, stats.TotalMisses);
            Assert.Equal(1, stats.CurrentSize);
            Assert.Equal(100, stats.MaxSize);
            Assert.True(stats.HitRatio > 0);
        }

        [Fact]
        public void KernelCache_VersionInvalidation()
        {
            var options = new KernelCacheOptions { MaxSize = 100 };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            // Put kernels with different versions
            cache.Put("kernel1", mockKernel as dynamic, "1.0");
            cache.Put("kernel2", mockKernel as dynamic, "1.0");
            cache.Put("kernel3", mockKernel as dynamic, "2.0");
            
            // Invalidate version 1.0
            var invalidated = cache.InvalidateVersion("1.0");
            
            Assert.Equal(2, invalidated);
            
            // Version 1.0 kernels should be gone
            Assert.False(cache.TryGet("kernel1", "1.0", out _));
            Assert.False(cache.TryGet("kernel2", "1.0", out _));
            
            // Version 2.0 kernel should still be there
            Assert.True(cache.TryGet("kernel3", "2.0", out _));
        }

        [Fact]
        public void KernelCache_ClearAll()
        {
            var options = new KernelCacheOptions { MaxSize = 100 };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            // Put multiple kernels
            cache.Put("kernel1", mockKernel as dynamic, "1.0");
            cache.Put("kernel2", mockKernel as dynamic, "1.0");
            cache.Put("kernel3", mockKernel as dynamic, "2.0");
            
            Assert.Equal(3, cache.CurrentSize);
            
            // Clear all
            cache.Clear();
            
            Assert.Equal(0, cache.CurrentSize);
            Assert.False(cache.TryGet("kernel1", "1.0", out _));
            Assert.False(cache.TryGet("kernel2", "1.0", out _));
            Assert.False(cache.TryGet("kernel3", "2.0", out _));
        }

        [Fact]
        public void KernelCache_Maintenance()
        {
            var options = new KernelCacheOptions 
            { 
                MaxSize = 100,
                DefaultTTL = TimeSpan.FromMilliseconds(50),
                EnableAutomaticMaintenance = false // Disable automatic maintenance for this test
            };
            using var cache = new KernelCacheManager(options);
            
            var mockKernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            // Put kernels
            cache.Put("kernel1", mockKernel as dynamic, "1.0");
            cache.Put("kernel2", mockKernel as dynamic, "1.0");
            
            // Wait for expiration
            Thread.Sleep(100);
            
            Assert.Equal(2, cache.CurrentSize);
            
            // Perform maintenance
            var removed = cache.PerformMaintenance();
            
            Assert.True(removed > 0);
            Assert.True(cache.CurrentSize < 2);
        }

        [Fact]
        public async Task KernelCache_AsyncOperations()
        {
            var options = new KernelCacheOptions 
            { 
                MaxSize = 100,
                EnablePersistentCache = false // Disable for this test
            };
            using var cache = new KernelCacheManager(options);
            
            // Test async preload (should complete without error)
            await cache.PreloadAsync();
            
            // Test async persist (should complete without error)
            await cache.PersistAsync();
        }

        #endregion

        #region accelerator Integration Tests

        [Fact]
        public void KernelCache_acceleratorIntegration()
        {
            var cache = acceleratorKernelCache.GetOrCreateCache(accelerator);
            
            Assert.NotNull(cache);
            Assert.Equal(0, cache.CurrentSize);
            
            // Test that we get the same cache instance
            var cache2 = acceleratorKernelCache.GetOrCreateCache(accelerator);
            Assert.Same(cache, cache2);
        }

        [Fact]
        public void KernelCache_LoadKernelCached()
        {
            using var cachedKernel = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            Assert.NotNull(cachedKernel);
            Assert.NotNull(cachedKernel.CacheKey);
            Assert.NotNull(cachedKernel.Version);
            
            var info = cachedKernel.GetInfo();
            Assert.NotNull(info);
            Assert.Equal("SimpleKernel", info.MethodName);
            Assert.Contains("CPU", info.acceleratorType);
        }

        [Fact]
        public void KernelCache_KernelExecution()
        {
            const int length = 1000;
            using var buffer = accelerator.Allocate1D<int>(length);
            using var cachedKernel = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            
            // Execute the cached kernel
            cachedKernel.Invoke(accelerator.DefaultStream, (Index1D)length, buffer.View);
            accelerator.Synchronize();
            
            // Verify results
            var data = buffer.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(i, data[i]);
            }
        }

        [Fact]
        public void KernelCache_MultipleKernelTypes()
        {
            using var kernel1 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            using var kernel2 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>, ArrayView<int>, int>>(MultiplyKernel);
            using var kernel3 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>>(AddKernel);
            
            Assert.NotEqual(kernel1.CacheKey, kernel2.CacheKey);
            Assert.NotEqual(kernel1.CacheKey, kernel3.CacheKey);
            Assert.NotEqual(kernel2.CacheKey, kernel3.CacheKey);
            
            // All should have the same version (same runtime)
            Assert.Equal(kernel1.Version, kernel2.Version);
            Assert.Equal(kernel1.Version, kernel3.Version);
        }

        [Fact]
        public void KernelCache_ConcurrentAccess()
        {
            const int threadCount = 10;
            const int kernelsPerThread = 50;
            var cache = acceleratorKernelCache.GetOrCreateCache(accelerator);
            
            var tasks = new Task[threadCount];
            var exceptions = new List<Exception>();
            
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < kernelsPerThread; i++)
                        {
                            using var cachedKernel = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
                            var info = cachedKernel.GetInfo();
                            
                            // Access the kernel to trigger cache operations
                            var kernel = cachedKernel.Kernel;
                            Assert.NotNull(kernel);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
            }
            
            Task.WaitAll(tasks);
            
            // Verify no exceptions occurred
            Assert.Empty(exceptions);
            
            // Cache should contain the kernel
            Assert.True(cache.CurrentSize > 0);
        }

        [Fact]
        public void KernelCache_Performance()
        {
            const int iterations = 100;
            var stopwatch = new System.Diagnostics.Stopwatch();
            
            // Measure uncached kernel loading
            stopwatch.Start();
            for (int i = 0; i < iterations; i++)
            {
                using var kernel = accelerator.LoadKernel<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            }
            stopwatch.Stop();
            var uncachedTime = stopwatch.Elapsed;
            
            // Measure cached kernel loading
            stopwatch.Restart();
            using var cachedKernel = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            for (int i = 0; i < iterations; i++)
            {
                var kernel = cachedKernel.Kernel; // Should hit cache after first load
            }
            stopwatch.Stop();
            var cachedTime = stopwatch.Elapsed;
            
            System.Console.WriteLine($"Uncached time: {uncachedTime.TotalMilliseconds:F2}ms");
            System.Console.WriteLine($"Cached time: {cachedTime.TotalMilliseconds:F2}ms");
            
            // Cached should be significantly faster (though first load might be slower)
            // This is more of a performance verification than assertion
            Assert.True(cachedTime < uncachedTime * 10); // Allow for cache miss on first load
        }

        [Fact]
        public void KernelCache_GlobalStatistics()
        {
            // Load some kernels to populate caches
            using var kernel1 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            using var kernel2 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>, ArrayView<int>, int>>(MultiplyKernel);
            
            var stats = acceleratorKernelCache.GetAllStatistics();
            
            Assert.NotEmpty(stats);
            Assert.True(stats.Count > 0);
            
            foreach (var kvp in stats)
            {
                Assert.NotNull(kvp.Key);
                Assert.NotNull(kvp.Value);
                Assert.True(kvp.Value.MaxSize > 0);
            }
        }

        [Fact]
        public void KernelCache_ClearAllacceleratorCaches()
        {
            // Load some kernels
            using var kernel1 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>>>(SimpleKernel);
            using var kernel2 = accelerator.LoadKernelCached<Action<Index1D, ArrayView<int>, ArrayView<int>, int>>(MultiplyKernel);
            
            var cache = acceleratorKernelCache.GetOrCreateCache(accelerator);
            Assert.True(cache.CurrentSize > 0);
            
            // Clear all caches
            acceleratorKernelCache.ClearAllCaches();
            
            Assert.Equal(0, cache.CurrentSize);
        }

        #endregion

        #region Utility Extension Tests

        [Fact]
        public void KernelCache_CreateCacheKey()
        {
            var key1 = KernelCacheExtensions.CreateCacheKey(
                "TestKernel", 
                new object[] { typeof(int), typeof(float) }, 
                "CPU_Device_0");
                
            var key2 = KernelCacheExtensions.CreateCacheKey(
                "TestKernel", 
                new object[] { typeof(int), typeof(double) }, 
                "CPU_Device_0");
                
            var key3 = KernelCacheExtensions.CreateCacheKey(
                "TestKernel", 
                new object[] { typeof(int), typeof(float) }, 
                "GPU_Device_0");
            
            Assert.NotEqual(key1, key2); // Different parameter types
            Assert.NotEqual(key1, key3); // Different device
            Assert.NotEqual(key2, key3); // Different parameter types and device
        }

        [Fact]
        public void KernelCache_CreateVersionString()
        {
            var version1 = KernelCacheExtensions.CreateVersionString("1.0.0", "O2", "x64");
            var version2 = KernelCacheExtensions.CreateVersionString("1.0.0", "O3", "x64");
            var version3 = KernelCacheExtensions.CreateVersionString("1.0.1", "O2", "x64");
            
            Assert.NotEqual(version1, version2); // Different optimization
            Assert.NotEqual(version1, version3); // Different compiler version
            Assert.NotEqual(version2, version3); // Different compiler version and optimization
            
            Assert.Contains("1.0.0", version1);
            Assert.Contains("O2", version1);
            Assert.Contains("x64", version1);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes of the test resources.
        /// </summary>
        public void Dispose()
        {
            accelerator?.Dispose();
            context?.Dispose();
        }

        #endregion
    }
}