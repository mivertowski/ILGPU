// ---------------------------------------------------------------------------------------
//                                   ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: AOTBasicTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using System;
using Xunit;

namespace ILGPU.Tests.AOT
{
    /// <summary>
    /// Basic AOT compatibility tests for ILGPU core functionality.
    /// </summary>
    public class AOTBasicTests : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;

        public AOTBasicTests()
        {
            // Use CPU-only context to avoid native library conflicts in AOT tests
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUAccelerator(0);
        }

        public void Dispose()
        {
            accelerator?.Dispose();
            context?.Dispose();
        }

        [Fact]
        public void AOT_ContextCreation_ShouldWork()
        {
            Assert.NotNull(context);
            Assert.True(context.GetCPUDevices().Count > 0);
        }

        [Fact]
        public void AOT_AcceleratorCreation_ShouldWork()
        {
            Assert.NotNull(accelerator);
            Assert.Equal(AcceleratorType.CPU, accelerator.AcceleratorType);
        }

        [Fact]
        public void AOT_MemoryAllocation_ShouldWork()
        {
            const int length = 1024;
            using var buffer = accelerator.Allocate1D<int>(length);
            
            Assert.NotNull(buffer);
            Assert.Equal(length, buffer.Length);
        }

        [Fact]
        public void AOT_KernelSystemValidation_ShouldWork()
        {
            // Validate our Phase 4.7 implementation works correctly
            Assert.NotNull(context.KernelSystem);
            
#if NATIVE_AOT || AOT_COMPATIBLE
            Assert.True(context.KernelSystem.IsAOTCompatible);
            Assert.False(context.KernelSystem.SupportsDynamicGeneration);
            Assert.Contains("Generated", context.KernelSystem.AssemblyName);
#else
            Assert.False(context.KernelSystem.IsAOTCompatible);
            Assert.True(context.KernelSystem.SupportsDynamicGeneration);
            Assert.Contains("Runtime", context.KernelSystem.AssemblyName);
#endif
        }

        [Fact]
        public void AOT_DelegateResolverValidation_ShouldWork()
        {
            // Validate our AOTDelegateResolver works in both modes
            var testMethod = typeof(string).GetMethod("get_Length");
            Assert.NotNull(testMethod);
            
            var testDelegate = Runtime.AOTDelegateResolver.CreateDelegate<Func<string, int>>(testMethod);
            Assert.NotNull(testDelegate);
            
            // Test that it actually works
            var result = testDelegate("test");
            Assert.Equal(4, result);
        }

        [Fact]
        public void AOT_MemoryBuffer_InterfaceValidation_ShouldWork()
        {
            // Validate our Phase 4.2 IMemoryBuffer interface works
            const int length = 100;
            using var buffer = accelerator.Allocate1D<int>(length);
            
            // Our buffer should implement the unified interface
            Assert.IsAssignableFrom<Runtime.IMemoryBuffer>(buffer);
            
            var memBuffer = buffer as Runtime.IMemoryBuffer;
            Assert.NotNull(memBuffer);
            Assert.Equal(length, memBuffer.Length);
            Assert.Equal(length * sizeof(int), memBuffer.LengthInBytes);
            Assert.Equal(typeof(int), memBuffer.ElementType);
            Assert.Equal(1, memBuffer.Dimensions);
            Assert.False(memBuffer.IsDisposed);
        }
    }
}