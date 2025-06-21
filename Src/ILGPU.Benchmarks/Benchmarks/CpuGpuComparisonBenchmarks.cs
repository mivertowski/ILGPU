// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: CpuGpuComparisonBenchmarks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Runtime;
using ILGPU.SIMD;
using System.Numerics;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks comparing CPU SIMD vs GPU execution performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class CpuGpuComparisonBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? cpuAccelerator;
    private Accelerator? gpuAccelerator;
    private float[]? cpuData;

    [Params(1024, 8192, 65536, 262144)]
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        context = Context.Create(builder => builder.Cuda().CPU());
        cpuAccelerator = context.CreateCPUAccelerator(0);
        
        try
        {
            gpuAccelerator = context.GetPreferredDevice(AcceleratorType.Cuda);
        }
        catch
        {
            // GPU not available, CPU-only comparison
            gpuAccelerator = null;
        }

        cpuData = new float[DataSize];
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            cpuData[i] = random.NextSingle() * 2.0f - 1.0f;
        }
    }

    [Benchmark(Baseline = true)]
    public void ScalarCpuAddition()
    {
        var vectorB = new float[DataSize];
        var result = new float[DataSize];
        
        var random = new Random(123);
        for (int i = 0; i < DataSize; i++)
        {
            vectorB[i] = random.NextSingle();
        }

        for (int i = 0; i < DataSize; i++)
        {
            result[i] = cpuData![i] + vectorB[i];
        }
    }

    [Benchmark]
    public void SimdCpuAddition()
    {
        var vectorB = new float[DataSize];
        var result = new float[DataSize];
        var vectorSize = Vector<float>.Count;
        
        var random = new Random(123);
        for (int i = 0; i < DataSize; i++)
        {
            vectorB[i] = random.NextSingle();
        }

        int i = 0;
        for (; i <= DataSize - vectorSize; i += vectorSize)
        {
            var vecA = new Vector<float>(cpuData!, i);
            var vecB = new Vector<float>(vectorB, i);
            var vecResult = vecA + vecB;
            vecResult.CopyTo(result, i);
        }

        // Handle remainder
        for (; i < DataSize; i++)
        {
            result[i] = cpuData![i] + vectorB[i];
        }
    }

    [Benchmark]
    public void ILGPUCpuAddition()
    {
        try
        {
            var vectorB = new float[DataSize];
            var result = new float[DataSize];
            
            var random = new Random(123);
            for (int i = 0; i < DataSize; i++)
            {
                vectorB[i] = random.NextSingle();
            }

            VectorOperations.Add<float>(
                cpuData.AsSpan(),
                vectorB.AsSpan(),
                result.AsSpan());
        }
        catch
        {
            ScalarCpuAddition();
        }
    }

    [Benchmark]
    public void GpuAddition()
    {
        if (gpuAccelerator == null)
        {
            SimdCpuAddition();
            return;
        }

        try
        {
            using var bufferA = gpuAccelerator.Allocate1D<float>(DataSize);
            using var bufferB = gpuAccelerator.Allocate1D<float>(DataSize);
            using var result = gpuAccelerator.Allocate1D<float>(DataSize);

            var vectorB = new float[DataSize];
            var random = new Random(123);
            for (int i = 0; i < DataSize; i++)
            {
                vectorB[i] = random.NextSingle();
            }

            bufferA.CopyFromCPU(cpuData!);
            bufferB.CopyFromCPU(vectorB);

            var kernel = gpuAccelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                VectorAddKernel);

            kernel(gpuAccelerator.DefaultStream, DataSize,
                bufferA.View, bufferB.View, result.View);
                
            gpuAccelerator.Synchronize();
        }
        catch
        {
            SimdCpuAddition();
        }
    }

    [Benchmark]
    public float ScalarCpuDotProduct()
    {
        var vectorB = new float[DataSize];
        var random = new Random(123);
        for (int i = 0; i < DataSize; i++)
        {
            vectorB[i] = random.NextSingle();
        }

        float sum = 0.0f;
        for (int i = 0; i < DataSize; i++)
        {
            sum += cpuData![i] * vectorB[i];
        }
        return sum;
    }

    [Benchmark]
    public float SimdCpuDotProduct()
    {
        var vectorB = new float[DataSize];
        var vectorSize = Vector<float>.Count;
        var sumVector = Vector<float>.Zero;
        
        var random = new Random(123);
        for (int i = 0; i < DataSize; i++)
        {
            vectorB[i] = random.NextSingle();
        }

        int i = 0;
        for (; i <= DataSize - vectorSize; i += vectorSize)
        {
            var vecA = new Vector<float>(cpuData!, i);
            var vecB = new Vector<float>(vectorB, i);
            sumVector += vecA * vecB;
        }

        float sum = Vector.Dot(sumVector, Vector<float>.One);

        // Handle remainder
        for (; i < DataSize; i++)
        {
            sum += cpuData![i] * vectorB[i];
        }

        return sum;
    }

    [Benchmark]
    public float GpuDotProduct()
    {
        if (gpuAccelerator == null)
        {
            return SimdCpuDotProduct();
        }

        try
        {
            using var bufferA = gpuAccelerator.Allocate1D<float>(DataSize);
            using var bufferB = gpuAccelerator.Allocate1D<float>(DataSize);
            using var result = gpuAccelerator.Allocate1D<float>(1);

            var vectorB = new float[DataSize];
            var random = new Random(123);
            for (int i = 0; i < DataSize; i++)
            {
                vectorB[i] = random.NextSingle();
            }

            bufferA.CopyFromCPU(cpuData!);
            bufferB.CopyFromCPU(vectorB);

            var kernel = gpuAccelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int>(
                DotProductKernel);

            kernel(gpuAccelerator.DefaultStream, DataSize,
                bufferA.View, bufferB.View, result.View, DataSize);
                
            gpuAccelerator.Synchronize();

            return result.GetAsArray1D()[0];
        }
        catch
        {
            return SimdCpuDotProduct();
        }
    }

    [Benchmark]
    public void MatrixMultiplyCpu()
    {
        int matrixSize = (int)Math.Sqrt(DataSize);
        if (matrixSize * matrixSize != DataSize) return;

        var matrixB = new float[DataSize];
        var result = new float[DataSize];
        
        var random = new Random(456);
        for (int i = 0; i < DataSize; i++)
        {
            matrixB[i] = random.NextSingle();
        }

        // Simple matrix multiplication
        for (int i = 0; i < matrixSize; i++)
        {
            for (int j = 0; j < matrixSize; j++)
            {
                float sum = 0.0f;
                for (int k = 0; k < matrixSize; k++)
                {
                    sum += cpuData![i * matrixSize + k] * matrixB[k * matrixSize + j];
                }
                result[i * matrixSize + j] = sum;
            }
        }
    }

    [Benchmark]
    public void MatrixMultiplyGpu()
    {
        if (gpuAccelerator == null)
        {
            MatrixMultiplyCpu();
            return;
        }

        int matrixSize = (int)Math.Sqrt(DataSize);
        if (matrixSize * matrixSize != DataSize)
        {
            MatrixMultiplyCpu();
            return;
        }

        try
        {
            using var bufferA = gpuAccelerator.Allocate1D<float>(DataSize);
            using var bufferB = gpuAccelerator.Allocate1D<float>(DataSize);
            using var result = gpuAccelerator.Allocate1D<float>(DataSize);

            var matrixB = new float[DataSize];
            var random = new Random(456);
            for (int i = 0; i < DataSize; i++)
            {
                matrixB[i] = random.NextSingle();
            }

            bufferA.CopyFromCPU(cpuData!);
            bufferB.CopyFromCPU(matrixB);

            var kernel = gpuAccelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int>(
                MatrixMultiplyKernel);

            kernel(gpuAccelerator.DefaultStream, new Index2D(matrixSize, matrixSize),
                bufferA.View, bufferB.View, result.View, matrixSize);
                
            gpuAccelerator.Synchronize();
        }
        catch
        {
            MatrixMultiplyCpu();
        }
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

    private static void DotProductKernel(
        Index1D index,
        ArrayView<float> a,
        ArrayView<float> b,
        ArrayView<float> result,
        int size)
    {
        // Simple reduction - not optimal but functional for benchmarking
        if (index == 0)
        {
            float sum = 0.0f;
            for (int i = 0; i < size; i++)
            {
                sum += a[i] * b[i];
            }
            result[0] = sum;
        }
    }

    private static void MatrixMultiplyKernel(
        Index2D index,
        ArrayView<float> matrixA,
        ArrayView<float> matrixB,
        ArrayView<float> result,
        int size)
    {
        if (index.X >= size || index.Y >= size)
            return;

        float sum = 0.0f;
        for (int k = 0; k < size; k++)
        {
            sum += matrixA[index.X * size + k] * matrixB[k * size + index.Y];
        }

        result[index.X * size + index.Y] = sum;
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        cpuAccelerator?.Dispose();
        gpuAccelerator?.Dispose();
        context?.Dispose();
        cpuData = null;
    }
}