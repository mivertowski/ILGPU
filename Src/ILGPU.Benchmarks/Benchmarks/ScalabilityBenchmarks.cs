// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: ScalabilityBenchmarks.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Runtime;
using ILGPU.SIMD;
using System.Numerics;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for performance scaling across different problem sizes and parallelism levels.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class ScalabilityBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? accelerator;
    private Dictionary<int, float[]> testDataSets = new();

    [Params(1024, 4096, 16384, 65536, 262144, 1048576)] // 1K to 1M elements
    public int ProblemSize { get; set; }

    [Params(1, 2, 4, 8)] // Different parallelism levels
    public int ParallelismLevel { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        context = Context.CreateDefault();
        var device = context.GetPreferredDevice(preferCPU: false); // GPU preferred, CPU fallback
        accelerator = device?.CreateAccelerator(context);

        // Pre-generate test data for all problem sizes
        var sizes = new[] { 1024, 4096, 16384, 65536, 262144, 1048576 };
        var random = new Random(42);
        
        foreach (var size in sizes)
        {
            var data = new float[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = random.NextSingle() * 2.0f - 1.0f;
            }
            testDataSets[size] = data;
        }
    }

    [Benchmark]
    public void VectorAdditionScaling()
    {
        var data = testDataSets[ProblemSize];
        var vectorB = new float[ProblemSize];
        var result = new float[ProblemSize];
        
        var random = new Random(123);
        for (int i = 0; i < ProblemSize; i++)
        {
            vectorB[i] = random.NextSingle();
        }

        if (ParallelismLevel == 1)
        {
            // Serial execution
            for (int i = 0; i < ProblemSize; i++)
            {
                result[i] = data[i] + vectorB[i];
            }
        }
        else
        {
            // Parallel execution
            var partitionSize = ProblemSize / ParallelismLevel;
            var tasks = new Task[ParallelismLevel];
            
            for (int p = 0; p < ParallelismLevel; p++)
            {
                int partition = p;
                tasks[p] = Task.Run(() =>
                {
                    var start = partition * partitionSize;
                    var end = (partition == ParallelismLevel - 1) ? ProblemSize : start + partitionSize;
                    
                    for (int i = start; i < end; i++)
                    {
                        result[i] = data[i] + vectorB[i];
                    }
                });
            }
            
            Task.WaitAll(tasks);
        }
    }

    [Benchmark]
    public void SimdVectorAdditionScaling()
    {
        var data = testDataSets[ProblemSize];
        var vectorB = new float[ProblemSize];
        var result = new float[ProblemSize];
        
        var random = new Random(123);
        for (int i = 0; i < ProblemSize; i++)
        {
            vectorB[i] = random.NextSingle();
        }

        try
        {
            VectorOperations.Add<float>(
                data.AsSpan(),
                vectorB.AsSpan(),
                result.AsSpan());
        }
        catch
        {
            // Fallback to Vector<T>
            var vectorSize = Vector<float>.Count;
            int i = 0;
            
            for (; i <= ProblemSize - vectorSize; i += vectorSize)
            {
                var vecA = new Vector<float>(data, i);
                var vecB = new Vector<float>(vectorB, i);
                var vecResult = vecA + vecB;
                vecResult.CopyTo(result, i);
            }
            
            // Handle remainder
            for (; i < ProblemSize; i++)
            {
                result[i] = data[i] + vectorB[i];
            }
        }
    }

    [Benchmark]
    public void GpuComputeScaling()
    {
        if (accelerator == null)
        {
            VectorAdditionScaling();
            return;
        }

        try
        {
            var data = testDataSets[ProblemSize];
            var vectorB = new float[ProblemSize];
            var random = new Random(123);
            
            for (int i = 0; i < ProblemSize; i++)
            {
                vectorB[i] = random.NextSingle();
            }

            using var bufferA = accelerator.Allocate1D<float>(ProblemSize);
            using var bufferB = accelerator.Allocate1D<float>(ProblemSize);
            using var result = accelerator.Allocate1D<float>(ProblemSize);

            bufferA.CopyFromCPU(data);
            bufferB.CopyFromCPU(vectorB);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                VectorAddKernel);

            kernel( ProblemSize,
                bufferA.View, bufferB.View, result.View);
                
            accelerator.Synchronize();
            
            var resultData = result.GetAsArray1D();
        }
        catch
        {
            VectorAdditionScaling();
        }
    }

    [Benchmark]
    public void MemoryBandwidthScaling()
    {
        if (accelerator == null)
        {
            var data = testDataSets[ProblemSize];
            var copy = new float[ProblemSize];
            Array.Copy(data, copy, ProblemSize);
            return;
        }

        try
        {
            var data = testDataSets[ProblemSize];
            
            using var buffer = accelerator.Allocate1D<float>(ProblemSize);
            
            // Upload
            buffer.CopyFromCPU(data);
            
            // Download
            var result = buffer.GetAsArray1D();
        }
        catch
        {
            var data = testDataSets[ProblemSize];
            var copy = new float[ProblemSize];
            Array.Copy(data, copy, ProblemSize);
        }
    }

    [Benchmark]
    public void MatrixMultiplicationScaling()
    {
        var matrixSize = (int)Math.Sqrt(ProblemSize);
        if (matrixSize * matrixSize != ProblemSize) return;

        var matrixA = testDataSets[ProblemSize];
        var matrixB = new float[ProblemSize];
        var result = new float[ProblemSize];
        
        var random = new Random(456);
        for (int i = 0; i < ProblemSize; i++)
        {
            matrixB[i] = random.NextSingle();
        }

        if (ParallelismLevel == 1)
        {
            // Serial matrix multiplication
            for (int i = 0; i < matrixSize; i++)
            {
                for (int j = 0; j < matrixSize; j++)
                {
                    float sum = 0.0f;
                    for (int k = 0; k < matrixSize; k++)
                    {
                        sum += matrixA[i * matrixSize + k] * matrixB[k * matrixSize + j];
                    }
                    result[i * matrixSize + j] = sum;
                }
            }
        }
        else
        {
            // Parallel matrix multiplication
            var rowsPerTask = matrixSize / ParallelismLevel;
            var tasks = new Task[ParallelismLevel];
            
            for (int p = 0; p < ParallelismLevel; p++)
            {
                int partition = p;
                tasks[p] = Task.Run(() =>
                {
                    var startRow = partition * rowsPerTask;
                    var endRow = (partition == ParallelismLevel - 1) ? matrixSize : startRow + rowsPerTask;
                    
                    for (int i = startRow; i < endRow; i++)
                    {
                        for (int j = 0; j < matrixSize; j++)
                        {
                            float sum = 0.0f;
                            for (int k = 0; k < matrixSize; k++)
                            {
                                sum += matrixA[i * matrixSize + k] * matrixB[k * matrixSize + j];
                            }
                            result[i * matrixSize + j] = sum;
                        }
                    }
                });
            }
            
            Task.WaitAll(tasks);
        }
    }

    [Benchmark]
    public void ReductionScaling()
    {
        var data = testDataSets[ProblemSize];
        
        if (ParallelismLevel == 1)
        {
            // Serial reduction
            float sum = 0.0f;
            for (int i = 0; i < ProblemSize; i++)
            {
                sum += data[i];
            }
        }
        else
        {
            // Parallel reduction
            var partitionSize = ProblemSize / ParallelismLevel;
            var partialSums = new float[ParallelismLevel];
            var tasks = new Task[ParallelismLevel];
            
            for (int p = 0; p < ParallelismLevel; p++)
            {
                int partition = p;
                tasks[p] = Task.Run(() =>
                {
                    var start = partition * partitionSize;
                    var end = (partition == ParallelismLevel - 1) ? ProblemSize : start + partitionSize;
                    
                    float localSum = 0.0f;
                    for (int i = start; i < end; i++)
                    {
                        localSum += data[i];
                    }
                    partialSums[partition] = localSum;
                });
            }
            
            Task.WaitAll(tasks);
            
            // Final reduction
            float totalSum = 0.0f;
            for (int i = 0; i < ParallelismLevel; i++)
            {
                totalSum += partialSums[i];
            }
        }
    }

    [Benchmark]
    public void CacheEfficiencyScaling()
    {
        var data = testDataSets[ProblemSize];
        var result = new float[ProblemSize];
        
        // Sequential access (cache-friendly)
        for (int i = 0; i < ProblemSize; i++)
        {
            result[i] = data[i] * 2.0f;
        }
        
        // Random access (cache-unfriendly) for larger sizes
        if (ProblemSize > 4096)
        {
            var random = new Random(789);
            for (int i = 0; i < Math.Min(1000, ProblemSize); i++)
            {
                var index = random.Next(ProblemSize);
                result[index] = data[index] * 3.0f;
            }
        }
    }

    [Benchmark]
    public void ThreadScalingOverhead()
    {
        var data = testDataSets[ProblemSize];
        var result = new float[ProblemSize];
        
        // Measure threading overhead vs actual work
        var tasks = new Task[ParallelismLevel];
        var partitionSize = ProblemSize / ParallelismLevel;
        
        for (int p = 0; p < ParallelismLevel; p++)
        {
            int partition = p;
            tasks[p] = Task.Run(() =>
            {
                var start = partition * partitionSize;
                var end = (partition == ParallelismLevel - 1) ? ProblemSize : start + partitionSize;
                
                // Simple operation to measure scaling
                for (int i = start; i < end; i++)
                {
                    result[i] = data[i] + 1.0f;
                }
            });
        }
        
        Task.WaitAll(tasks);
    }

    [Benchmark]
    public void MemoryLatencyScaling()
    {
        var data = testDataSets[ProblemSize];
        var result = 0.0f;
        
        // Access pattern that tests memory latency at scale
        var stride = Math.Max(1, ProblemSize / 1000); // Adjust stride based on problem size
        
        for (int i = 0; i < ProblemSize; i += stride)
        {
            result += data[i];
        }
        
        // Prevent optimization
        _ = result;
    }

    #region Kernels

    private static void VectorAddKernel(
        Index1D index,
        ArrayView<float> a,
        ArrayView<float> b,
        ArrayView<float> result)
    {
        result[index] = a[index] + b[index];
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        accelerator?.Dispose();
        context?.Dispose();
        testDataSets.Clear();
    }
}
