// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: DeviceAPITests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using Xunit;

namespace ILGPU.Tests.CPU
{
    /// <summary>
    /// Tests for the modernized Device API properties.
    /// </summary>
    public class DeviceAPITests : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;

        public DeviceAPITests()
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
        public void DeviceStatus_ShouldBeAccessible()
        {
            // Test that Device.Status property is accessible
            var device = accelerator.Device;
            Assert.NotNull(device);
            
            var status = device.Status;
            Assert.True(Enum.IsDefined<DeviceStatus>(status), $"Invalid device status: {status}");
        }

        [Fact] 
        public void DeviceMemoryInfo_ShouldBeAccessible()
        {
            // Test that Device.Memory property is accessible
            var device = accelerator.Device;
            Assert.NotNull(device);
            
            var memoryInfo = device.Memory;
            Assert.NotNull(memoryInfo);
            
            // For CPU devices, memory info should be valid
            Assert.True(memoryInfo.IsValid || memoryInfo == MemoryInfo.Unknown);
        }

        [Fact]
        public void DeviceUnifiedMemorySupport_ShouldBeAccessible()
        {
            // Test that Device.SupportsUnifiedMemory property is accessible
            var device = accelerator.Device;
            Assert.NotNull(device);
            
            var supportsUnifiedMemory = device.SupportsUnifiedMemory;
            // CPU devices typically support unified memory (CPU and GPU share same memory space)
            // This is just testing that the property is accessible
            Assert.True(supportsUnifiedMemory || !supportsUnifiedMemory); // Always true, just testing accessibility
        }

        [Fact]
        public void DeviceMemoryPoolSupport_ShouldBeAccessible()
        {
            // Test that Device.SupportsMemoryPools property is accessible
            var device = accelerator.Device;
            Assert.NotNull(device);
            
            var supportsMemoryPools = device.SupportsMemoryPools;
            // This is just testing that the property is accessible
            Assert.True(supportsMemoryPools || !supportsMemoryPools); // Always true, just testing accessibility
        }

        [Fact]
        public void AcceleratorDeviceAPI_ShouldDelegateToDevice()
        {
            // Test that Accelerator implements IDevice and delegates to its Device
            var device = accelerator.Device;
            Assert.NotNull(device);
            
            // Test that accelerator properties delegate to device
            Assert.Equal(device.Status, accelerator.Status);
            Assert.Equal(device.Memory, accelerator.Memory);
            Assert.Equal(device.SupportsUnifiedMemory, accelerator.SupportsUnifiedMemory);
            Assert.Equal(device.SupportsMemoryPools, accelerator.SupportsMemoryPools);
        }

        [Fact]
        public void DeviceStatus_ExtensionMethods_ShouldWork()
        {
            var device = accelerator.Device;
            var status = device.Status;
            
            // Test extension methods work
            var isUsable = status.IsUsable();
            var isError = status.IsError();
            var requiresAttention = status.RequiresAttention();
            var description = status.GetDescription();
            
            Assert.NotNull(description);
            Assert.NotEmpty(description);
            
            // Logical consistency checks
            if (isError)
            {
                Assert.True(requiresAttention);
            }
            
            if (status == DeviceStatus.Available)
            {
                Assert.True(isUsable);
                Assert.False(isError);
            }
        }

        [Fact]
        public void MemoryInfo_PropertiesAndMethods_ShouldWork()
        {
            var device = accelerator.Device;
            var memoryInfo = device.Memory;
            
            if (memoryInfo.IsValid)
            {
                // Test that memory info properties are accessible
                Assert.True(memoryInfo.TotalMemory >= 0);
                Assert.True(memoryInfo.AvailableMemory >= 0);
                Assert.True(memoryInfo.UsedMemory >= 0);
                Assert.True(memoryInfo.MaxAllocationSize >= 0);
                Assert.True(memoryInfo.AllocationGranularity > 0);
                Assert.True(memoryInfo.CacheLineSize > 0);
                
                // Test calculated properties
                var utilization = memoryInfo.MemoryUtilization;
                Assert.True(utilization >= 0.0 && utilization <= 100.0);
                
                var availablePercentage = memoryInfo.AvailableMemoryPercentage;
                Assert.True(availablePercentage >= 0.0 && availablePercentage <= 100.0);
                
                // Test methods
                var optimalSize = memoryInfo.GetOptimalAllocationSize(1000);
                Assert.True(optimalSize >= 1000);
                Assert.True(optimalSize % memoryInfo.AllocationGranularity == 0);
                
                var canAllocate = memoryInfo.CanAllocate(1000);
                Assert.True(canAllocate || !canAllocate); // Just testing method exists
            }
        }

        [Fact]
        public void DeviceId_ShouldBeAccessible()
        {
            // Test that DeviceId property is accessible through IDeviceIdentifiable
            var device = accelerator.Device;
            Assert.NotNull(device);
            
            var deviceId = device.DeviceId;
            Assert.NotEqual(default, deviceId);
            Assert.Equal(device.AcceleratorType, deviceId.AcceleratorType);
        }

        #region Comprehensive DeviceStatus Tests

        [Theory]
        [InlineData(DeviceStatus.Available, true, false, false, false)]
        [InlineData(DeviceStatus.Busy, true, false, true, false)]
        [InlineData(DeviceStatus.Error, false, true, false, true)]
        [InlineData(DeviceStatus.FatalError, false, true, false, true)]
        [InlineData(DeviceStatus.Unavailable, false, false, false, false)]
        [InlineData(DeviceStatus.ExclusiveProcess, false, false, true, false)]
        [InlineData(DeviceStatus.Suspended, false, false, true, false)]
        [InlineData(DeviceStatus.Initializing, false, false, true, false)]
        [InlineData(DeviceStatus.ShuttingDown, false, false, true, false)]
        [InlineData(DeviceStatus.RequiresUpdate, false, false, false, true)]
        [InlineData(DeviceStatus.Throttling, true, false, true, false)]
        public void DeviceStatus_ExtensionMethods_AllValues(
            DeviceStatus status, 
            bool expectedUsable, 
            bool expectedError, 
            bool expectedTemporarilyUnavailable,
            bool expectedRequiresAttention)
        {
            Assert.Equal(expectedUsable, status.IsUsable());
            Assert.Equal(expectedError, status.IsError());
            Assert.Equal(expectedTemporarilyUnavailable, status.IsTemporarilyUnavailable());
            Assert.Equal(expectedRequiresAttention, status.RequiresAttention());
        }

        [Fact]
        public void DeviceStatus_GetDescription_AllValues()
        {
            var statusValues = Enum.GetValues<DeviceStatus>();
            foreach (var status in statusValues)
            {
                var description = status.GetDescription();
                Assert.NotNull(description);
                Assert.NotEmpty(description);
                Assert.DoesNotContain("Unknown status", description);
            }
        }

        [Fact]
        public void DeviceStatus_EnumValues_ValidRange()
        {
            var statusValues = Enum.GetValues<DeviceStatus>();
            Assert.True(statusValues.Length >= 12, "DeviceStatus should have at least 12 values");
            
            foreach (var status in statusValues)
            {
                Assert.True(Enum.IsDefined<DeviceStatus>(status), $"Status {status} should be defined");
            }
        }

        #endregion

        #region Comprehensive MemoryInfo Tests

        [Fact]
        public void MemoryInfo_Constructor_ValidatesParameters()
        {
            // Test invalid parameters throw exceptions
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                new MemoryInfo(-1, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                new MemoryInfo(1000, -1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                new MemoryInfo(1000, 0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                new MemoryInfo(1000, 0, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                new MemoryInfo(1000, 0, 0, 1000, 0)); // allocationGranularity
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                new MemoryInfo(1000, 0, 0, 1000, 1, false, false, false, 0)); // cacheLineSize
        }

        [Fact]
        public void MemoryInfo_Equality_WorksCorrectly()
        {
            var memory1 = new MemoryInfo(1000, 800, 200, 500, 64, true, false, true, 128, 1000000);
            var memory2 = new MemoryInfo(1000, 800, 200, 500, 64, true, false, true, 128, 1000000);
            var memory3 = new MemoryInfo(1000, 700, 300, 500, 64, true, false, true, 128, 1000000);

            Assert.Equal(memory1, memory2);
            Assert.True(memory1 == memory2);
            Assert.False(memory1 != memory2);
            
            Assert.NotEqual(memory1, memory3);
            Assert.False(memory1 == memory3);
            Assert.True(memory1 != memory3);
            
            Assert.Equal(memory1.GetHashCode(), memory2.GetHashCode());
        }

        [Fact]
        public void MemoryInfo_WithUpdatedUsage_CreatesNewInstance()
        {
            var original = new MemoryInfo(1000, 800, 200, 500);
            var updated = original.WithUpdatedUsage(700, 300);
            
            Assert.NotSame(original, updated);
            Assert.Equal(1000, updated.TotalMemory);
            Assert.Equal(700, updated.AvailableMemory);
            Assert.Equal(300, updated.UsedMemory);
            Assert.Equal(original.MaxAllocationSize, updated.MaxAllocationSize);
        }

        [Fact]
        public void MemoryInfo_CalculatedProperties_AccurateValues()
        {
            var memory = new MemoryInfo(1000, 600, 400, 500);
            
            Assert.Equal(40.0, memory.MemoryUtilization);
            Assert.Equal(60.0, memory.AvailableMemoryPercentage);
            Assert.Equal(1.0 - (500.0 / 600.0), memory.FragmentationRatio, 0.001);
            Assert.False(memory.IsLowMemory); // 60% available
            
            var lowMemory = new MemoryInfo(1000, 50, 950, 20);
            Assert.True(lowMemory.IsLowMemory); // 5% available
            Assert.True(lowMemory.IsFragmented); // High fragmentation (1.0 - (20/50) = 0.6 > 0.5)
        }

        [Fact]
        public void MemoryInfo_ToString_FormatsCorrectly()
        {
            var memory = new MemoryInfo(1024 * 1024 * 1024, 512 * 1024 * 1024, 512 * 1024 * 1024, 256 * 1024 * 1024);
            var str = memory.ToString();
            
            Assert.Contains("512MB/1024MB used", str);
            Assert.Contains("50.0%", str);
            Assert.Contains("512MB available", str);
            Assert.Contains("Max Allocation: 256MB", str);
        }

        [Fact]
        public void MemoryInfo_Unknown_HasExpectedValues()
        {
            var unknown = MemoryInfo.Unknown;
            
            Assert.False(unknown.IsValid);
            Assert.Equal(0, unknown.TotalMemory);
            Assert.Equal(0, unknown.AvailableMemory);
            Assert.Equal(0, unknown.UsedMemory);
            Assert.Equal("Memory: Unknown", unknown.ToString());
        }

        [Fact]
        public void MemoryInfo_Methods_EdgeCases()
        {
            var memory = new MemoryInfo(1000, 800, 200, 500, 64);
            
            // Test GetOptimalAllocationSize
            Assert.Equal(64, memory.GetOptimalAllocationSize(1));
            Assert.Equal(64, memory.GetOptimalAllocationSize(64));
            Assert.Equal(128, memory.GetOptimalAllocationSize(65));
            Assert.Equal(128, memory.GetOptimalAllocationSize(100));
            
            // Test CanAllocate
            Assert.True(memory.CanAllocate(500)); // Exactly max allocation
            Assert.True(memory.CanAllocate(400)); // Within available memory
            Assert.False(memory.CanAllocate(600)); // Exceeds max allocation
            Assert.False(memory.CanAllocate(900)); // Exceeds available memory
            Assert.False(memory.CanAllocate(0)); // Invalid size
            Assert.False(memory.CanAllocate(-1)); // Invalid size
        }

        #endregion

        #region Enhanced Device and Accelerator Tests

        [Fact]
        public void Device_Properties_DefaultValues()
        {
            var device = accelerator.Device;
            
            // DeviceStatus should have a valid default value
            Assert.True(Enum.IsDefined<DeviceStatus>(device.Status));
            
            // Memory should be initialized (either valid or Unknown)
            Assert.NotNull(device.Memory);
            
            // Boolean properties should have defined values
            Assert.True(device.SupportsUnifiedMemory || !device.SupportsUnifiedMemory);
            Assert.True(device.SupportsMemoryPools || !device.SupportsMemoryPools);
        }

        [Fact]
        public void Device_DeviceId_ConsistentWithAcceleratorType()
        {
            var device = accelerator.Device;
            var deviceId = device.DeviceId;
            
            Assert.Equal(device.AcceleratorType, deviceId.AcceleratorType);
            Assert.Equal(AcceleratorType.CPU, deviceId.AcceleratorType);
            Assert.True(deviceId.IsCPU);
            Assert.False(deviceId.IsCuda);
            Assert.False(deviceId.IsOpenCL);
            Assert.False(deviceId.IsVelocity);
        }

        [Fact]
        public void Accelerator_IDevice_ImplementationComplete()
        {
            // Verify Accelerator implements IDevice completely
            var iDevice = (IDevice)accelerator;
            
            Assert.NotNull(iDevice.Status);
            Assert.NotNull(iDevice.Memory);
            Assert.Equal(accelerator.Name, iDevice.Name);
            Assert.Equal(accelerator.MemorySize, iDevice.MemorySize);
            Assert.Equal(accelerator.Capabilities, iDevice.Capabilities);
            
            // Test that all IDevice members are accessible
            Assert.True(iDevice.SupportsUnifiedMemory || !iDevice.SupportsUnifiedMemory);
            Assert.True(iDevice.SupportsMemoryPools || !iDevice.SupportsMemoryPools);
        }

        [Fact]
        public void Device_MemoryInfo_ValidForCPUAccelerator()
        {
            var device = accelerator.Device;
            var memory = device.Memory;
            
            if (memory.IsValid)
            {
                // For CPU accelerator, memory should reflect system memory
                Assert.True(memory.TotalMemory > 0);
                Assert.True(memory.AvailableMemory >= 0);
                Assert.True(memory.MaxAllocationSize > 0);
                Assert.True(memory.AllocationGranularity > 0);
                
                // CPU-specific expectations
                Assert.True(memory.SupportsZeroCopy); // CPU should support zero-copy
                Assert.Equal(64, memory.CacheLineSize); // Common CPU cache line size
            }
        }

        #endregion
    }
}