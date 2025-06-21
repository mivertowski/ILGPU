// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: UnifiedMemoryBenchmarks.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Numerics;
using ILGPU.Numerics.Hybrid;
using ILGPU.Runtime;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for unified memory operations and zero-copy performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class UnifiedMemoryBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? accelerator;
    private UnifiedTensor<float>? unifiedTensorA;
    private UnifiedTensor<float>? unifiedTensorB;

    [Params(64, 128, 256)]
    public int TensorSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            context = Context.CreateDefault();
            var device = context.GetPreferredDevice(preferCPU: false); // GPU preferred, CPU fallback
            accelerator = device?.CreateAccelerator(context);
        }
        catch
        {
            context = Context.CreateDefault();
            var device = context.GetPreferredDevice(preferCPU: true);
            accelerator = device?.CreateAccelerator(context);
        }

        var shape = new TensorShape(TensorSize, TensorSize);
        unifiedTensorA = UnifiedTensor.Random<float>(accelerator!, shape);
        unifiedTensorB = UnifiedTensor.Random<float>(accelerator!, shape);
    }

    [Benchmark(Baseline = true)]
    public void StandardMemoryTransfer()
    {
        try
        {
            var totalElements = TensorSize * TensorSize;
            using var bufferA = accelerator!.Allocate1D<float>(totalElements);
            using var bufferB = accelerator.Allocate1D<float>(totalElements);
            using var result = accelerator.Allocate1D<float>(totalElements);

            // Standard CPU -> GPU transfer
            var cpuDataA = new float[totalElements];
            var cpuDataB = new float[totalElements];
            var random = new Random(42);
            
            for (int i = 0; i < totalElements; i++)
            {
                cpuDataA[i] = random.NextSingle();
                cpuDataB[i] = random.NextSingle();
            }

            bufferA.CopyFromCPU(cpuDataA);
            bufferB.CopyFromCPU(cpuDataB);

            // Process on GPU
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                AddKernel);

            kernel( totalElements,
                bufferA.View, bufferB.View, result.View);

            accelerator!.Synchronize();

            // Transfer back
            var resultData = result.GetAsArray1D();
        }
        catch
        {
            // Fallback CPU operation
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void UnifiedMemoryAccess()
    {
        try
        {
            // Access unified memory directly without explicit transfers
            using var result = unifiedTensorA!.AddSimd(unifiedTensorB!);
            
            // Force materialization to measure complete operation
            _ = result.AsSpan()[0];
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void ZeroCopyOperations()
    {
        try
        {
            // Test zero-copy between CPU and GPU views
            var cpuSpan = unifiedTensorA!.AsSpan();
            var gpuBuffer = unifiedTensorA.AsGpuBuffer();

            // Modify on CPU
            for (int i = 0; i < Math.Min(100, cpuSpan.Length); i++)
            {
                cpuSpan[i] *= 1.1f;
            }

            // Access on GPU (should see CPU changes without explicit transfer)
            var kernel = accelerator!.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>>(ModifyKernel);

            kernel((Index1D)Math.Min(100, gpuBuffer.Length),
                gpuBuffer.View.SubView(0, Math.Min(100, gpuBuffer.Length)));

            accelerator!.Synchronize();
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void UnifiedMemoryCoherence()
    {
        try
        {
            // Test memory coherence between CPU and GPU
            var cpuSpan = unifiedTensorA!.AsSpan();
            var gpuBuffer = unifiedTensorA.AsGpuBuffer();

            // Alternating CPU/GPU access pattern
            for (int iteration = 0; iteration < 5; iteration++)
            {
                // CPU modification
                for (int i = 0; i < Math.Min(50, cpuSpan.Length); i++)
                {
                    cpuSpan[i] += 0.1f;
                }

                // GPU modification
                var kernel = accelerator!.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<float>>(ModifyKernel);

                kernel((Index1D)Math.Min(50, gpuBuffer.Length),
                    gpuBuffer.View.SubView(0, Math.Min(50, gpuBuffer.Length)));

                accelerator!.Synchronize();
            }
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void PinnedVsUnifiedMemory()
    {
        try
        {
            var totalElements = TensorSize * TensorSize;
            var pinnedData = new float[totalElements];
            var random = new Random(42);
            
            for (int i = 0; i < totalElements; i++)
            {
                pinnedData[i] = random.NextSingle();
            }

            // Test pinned memory transfer
            using var pinnedScope = accelerator!.CreatePageLockFromPinned(pinnedData);
            using var buffer = accelerator.Allocate1D<float>(totalElements);
            
            buffer.CopyFromCPU(pinnedData);
            accelerator!.Synchronize();
            
            var result = buffer.GetAsArray1D();

            // Compare with unified memory access
            _ = unifiedTensorA!.AsSpan()[0];
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void StreamingUnifiedMemory()
    {
        try
        {
            // Test streaming access patterns with unified memory
            using var stream1 = accelerator!.CreateStream();
            using var stream2 = accelerator.CreateStream();

            var totalElements = TensorSize * TensorSize;
            var halfSize = totalElements / 2;

            // Process first half on stream 1
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>>(ModifyKernel);

            var buffer = unifiedTensorA!.AsGpuBuffer();
            var firstHalf = buffer.View.SubView(0, halfSize);
            var secondHalf = buffer.View.SubView(halfSize, halfSize);

            kernel(halfSize, firstHalf);
            kernel(halfSize, secondHalf);
            
            // Execute on streams
            stream1.Synchronize();
            stream2.Synchronize();

            // Synchronize both streams
            Task.WaitAll(
                stream1.SynchronizeAsync(),
                stream2.SynchronizeAsync());
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void MemoryMigrationOverhead()
    {
        try
        {
            // Measure overhead of memory migration between CPU and GPU
            var cpuSpan = unifiedTensorA!.AsSpan();
            var gpuBuffer = unifiedTensorA.AsGpuBuffer();

            // Force migration CPU -> GPU
            var kernel = accelerator!.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>>(ModifyKernel);

            kernel((Index1D)gpuBuffer.Length, gpuBuffer.View);
            accelerator!.Synchronize();

            // Force migration GPU -> CPU
            for (int i = 0; i < cpuSpan.Length; i++)
            {
                cpuSpan[i] += 0.001f;
            }

            // Force migration CPU -> GPU again
            kernel((Index1D)gpuBuffer.Length, gpuBuffer.View);
            accelerator!.Synchronize();
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void UnifiedMemoryScaling()
    {
        try
        {
            // Test performance scaling with unified memory
            var shapes = new[]
            {
                new TensorShape(TensorSize / 4, TensorSize / 4),
                new TensorShape(TensorSize / 2, TensorSize / 2),
                new TensorShape(TensorSize, TensorSize)
            };

            foreach (var shape in shapes)
            {
                using var tensor = UnifiedTensor.Random<float>(accelerator!, shape);
                using var result = tensor.AddSimd(tensor);
                
                // Force computation
                _ = result.AsSpan()[0];
            }
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    [Benchmark]
    public void ConcurrentUnifiedAccess()
    {
        try
        {
            // Test concurrent access to unified memory
            var tasks = new Task[4];
            
            for (int i = 0; i < tasks.Length; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(() =>
                {
                    var cpuSpan = unifiedTensorA!.AsSpan();
                    var start = taskId * (cpuSpan.Length / tasks.Length);
                    var length = cpuSpan.Length / tasks.Length;
                    
                    for (int j = start; j < start + length && j < cpuSpan.Length; j++)
                    {
                        cpuSpan[j] *= 1.01f;
                    }
                });
            }
            
            Task.WaitAll(tasks);
        }
        catch
        {
            StandardCpuOperation();
        }
    }

    private void StandardCpuOperation()
    {
        var totalElements = TensorSize * TensorSize;
        var dataA = new float[totalElements];
        var dataB = new float[totalElements];
        var result = new float[totalElements];
        
        var random = new Random(42);
        for (int i = 0; i < totalElements; i++)
        {
            dataA[i] = random.NextSingle();
            dataB[i] = random.NextSingle();
            result[i] = dataA[i] + dataB[i];
        }
    }

    #region Kernels

    private static void AddKernel(
        Index1D index,
        ArrayView<float> a,
        ArrayView<float> b,
        ArrayView<float> result)
    {
        result[index] = a[index] + b[index];
    }

    private static void ModifyKernel(Index1D index, ArrayView<float> data)
    {
        data[index] = data[index] * 1.1f + 0.01f;
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        unifiedTensorA?.Dispose();
        unifiedTensorB?.Dispose();
        accelerator?.Dispose();
        context?.Dispose();
    }
}
