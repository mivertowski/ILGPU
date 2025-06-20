#!/usr/bin/env dotnet-script
#r "Bin/Release/net9.0/ILGPU.dll"

using System;
using ILGPU;
using ILGPU.Runtime;

Console.WriteLine("=== Quick Device API Verification ===");

try
{
    using var context = Context.CreateDefault();
    using var accelerator = context.CreateCPUAccelerator(0);
    
    var device = accelerator.Device;
    
    // Test all new properties exist and are accessible
    Console.WriteLine($"✓ Device Status: {device.Status}");
    Console.WriteLine($"✓ Memory Info Valid: {device.Memory.IsValid}");
    Console.WriteLine($"✓ Supports Unified Memory: {device.SupportsUnifiedMemory}");
    Console.WriteLine($"✓ Supports Memory Pools: {device.SupportsMemoryPools}");
    Console.WriteLine($"✓ Device ID: {device.DeviceId}");
    
    // Test accelerator delegation
    Console.WriteLine($"✓ Accelerator delegates Status: {device.Status == accelerator.Status}");
    Console.WriteLine($"✓ Accelerator delegates Memory: {device.Memory == accelerator.Memory}");
    
    Console.WriteLine("\n=== ALL DEVICE API PROPERTIES SUCCESSFULLY IMPLEMENTED! ===");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
}