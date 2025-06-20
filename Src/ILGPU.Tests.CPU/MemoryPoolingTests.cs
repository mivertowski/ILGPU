// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryPoolingTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.MemoryPooling;
using ILGPU.Runtime.CPU;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ILGPU.Tests.CPU
{
    public class MemoryPoolingTests : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;
        
        public MemoryPoolingTests()
        {
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUAccelerator(0);
        }
        
        public void Dispose()
        {
            accelerator?.Dispose();
            context?.Dispose();
        }

        [Fact]
        public void MemoryPool_Basic_RentAndReturn()
        {
            using var pool = new AdaptiveMemoryPool<int>(accelerator);

            // Rent a buffer
            var buffer = pool.Rent(1000);
            Assert.NotNull(buffer);
            Assert.True(buffer.Length >= 1000);
            Assert.False(buffer.IsReturned);

            // Return the buffer
            pool.Return(buffer);
            Assert.True(buffer.IsReturned);
        }

        [Fact]
        public void MemoryPool_ReuseBuffer()
        {
            using var pool = new AdaptiveMemoryPool<int>(accelerator);

            // Rent and return a buffer
            var buffer1 = pool.Rent(1000);
            var originalPtr = buffer1.NativePtr;
            pool.Return(buffer1);

            // Rent again - should get the same buffer back
            var buffer2 = pool.Rent(1000);
            Assert.Equal(originalPtr, buffer2.NativePtr);

            pool.Return(buffer2);
        }

        [Fact]
        public void MemoryPool_Statistics()
        {
            using var pool = new AdaptiveMemoryPool<int>(accelerator);

            var buffer1 = pool.Rent(1000);
            var buffer2 = pool.Rent(2000);

            var stats = pool.GetStatistics();
            Assert.Equal(2, stats.RentedBuffers);
            Assert.Equal(2, stats.TotalAllocations);
            Assert.True(stats.UsedSizeBytes > 0);

            pool.Return(buffer1);
            pool.Return(buffer2);

            stats = pool.GetStatistics();
            Assert.Equal(0, stats.RentedBuffers);
            Assert.Equal(2, stats.AvailableBuffers);
        }

        [Fact]
        public void MemoryPool_SizeBuckets()
        {
            using var pool = new AdaptiveMemoryPool<int>(accelerator);

            // Request slightly different sizes - should get bucketed
            var buffer1 = pool.Rent(1000);
            var buffer2 = pool.Rent(1100);

            // Both should be same bucket size (rounded up)
            Assert.Equal(buffer1.ActualLength, buffer2.ActualLength);

            pool.Return(buffer1);
            pool.Return(buffer2);
        }

        [Fact]
        public void MemoryPool_MaxSizeLimit()
        {
            var config = new MemoryPoolConfiguration
            {
                MaxBufferSizeBytes = 1024 * sizeof(int)
            };
            using var pool = new AdaptiveMemoryPool<int>(accelerator, config);

            // This should work
            var buffer1 = pool.Rent(1024);
            Assert.NotNull(buffer1);

            // This should throw
            Assert.Throws<ArgumentException>(() => pool.Rent(2048));

            pool.Return(buffer1);
        }

        [Fact]
        public void MemoryPool_ConfigurationValidation()
        {
            var config = new MemoryPoolConfiguration();
            
            // Valid configuration
            config.Validate(); // Should not throw

            // Invalid configurations
            config.MaxPoolSizeBytes = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());

            config.MaxPoolSizeBytes = 1000;
            config.MaxBufferSizeBytes = 2000;
            Assert.Throws<ArgumentException>(() => config.Validate());

            config.MaxBufferSizeBytes = 500;
            config.AllocationAlignment = 3; // Not power of 2
            Assert.Throws<ArgumentException>(() => config.Validate());
        }

        [Fact]
        public void MemoryPool_PresetConfigurations()
        {
            // Test all preset configurations
            var presets = new[]
            {
                MemoryPoolConfiguration.CreateHighPerformance(),
                MemoryPoolConfiguration.CreateMemoryEfficient(),
                MemoryPoolConfiguration.CreateDevelopment()
            };

            foreach (var config in presets)
            {
                config.Validate(); // Should not throw
                using var pool = new AdaptiveMemoryPool<int>(accelerator, config);
                
                var buffer = pool.Rent(1000);
                Assert.NotNull(buffer);
                pool.Return(buffer);
            }
        }

        [Fact]
        public void MemoryPool_Trim()
        {
            var config = new MemoryPoolConfiguration
            {
                RetentionPolicy = PoolRetentionPolicy.Aggressive,
                EnableStatistics = true
            };
            using var pool = new AdaptiveMemoryPool<int>(accelerator, config);

            // Create and return several buffers
            for (int i = 0; i < 5; i++)
            {
                var buffer = pool.Rent(1000);
                pool.Return(buffer);
            }

            var statsBefore = pool.GetStatistics();
            Assert.True(statsBefore.AvailableBuffers > 0);

            // Trim should reduce available buffers
            pool.Trim();

            var statsAfter = pool.GetStatistics();
            Assert.True(statsAfter.AvailableBuffers <= statsBefore.AvailableBuffers);
        }

        [Fact]
        public async Task MemoryPool_AsyncRent()
        {
            using var pool = new AdaptiveMemoryPool<int>(accelerator);

            var buffer = await pool.RentAsync(1000);
            Assert.NotNull(buffer);
            Assert.True(buffer.Length >= 1000);

            pool.Return(buffer);
        }

        [Fact]
        public void MemoryPool_PooledBufferDispose()
        {
            using var pool = new AdaptiveMemoryPool<int>(accelerator);

            IPooledMemoryBuffer<int> buffer;
            using (buffer = pool.Rent(1000))
            {
                Assert.False(buffer.IsReturned);
            }
            
            // Should be automatically returned on dispose
            Assert.True(buffer.IsReturned);
        }

        [Fact]
        public void MemoryPool_FactoryPattern()
        {
            var factory = new DefaultMemoryPoolFactory();

            using var pool = factory.CreatePool<int>(accelerator);
            Assert.NotNull(pool);

            var buffer = pool.Rent(1000);
            Assert.NotNull(buffer);
            
            pool.Return(buffer);
        }
    }
}