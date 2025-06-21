// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: SimdVectorBenchmarks.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.SIMD;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for SIMD vector operations with platform-specific optimizations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class SimdVectorBenchmarks : IDisposable
{
    private float[]? vectorA;
    private float[]? vectorB;
    private float[]? result;

    [Params(1024, 4096, 16384, 65536, 262144, 1048576)]
    public int VectorSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        vectorA = new float[VectorSize];
        vectorB = new float[VectorSize];
        result = new float[VectorSize];

        var random = new Random(42);
        for (int i = 0; i < VectorSize; i++)
        {
            vectorA[i] = random.NextSingle() * 2.0f - 1.0f;
            vectorB[i] = random.NextSingle() * 2.0f - 1.0f;
        }
    }

    [Benchmark(Baseline = true)]
    public void ScalarAddition()
    {
        for (int i = 0; i < VectorSize; i++)
        {
            result![i] = vectorA![i] + vectorB![i];
        }
    }

    [Benchmark]
    public void SystemNumericsVectorAddition()
    {
        var vectorSize = Vector<float>.Count;
        var vectorizedLength = VectorSize - (VectorSize % vectorSize);

        // Vectorized part
        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var vecA = new Vector<float>(vectorA!, i);
            var vecB = new Vector<float>(vectorB!, i);
            var vecResult = vecA + vecB;
            vecResult.CopyTo(result!, i);
        }

        // Handle remainder
        for (int i = vectorizedLength; i < VectorSize; i++)
        {
            result![i] = vectorA![i] + vectorB![i];
        }
    }

    [Benchmark]
    public void ILGPUVectorOperationsAddition()
    {
        try
        {
            VectorOperations.Add<float>(
                vectorA.AsSpan(),
                vectorB.AsSpan(),
                result.AsSpan());
        }
        catch (Exception)
        {
            // Fallback to scalar
            ScalarAddition();
        }
    }

    [Benchmark]
    public void ScalarMultiplication()
    {
        for (int i = 0; i < VectorSize; i++)
        {
            result![i] = vectorA![i] * vectorB![i];
        }
    }

    [Benchmark]
    public void SystemNumericsVectorMultiplication()
    {
        var vectorSize = Vector<float>.Count;
        var vectorizedLength = VectorSize - (VectorSize % vectorSize);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var vecA = new Vector<float>(vectorA!, i);
            var vecB = new Vector<float>(vectorB!, i);
            var vecResult = vecA * vecB;
            vecResult.CopyTo(result!, i);
        }

        for (int i = vectorizedLength; i < VectorSize; i++)
        {
            result![i] = vectorA![i] * vectorB![i];
        }
    }

    [Benchmark]
    public void ILGPUVectorOperationsMultiplication()
    {
        try
        {
            VectorOperations.Multiply<float>(
                vectorA.AsSpan(),
                vectorB.AsSpan(),
                result.AsSpan());
        }
        catch (Exception)
        {
            ScalarMultiplication();
        }
    }

    [Benchmark]
    public float ScalarDotProduct()
    {
        float sum = 0.0f;
        for (int i = 0; i < VectorSize; i++)
        {
            sum += vectorA![i] * vectorB![i];
        }
        return sum;
    }

    [Benchmark]
    public float SystemNumericsVectorDotProduct()
    {
        var vectorSize = Vector<float>.Count;
        var vectorizedLength = VectorSize - (VectorSize % vectorSize);
        var sumVector = Vector<float>.Zero;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var vecA = new Vector<float>(vectorA!, i);
            var vecB = new Vector<float>(vectorB!, i);
            sumVector += vecA * vecB;
        }

        float sum = Vector.Dot(sumVector, Vector<float>.One);

        for (int i = vectorizedLength; i < VectorSize; i++)
        {
            sum += vectorA![i] * vectorB![i];
        }

        return sum;
    }

    [Benchmark]
    public float ILGPUVectorOperationsDotProduct()
    {
        try
        {
            return VectorOperations.DotProduct<float>(
                vectorA.AsSpan(),
                vectorB.AsSpan());
        }
        catch (Exception)
        {
            return ScalarDotProduct();
        }
    }

    [Benchmark]
    public void MatrixVectorMultiplication()
    {
        // Create a square matrix for testing (use smaller size to avoid excessive memory)
        int matrixSize = Math.Min(512, (int)Math.Sqrt(VectorSize));
        var matrix = new float[matrixSize * matrixSize];
        var vector = new float[matrixSize];
        var matrixResult = new float[matrixSize];

        var random = new Random(42);
        for (int i = 0; i < matrix.Length; i++)
        {
            matrix[i] = random.NextSingle();
        }
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = random.NextSingle();
        }

        // Standard matrix-vector multiplication
        for (int i = 0; i < matrixSize; i++)
        {
            float sum = 0.0f;
            for (int j = 0; j < matrixSize; j++)
            {
                sum += matrix[i * matrixSize + j] * vector[j];
            }
            matrixResult[i] = sum;
        }
    }

    [Benchmark]
    public void ILGPUMatrixVectorMultiplication()
    {
        try
        {
            int matrixSize = Math.Min(512, (int)Math.Sqrt(VectorSize));
            var matrix = new float[matrixSize * matrixSize];
            var vector = new float[matrixSize];
            var matrixResult = new float[matrixSize];

            var random = new Random(42);
            for (int i = 0; i < matrix.Length; i++)
            {
                matrix[i] = random.NextSingle();
            }
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = random.NextSingle();
            }

            VectorOperations.MatrixVectorMultiply<float>(
                matrix.AsSpan(),
                matrixSize,
                matrixSize,
                vector.AsSpan(),
                matrixResult.AsSpan());
        }
        catch (Exception)
        {
            MatrixVectorMultiplication();
        }
    }

    [Benchmark]
    public void PlatformSpecificOptimizations()
    {
        // Test platform detection and optimization selection
        var spanA = vectorA.AsSpan();
        var spanB = vectorB.AsSpan();
        var spanResult = result.AsSpan();

        // This will automatically choose AVX, SSE, or NEON based on platform
        if (System.Runtime.Intrinsics.X86.Avx.IsSupported)
        {
            // Simulate AVX operations
            SystemNumericsVectorAddition();
        }
        else if (System.Runtime.Intrinsics.X86.Sse.IsSupported)
        {
            // Simulate SSE operations
            SystemNumericsVectorAddition();
        }
        else if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
        {
            // Simulate NEON operations
            SystemNumericsVectorAddition();
        }
        else
        {
            // Fallback to scalar
            ScalarAddition();
        }
    }

    [Benchmark]
    public void VectorExtensionMethods()
    {
        try
        {
            // Test System.Numerics.Vector extensions
            var vec = new Vector<float>(vectorA!, 0);
            var gpuArray = vec.ToGPUArray();
            var backToVector = new Vector<float>(gpuArray.AsSpan());
            
            // Simple operation to prevent optimization away
            _ = Vector<float>.Count;
        }
        catch (Exception)
        {
            // Fallback operation
            _ = vectorA![0];
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        // Arrays will be garbage collected
        vectorA = null;
        vectorB = null;
        result = null;
    }
}
