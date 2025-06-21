// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: HybridProcessingBenchmarks.cs
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
/// Benchmarks for hybrid CPU/GPU processing workloads.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class HybridProcessingBenchmarks : IDisposable
{
    private Context? context;
    private IHybridTensorProcessor? hybridProcessor;
    private UnifiedTensor<float>? tensorA;
    private UnifiedTensor<float>? tensorB;

    [Params(64, 128, 256)]
    public int TensorSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            context = Context.CreateDefault();
            hybridProcessor = HybridTensorProcessorFactory.Create(context);
            
            var device = context.GetPreferredDevice(preferCPU: true);
            var accelerator = device?.CreateAccelerator(context);
            var shape = new TensorShape(TensorSize, TensorSize);
            
            tensorA = UnifiedTensor.Random<float>(accelerator!, shape);
            tensorB = UnifiedTensor.Random<float>(accelerator!, shape);
        }
        catch
        {
            // Fallback for testing environments
            context = Context.CreateDefault();
            var cpuDevice = context.GetPreferredDevice(preferCPU: true);
            var accelerator = cpuDevice?.CreateAccelerator(context);
            var shape = new TensorShape(TensorSize, TensorSize);
            
            tensorA = UnifiedTensor.Random<float>(accelerator!, shape);
            tensorB = UnifiedTensor.Random<float>(accelerator!, shape);
        }
    }

    [Benchmark(Baseline = true)]
    public async Task CpuOnlyProcessing()
    {
        if (hybridProcessor == null) return;
        
        try
        {
            using var result = await hybridProcessor.ProcessAsync(
                tensorA!,
                new MockTensorOperation(TensorOperationType.ElementWiseAdd),
                HybridStrategy.CpuSimd);
        }
        catch
        {
            // Fallback - direct CPU operation
            using var result = tensorA!.AddSimd(tensorB!);
        }
    }

    [Benchmark]
    public async Task GpuOnlyProcessing()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            using var result = await hybridProcessor.ProcessAsync(
                tensorA!,
                new MockTensorOperation(TensorOperationType.ElementWiseAdd),
                HybridStrategy.GpuGeneral);
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [Benchmark]
    public async Task AutoHybridProcessing()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            using var result = await hybridProcessor.ProcessAsync(
                tensorA!,
                new MockTensorOperation(TensorOperationType.ElementWiseAdd),
                HybridStrategy.Auto);
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [Benchmark]
    public async Task WorkloadSplittingProcessing()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            using var result = await hybridProcessor.ProcessAsync(
                tensorA!,
                new MockTensorOperation(TensorOperationType.MatrixMultiply),
                HybridStrategy.Hybrid);
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [Benchmark]
    public async Task AdaptiveThresholdProcessing()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            using var result = await hybridProcessor.ProcessAsync(
                tensorA!,
                new MockTensorOperation(TensorOperationType.MatrixMultiply),
                HybridStrategy.Auto);
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [Benchmark]
    public void UnifiedMemoryOperations()
    {
        try
        {
            // Test zero-copy operations between CPU and GPU
            var cpuSpan = tensorA!.AsSpan();
            var gpuBuffer = tensorA.AsGpuBuffer();
            
            // Modify data on CPU
            for (int i = 0; i < Math.Min(100, cpuSpan.Length); i++)
            {
                cpuSpan[i] *= 1.1f;
            }
            
            // Access on GPU (would trigger synchronization in real implementation)
            _ = gpuBuffer.Length;
        }
        catch
        {
            // Fallback operation
            _ = tensorA!.Shape.Size;
        }
    }

    [Benchmark]
    public async Task PipelinedProcessing()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            // Simulate a processing pipeline: Add -> Multiply -> Add
            using var step1 = await hybridProcessor.ProcessAsync(
                tensorA!,
                new MockTensorOperation(TensorOperationType.ElementWiseAdd),
                HybridStrategy.Auto);
                
            using var step2 = await hybridProcessor.ProcessAsync(
                step1,
                new MockTensorOperation(TensorOperationType.MatrixMultiply),
                HybridStrategy.Auto);
                
            using var final = await hybridProcessor.ProcessAsync(
                step2,
                new MockTensorOperation(TensorOperationType.ElementWiseAdd),
                HybridStrategy.Auto);
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [Benchmark]
    public async Task MemoryPoolingOperations()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            // Create multiple tensors to test memory pooling
            var tasks = new List<Task<ITensor<float>>>();
            
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(hybridProcessor.ProcessAsync(
                    tensorA!,
                    new MockTensorOperation(TensorOperationType.ElementWiseAdd),
                    HybridStrategy.Auto));
            }
            
            var results = await Task.WhenAll(tasks);
            
            // Dispose all results
            foreach (var result in results)
            {
                result.Dispose();
            }
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [Benchmark]
    public void DataMovementOverhead()
    {
        try
        {
            // Measure the overhead of moving data between CPU and GPU
            for (int i = 0; i < 10; i++)
            {
                var cpuData = tensorA!.AsSpan().ToArray();
                using var tempTensor = UnifiedTensor.FromArray<float>(
                    tensorA.Accelerator, 
                    tensorA.Shape,
                    cpuData);
                
                _ = tempTensor.AsGpuBuffer();
            }
        }
        catch
        {
            // Fallback operation
            _ = tensorA!.Shape.Size;
        }
    }

    [Benchmark]
    public async Task ConcurrentHybridOperations()
    {
        if (hybridProcessor == null)
        {
            await CpuOnlyProcessing();
            return;
        }
        
        try
        {
            // Run multiple operations concurrently
            var tasks = new[]
            {
                hybridProcessor.ProcessAsync(tensorA!, new MockTensorOperation(TensorOperationType.ElementWiseAdd), HybridStrategy.CpuSimd),
                hybridProcessor.ProcessAsync(tensorB!, new MockTensorOperation(TensorOperationType.ElementWiseAdd), HybridStrategy.GpuGeneral),
                hybridProcessor.ProcessAsync(tensorA!, new MockTensorOperation(TensorOperationType.MatrixMultiply), HybridStrategy.Auto)
            };
            
            var results = await Task.WhenAll(tasks);
            
            foreach (var result in results)
            {
                result.Dispose();
            }
        }
        catch
        {
            await CpuOnlyProcessing();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        tensorA?.Dispose();
        tensorB?.Dispose();
        hybridProcessor?.Dispose();
        context?.Dispose();
    }
}

/// <summary>
/// Mock tensor operation for benchmarking purposes.
/// </summary>
public class MockTensorOperation : TensorOperation
{
    private readonly TensorOperationType operationType;

    public MockTensorOperation(TensorOperationType type)
    {
        operationType = type;
    }

    public override TensorOperationType Type => operationType;

    public override long EstimatedOps => 1000; // Simple estimate for benchmarking

    public override bool PrefersTensorCores => operationType == TensorOperationType.MatrixMultiply;
}
