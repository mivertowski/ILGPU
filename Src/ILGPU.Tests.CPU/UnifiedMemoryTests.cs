// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: UnifiedMemoryTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.UnifiedMemory;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ILGPU.Tests.CPU
{
    public class UnifiedMemoryTests : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;

        public UnifiedMemoryTests()
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
        public void UnifiedMemory_BasicAllocation()
        {
            // Allocate unified memory
            using var buffer = accelerator.AllocateUnified<int>(100);
            
            Assert.NotNull(buffer);
            Assert.Equal(100, buffer.Length);
        }

        [Fact]
        public void UnifiedMemory_AccessModes()
        {
            // Test different access modes
            using var sharedBuffer = accelerator.AllocateUnified<float>(50, UnifiedMemoryAccessMode.Shared);
            using var deviceBuffer = accelerator.AllocateUnified<float>(50, UnifiedMemoryAccessMode.DevicePreferred);
            using var hostBuffer = accelerator.AllocateUnified<float>(50, UnifiedMemoryAccessMode.HostPreferred);
            
            Assert.Equal(UnifiedMemoryAccessMode.Shared, sharedBuffer.AccessMode);
            Assert.Equal(UnifiedMemoryAccessMode.DevicePreferred, deviceBuffer.AccessMode);
            Assert.Equal(UnifiedMemoryAccessMode.HostPreferred, hostBuffer.AccessMode);
        }

        [Fact]
        public void UnifiedMemory_CPUView()
        {
            const int length = 10;
            using var buffer = accelerator.AllocateUnified<int>(length);
            
            // Access CPU view
            var cpuView = buffer.CPUView;
            Assert.Equal(length, cpuView.Length);
            
            // Write data through CPU view
            for (int i = 0; i < length; i++)
            {
                cpuView[i] = i * 2;
            }
            
            // Verify data
            var data = buffer.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(i * 2, data[i]);
            }
        }

        [Fact]
        public void UnifiedMemory_Transform()
        {
            const int length = 100;
            using var buffer = accelerator.AllocateUnified<int>(length);
            using var stream = accelerator.CreateStream();
            
            // Initialize data
            var cpuView = buffer.CPUView;
            for (int i = 0; i < length; i++)
            {
                cpuView[i] = i;
            }
            
            // Apply transformation
            buffer.Transform((index, view) =>
            {
                view[index] = view[index] * 2 + 1;
            }, stream);
            
            stream.Synchronize();
            
            // Verify transformation
            var result = buffer.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(i * 2 + 1, result[i]);
            }
        }

        [Fact]
        public async Task UnifiedMemory_TransformAsync()
        {
            const int length = 50;
            using var buffer = accelerator.AllocateUnified<float>(length);
            using var stream = accelerator.CreateStream();
            
            // Initialize data
            var cpuView = buffer.CPUView;
            for (int i = 0; i < length; i++)
            {
                cpuView[i] = i * 0.5f;
            }
            
            // Apply async transformation
            await buffer.TransformAsync((index, view) =>
            {
                view[index] = view[index] * view[index];
            }, stream);
            
            // Verify transformation
            var result = buffer.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                var expected = (i * 0.5f) * (i * 0.5f);
                Assert.Equal(expected, result[i], 5);
            }
        }

        [Fact]
        public void UnifiedMemory_Operations_Add()
        {
            const int length = 10;
            using var left = accelerator.AllocateUnified<int>(length);
            using var right = accelerator.AllocateUnified<int>(length);
            using var result = accelerator.AllocateUnified<int>(length);
            
            // Initialize data
            var leftCpu = left.CPUView;
            var rightCpu = right.CPUView;
            for (int i = 0; i < length; i++)
            {
                leftCpu[i] = i;
                rightCpu[i] = i * 2;
            }
            
            // Perform addition
            UnifiedMemoryOperations.Add(left, right, result, accelerator);
            
            // Verify result
            var resultData = result.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(i + i * 2, resultData[i]);
            }
        }

        [Fact]
        public void UnifiedMemory_Operations_MultiplyScalar()
        {
            const int length = 20;
            const float scalar = 2.5f;
            using var buffer = accelerator.AllocateUnified<float>(length);
            using var result = accelerator.AllocateUnified<float>(length);
            
            // Initialize data
            var cpuView = buffer.CPUView;
            for (int i = 0; i < length; i++)
            {
                cpuView[i] = i;
            }
            
            // Perform scalar multiplication
            UnifiedMemoryOperations.MultiplyScalar(buffer, scalar, result, accelerator);
            
            // Verify result
            var resultData = result.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(i * scalar, resultData[i], 5);
            }
        }

        [Fact]
        public void UnifiedMemory_Operations_Sum()
        {
            const int length = 10;
            using var buffer = accelerator.AllocateUnified<int>(length);
            
            // Initialize data
            var cpuView = buffer.CPUView;
            for (int i = 0; i < length; i++)
            {
                cpuView[i] = i + 1;
            }
            
            // Calculate sum
            var sum = UnifiedMemoryOperations.Sum(buffer, accelerator);
            
            // Expected sum: 1 + 2 + ... + 10 = 55
            Assert.Equal(55, sum);
        }

        [Fact]
        public async Task UnifiedMemory_AsyncOperations()
        {
            const int length = 15;
            using var left = accelerator.AllocateUnified<float>(length);
            using var right = accelerator.AllocateUnified<float>(length);
            using var result = accelerator.AllocateUnified<float>(length);
            
            // Initialize data
            var leftCpu = left.CPUView;
            var rightCpu = right.CPUView;
            for (int i = 0; i < length; i++)
            {
                leftCpu[i] = i * 0.1f;
                rightCpu[i] = i * 0.2f;
            }
            
            // Perform async addition
            await AsyncUnifiedMemoryOperations.AddAsync(left, right, result, accelerator);
            
            // Verify result
            var resultData = result.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                var expected = i * 0.1f + i * 0.2f;
                Assert.Equal(expected, resultData[i], 5);
            }
        }

        [Fact]
        public void UnifiedMemory_UnifiedCopy()
        {
            const int length = 25;
            using var source = accelerator.AllocateUnified<double>(length);
            using var destination = accelerator.AllocateUnified<double>(length);
            using var stream = accelerator.CreateStream();
            
            // Initialize source data
            var sourceCpu = source.CPUView;
            for (int i = 0; i < length; i++)
            {
                sourceCpu[i] = Math.Sqrt(i);
            }
            
            // Copy data
            source.UnifiedCopy(destination, stream);
            stream.Synchronize();
            
            // Verify copy
            var destData = destination.GetAsArray1D();
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(Math.Sqrt(i), destData[i], 10);
            }
        }

        [Fact]
        public void UnifiedMemory_UnifiedArrayView()
        {
            const int length = 30;
            using var buffer = accelerator.AllocateUnified<int>(length);
            
            // Create unified view
            var unifiedView = buffer.AsUnifiedView();
            
            Assert.Equal(length, unifiedView.Length);
            Assert.True(unifiedView.IsValid);
            Assert.Equal(UnifiedMemoryAccessMode.Shared, unifiedView.AccessMode);
            
            // Test GPU view access
            var gpuView = unifiedView.GPUView;
            Assert.Equal(length, gpuView.Length);
        }

        [Fact]
        public void UnifiedMemory_Prefetch()
        {
            using var buffer = accelerator.AllocateUnified<float>(100);
            using var stream = accelerator.CreateStream();
            
            // These operations should not throw
            buffer.Prefetch(stream, UnifiedMemoryTarget.Host);
            buffer.Prefetch(stream, UnifiedMemoryTarget.Device);
            buffer.Prefetch(stream, UnifiedMemoryTarget.Auto);
            
            stream.Synchronize();
        }

        [Fact]
        public void UnifiedMemory_Advise()
        {
            using var buffer = accelerator.AllocateUnified<int>(50);
            
            // These operations should not throw
            buffer.Advise(UnifiedMemoryAdvice.None);
            buffer.Advise(UnifiedMemoryAdvice.ReadMostly);
            buffer.Advise(UnifiedMemoryAdvice.PreferredLocation);
            buffer.Advise(UnifiedMemoryAdvice.RandomAccess);
            buffer.Advise(UnifiedMemoryAdvice.SequentialAccess);
            buffer.Advise(UnifiedMemoryAdvice.AccessedFrequently);
        }

        [Fact]
        public void UnifiedMemory_Pin()
        {
            using var buffer = accelerator.AllocateUnified<long>(20);
            
            // Pin the memory
            using (var pinScope = buffer.Pin())
            {
                // Access data while pinned
                var cpuView = buffer.CPUView;
                for (int i = 0; i < cpuView.Length; i++)
                {
                    cpuView[i] = i * 100;
                }
            }
            
            // Verify data after unpinning
            var data = buffer.GetAsArray1D();
            for (int i = 0; i < data.Length; i++)
            {
                Assert.Equal(i * 100, data[i]);
            }
        }

        [Fact]
        public void UnifiedMemory_SupportsCheck()
        {
            // CPU accelerator should support unified memory in our implementation
            Assert.True(accelerator.SupportsUnifiedMemory());
        }
    }
}