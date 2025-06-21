// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MatrixVectorBenchmarks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.SIMD;
using System.Numerics;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for matrix-vector operations and linear algebra primitives.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class MatrixVectorBenchmarks
{
    private float[]? matrix;
    private float[]? vector;
    private float[]? result;

    [Params(64, 128, 256, 512, 1024)]
    public int MatrixSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        matrix = new float[MatrixSize * MatrixSize];
        vector = new float[MatrixSize];
        result = new float[MatrixSize];

        var random = new Random(42);
        for (int i = 0; i < matrix.Length; i++)
        {
            matrix[i] = random.NextSingle() * 2.0f - 1.0f;
        }
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = random.NextSingle() * 2.0f - 1.0f;
        }
    }

    [Benchmark(Baseline = true)]
    public void ScalarMatrixVectorMultiply()
    {
        for (int i = 0; i < MatrixSize; i++)
        {
            float sum = 0.0f;
            for (int j = 0; j < MatrixSize; j++)
            {
                sum += matrix![i * MatrixSize + j] * vector![j];
            }
            result![i] = sum;
        }
    }

    [Benchmark]
    public void VectorizedMatrixVectorMultiply()
    {
        var vectorSize = Vector<float>.Count;
        
        for (int i = 0; i < MatrixSize; i++)
        {
            var sumVector = Vector<float>.Zero;
            int j = 0;
            
            // Vectorized part
            for (; j <= MatrixSize - vectorSize; j += vectorSize)
            {
                var matrixVec = new Vector<float>(matrix!, i * MatrixSize + j);
                var vectorVec = new Vector<float>(vector!, j);
                sumVector += matrixVec * vectorVec;
            }
            
            // Sum vector elements
            float sum = Vector.Dot(sumVector, Vector<float>.One);
            
            // Handle remainder
            for (; j < MatrixSize; j++)
            {
                sum += matrix![i * MatrixSize + j] * vector![j];
            }
            
            result![i] = sum;
        }
    }

    [Benchmark]
    public void ILGPUMatrixVectorMultiply()
    {
        try
        {
            VectorOperations.MatrixVectorMultiply<float>(
                matrix.AsSpan(),
                MatrixSize,
                MatrixSize,
                vector.AsSpan(),
                result.AsSpan());
        }
        catch
        {
            ScalarMatrixVectorMultiply();
        }
    }

    [Benchmark]
    public void TransposedMatrixVectorMultiply()
    {
        // A^T * v where A^T is the transpose of matrix A
        for (int i = 0; i < MatrixSize; i++)
        {
            float sum = 0.0f;
            for (int j = 0; j < MatrixSize; j++)
            {
                sum += matrix![j * MatrixSize + i] * vector![j]; // Transposed access
            }
            result![i] = sum;
        }
    }

    [Benchmark]
    public void OuterProduct()
    {
        // Compute v * u^T (outer product of two vectors)
        var vector2 = new float[MatrixSize];
        var outerResult = new float[MatrixSize * MatrixSize];
        
        var random = new Random(123);
        for (int i = 0; i < MatrixSize; i++)
        {
            vector2[i] = random.NextSingle();
        }

        for (int i = 0; i < MatrixSize; i++)
        {
            for (int j = 0; j < MatrixSize; j++)
            {
                outerResult[i * MatrixSize + j] = vector![i] * vector2[j];
            }
        }
    }

    [Benchmark]
    public void VectorizedOuterProduct()
    {
        var vector2 = new float[MatrixSize];
        var outerResult = new float[MatrixSize * MatrixSize];
        var vectorSize = Vector<float>.Count;
        
        var random = new Random(123);
        for (int i = 0; i < MatrixSize; i++)
        {
            vector2[i] = random.NextSingle();
        }

        for (int i = 0; i < MatrixSize; i++)
        {
            var vi = new Vector<float>(vector![i]);
            int j = 0;
            
            for (; j <= MatrixSize - vectorSize; j += vectorSize)
            {
                var v2Vec = new Vector<float>(vector2, j);
                var product = vi * v2Vec;
                product.CopyTo(outerResult, i * MatrixSize + j);
            }
            
            // Handle remainder
            for (; j < MatrixSize; j++)
            {
                outerResult[i * MatrixSize + j] = vector[i] * vector2[j];
            }
        }
    }

    [Benchmark]
    public float VectorNorm()
    {
        float sum = 0.0f;
        for (int i = 0; i < MatrixSize; i++)
        {
            sum += vector![i] * vector[i];
        }
        return MathF.Sqrt(sum);
    }

    [Benchmark]
    public float VectorizedVectorNorm()
    {
        var vectorSize = Vector<float>.Count;
        var sumVector = Vector<float>.Zero;
        int i = 0;
        
        for (; i <= MatrixSize - vectorSize; i += vectorSize)
        {
            var vec = new Vector<float>(vector!, i);
            sumVector += vec * vec;
        }
        
        float sum = Vector.Dot(sumVector, Vector<float>.One);
        
        // Handle remainder
        for (; i < MatrixSize; i++)
        {
            sum += vector![i] * vector[i];
        }
        
        return MathF.Sqrt(sum);
    }

    [Benchmark]
    public void MatrixAddition()
    {
        var matrix2 = new float[MatrixSize * MatrixSize];
        var matrixResult = new float[MatrixSize * MatrixSize];
        
        var random = new Random(456);
        for (int i = 0; i < matrix2.Length; i++)
        {
            matrix2[i] = random.NextSingle();
        }

        for (int i = 0; i < matrix!.Length; i++)
        {
            matrixResult[i] = matrix[i] + matrix2[i];
        }
    }

    [Benchmark]
    public void VectorizedMatrixAddition()
    {
        var matrix2 = new float[MatrixSize * MatrixSize];
        var matrixResult = new float[MatrixSize * MatrixSize];
        var vectorSize = Vector<float>.Count;
        
        var random = new Random(456);
        for (int i = 0; i < matrix2.Length; i++)
        {
            matrix2[i] = random.NextSingle();
        }

        int totalElements = MatrixSize * MatrixSize;
        int i = 0;
        
        for (; i <= totalElements - vectorSize; i += vectorSize)
        {
            var vec1 = new Vector<float>(matrix!, i);
            var vec2 = new Vector<float>(matrix2, i);
            var sum = vec1 + vec2;
            sum.CopyTo(matrixResult, i);
        }
        
        // Handle remainder
        for (; i < totalElements; i++)
        {
            matrixResult[i] = matrix![i] + matrix2[i];
        }
    }

    [Benchmark]
    public void BlockedMatrixVectorMultiply()
    {
        // Use cache-friendly blocked algorithm
        const int blockSize = 64;
        
        Array.Clear(result!);
        
        for (int ii = 0; ii < MatrixSize; ii += blockSize)
        {
            for (int jj = 0; jj < MatrixSize; jj += blockSize)
            {
                int iEnd = Math.Min(ii + blockSize, MatrixSize);
                int jEnd = Math.Min(jj + blockSize, MatrixSize);
                
                for (int i = ii; i < iEnd; i++)
                {
                    float sum = 0.0f;
                    for (int j = jj; j < jEnd; j++)
                    {
                        sum += matrix![i * MatrixSize + j] * vector![j];
                    }
                    result[i] += sum;
                }
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        matrix = null;
        vector = null;
        result = null;
    }
}