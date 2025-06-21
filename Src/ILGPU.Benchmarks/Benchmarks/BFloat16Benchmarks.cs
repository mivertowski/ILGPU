// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: BFloat16Benchmarks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Numerics;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for BFloat16 (Brain Floating Point) operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class BFloat16Benchmarks
{
    private BFloat16[]? vectorA;
    private BFloat16[]? vectorB;
    private BFloat16[]? result;
    private float[]? fp32Result;

    [Params(1024, 4096, 16384, 65536)]
    public int VectorSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        vectorA = new BFloat16[VectorSize];
        vectorB = new BFloat16[VectorSize];
        result = new BFloat16[VectorSize];
        fp32Result = new float[VectorSize];

        var random = new Random(42);
        for (int i = 0; i < VectorSize; i++)
        {
            vectorA[i] = new BFloat16(random.NextSingle() * 2.0f - 1.0f);
            vectorB[i] = new BFloat16(random.NextSingle() * 2.0f - 1.0f);
        }
    }

    [Benchmark(Baseline = true)]
    public void BF16Addition()
    {
        for (int i = 0; i < VectorSize; i++)
        {
            result![i] = BFloat16.Add(vectorA![i], vectorB![i]);
        }
    }

    [Benchmark]
    public void BF16Multiplication()
    {
        for (int i = 0; i < VectorSize; i++)
        {
            result![i] = BFloat16.Multiply(vectorA![i], vectorB![i]);
        }
    }

    [Benchmark]
    public void BF16ToFP32Conversion()
    {
        for (int i = 0; i < VectorSize; i++)
        {
            fp32Result![i] = vectorA![i].ToFloat();
        }
    }

    [Benchmark]
    public void FP32ToBF16Conversion()
    {
        var fp32Data = new float[VectorSize];
        var random = new Random(42);
        for (int i = 0; i < VectorSize; i++)
        {
            fp32Data[i] = random.NextSingle();
        }

        for (int i = 0; i < VectorSize; i++)
        {
            result![i] = new BFloat16(fp32Data[i]);
        }
    }

    [Benchmark]
    public float BF16DotProduct()
    {
        float sum = 0.0f;
        for (int i = 0; i < VectorSize; i++)
        {
            var product = BFloat16.Multiply(vectorA![i], vectorB![i]);
            sum += product.ToFloat();
        }
        return sum;
    }

    [Benchmark]
    public void BF16MatrixMultiply()
    {
        // Use smaller matrix for BF16 to keep benchmark reasonable
        int matrixSize = Math.Min(256, (int)Math.Sqrt(VectorSize));
        var matrixA = new BFloat16[matrixSize * matrixSize];
        var matrixB = new BFloat16[matrixSize * matrixSize];
        var matrixResult = new BFloat16[matrixSize * matrixSize];

        var random = new Random(42);
        for (int i = 0; i < matrixA.Length; i++)
        {
            matrixA[i] = new BFloat16(random.NextSingle());
            matrixB[i] = new BFloat16(random.NextSingle());
        }

        // Matrix multiplication using BF16
        for (int i = 0; i < matrixSize; i++)
        {
            for (int j = 0; j < matrixSize; j++)
            {
                var sum = new BFloat16(0.0f);
                for (int k = 0; k < matrixSize; k++)
                {
                    var a = matrixA[i * matrixSize + k];
                    var b = matrixB[k * matrixSize + j];
                    var product = BFloat16.Multiply(a, b);
                    sum = BFloat16.Add(sum, product);
                }
                matrixResult[i * matrixSize + j] = sum;
            }
        }
    }

    [Benchmark]
    public void BF16VectorizedOperations()
    {
        // Simulate vectorized BF16 operations by processing in chunks
        const int chunkSize = 8;
        int vectorizedLength = VectorSize - (VectorSize % chunkSize);

        for (int i = 0; i < vectorizedLength; i += chunkSize)
        {
            // Process chunk of BF16 values
            for (int j = 0; j < chunkSize && (i + j) < VectorSize; j++)
            {
                result![i + j] = BFloat16.Add(vectorA![i + j], vectorB![i + j]);
            }
        }

        // Handle remainder
        for (int i = vectorizedLength; i < VectorSize; i++)
        {
            result![i] = BFloat16.Add(vectorA![i], vectorB![i]);
        }
    }

    [Benchmark]
    public void BF16MLWorkload()
    {
        // Simulate a typical ML workload: Weighted sum + activation
        var weights = new BFloat16[VectorSize];
        var bias = new BFloat16(0.1f);
        
        var random = new Random(42);
        for (int i = 0; i < VectorSize; i++)
        {
            weights[i] = new BFloat16(random.NextSingle() * 0.1f);
        }

        for (int i = 0; i < VectorSize; i++)
        {
            // Weighted sum
            var weighted = BFloat16.Multiply(vectorA![i], weights[i]);
            var biased = BFloat16.Add(weighted, bias);
            
            // Simple activation (ReLU-like)
            if (biased.ToFloat() > 0.0f)
                result![i] = biased;
            else
                result![i] = new BFloat16(0.0f);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        vectorA = null;
        vectorB = null;
        result = null;
        fp32Result = null;
    }
}