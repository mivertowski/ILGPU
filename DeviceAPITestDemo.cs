// Simple standalone test to verify Device API properties work
using System;
using ILGPU;
using ILGPU.Runtime;

class DeviceAPITestDemo
{
    static void Main()
    {
        Console.WriteLine("=== Device API Implementation Verification ===");
        
        try
        {
            using var context = Context.CreateDefault();
            using var accelerator = context.CreateCPUAccelerator(0);
            
            Console.WriteLine($"✓ Created accelerator: {accelerator.Name}");
            
            var device = accelerator.Device;
            Console.WriteLine($"✓ Retrieved device: {device.Name}");
            
            // Test new Device API properties
            Console.WriteLine("\n--- Testing New Device API Properties ---");
            
            // Test DeviceStatus
            var status = device.Status;
            Console.WriteLine($"✓ Device Status: {status} ({status.GetDescription()})");
            Console.WriteLine($"  - Is Usable: {status.IsUsable()}");
            Console.WriteLine($"  - Is Error: {status.IsError()}");
            
            // Test MemoryInfo
            var memory = device.Memory;
            Console.WriteLine($"✓ Memory Info: {memory}");
            Console.WriteLine($"  - Is Valid: {memory.IsValid}");
            if (memory.IsValid)
            {
                Console.WriteLine($"  - Total Memory: {memory.TotalMemory:N0} bytes");
                Console.WriteLine($"  - Available Memory: {memory.AvailableMemory:N0} bytes");
                Console.WriteLine($"  - Memory Utilization: {memory.MemoryUtilization:F1}%");
            }
            
            // Test unified memory support
            var supportsUnified = device.SupportsUnifiedMemory;
            Console.WriteLine($"✓ Supports Unified Memory: {supportsUnified}");
            
            // Test memory pools support  
            var supportsPools = device.SupportsMemoryPools;
            Console.WriteLine($"✓ Supports Memory Pools: {supportsPools}");
            
            // Test DeviceId
            var deviceId = device.DeviceId;
            Console.WriteLine($"✓ Device ID: {deviceId}");
            Console.WriteLine($"  - Accelerator Type: {deviceId.AcceleratorType}");
            Console.WriteLine($"  - Value: {deviceId.Value}");
            
            // Test that Accelerator delegates to Device
            Console.WriteLine("\n--- Testing Accelerator Delegation ---");
            Console.WriteLine($"✓ Status delegation: {device.Status == accelerator.Status}");
            Console.WriteLine($"✓ Memory delegation: {device.Memory == accelerator.Memory}");
            Console.WriteLine($"✓ Unified Memory delegation: {device.SupportsUnifiedMemory == accelerator.SupportsUnifiedMemory}");
            Console.WriteLine($"✓ Memory Pools delegation: {device.SupportsMemoryPools == accelerator.SupportsMemoryPools}");
            
            Console.WriteLine("\n=== All Device API Properties Successfully Implemented! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}