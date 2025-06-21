// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: PlatformIntrinsicsBenchmarks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.SIMD;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for platform-specific intrinsics (AVX, SSE, NEON).
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class PlatformIntrinsicsBenchmarks
{
    private float[]? vectorA;
    private float[]? vectorB;
    private float[]? result;

    [Params(1024, 8192, 65536)]
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
    public void AvxAddition()
    {
        if (!Avx.IsSupported)
        {
            ScalarAddition();
            return;
        }

        unsafe
        {
            fixed (float* ptrA = vectorA)
            fixed (float* ptrB = vectorB)
            fixed (float* ptrResult = result)
            {
                int vectorizedLength = VectorSize & ~7; // Process 8 floats at a time
                
                for (int i = 0; i < vectorizedLength; i += 8)
                {
                    var vecA = Avx.LoadVector256(ptrA + i);
                    var vecB = Avx.LoadVector256(ptrB + i);
                    var vecResult = Avx.Add(vecA, vecB);
                    Avx.Store(ptrResult + i, vecResult);
                }

                // Handle remainder
                for (int i = vectorizedLength; i < VectorSize; i++)
                {
                    ptrResult[i] = ptrA[i] + ptrB[i];
                }
            }
        }
    }

    [Benchmark]
    public void SseAddition()
    {
        if (!Sse.IsSupported)
        {
            ScalarAddition();
            return;
        }

        unsafe
        {
            fixed (float* ptrA = vectorA)
            fixed (float* ptrB = vectorB)
            fixed (float* ptrResult = result)
            {
                int vectorizedLength = VectorSize & ~3; // Process 4 floats at a time
                
                for (int i = 0; i < vectorizedLength; i += 4)
                {
                    var vecA = Sse.LoadVector128(ptrA + i);
                    var vecB = Sse.LoadVector128(ptrB + i);
                    var vecResult = Sse.Add(vecA, vecB);
                    Sse.Store(ptrResult + i, vecResult);
                }

                // Handle remainder
                for (int i = vectorizedLength; i < VectorSize; i++)
                {
                    ptrResult[i] = ptrA[i] + ptrB[i];
                }
            }
        }
    }

    [Benchmark]
    public void NeonAddition()
    {
        if (!AdvSimd.IsSupported)
        {
            ScalarAddition();
            return;
        }

        unsafe
        {
            fixed (float* ptrA = vectorA)
            fixed (float* ptrB = vectorB)
            fixed (float* ptrResult = result)
            {
                int vectorizedLength = VectorSize & ~3; // Process 4 floats at a time
                
                for (int i = 0; i < vectorizedLength; i += 4)
                {
                    var vecA = AdvSimd.LoadVector128(ptrA + i);
                    var vecB = AdvSimd.LoadVector128(ptrB + i);
                    var vecResult = AdvSimd.Add(vecA, vecB);
                    AdvSimd.Store(ptrResult + i, vecResult);
                }

                // Handle remainder
                for (int i = vectorizedLength; i < VectorSize; i++)
                {
                    ptrResult[i] = ptrA[i] + ptrB[i];
                }
            }
        }
    }

    [Benchmark]
    public void ILGPUPlatformOptimizedAddition()
    {
        try
        {
            VectorOperations.Add<float>(
                vectorA.AsSpan(),
                vectorB.AsSpan(),
                result.AsSpan());
        }
        catch
        {
            ScalarAddition();
        }
    }

    [Benchmark]
    public float AvxDotProduct()
    {
        if (!Avx.IsSupported)
        {
            return ScalarDotProduct();
        }

        unsafe
        {
            fixed (float* ptrA = vectorA)
            fixed (float* ptrB = vectorB)
            {
                var sumVec = Vector256<float>.Zero;
                int vectorizedLength = VectorSize & ~7;
                
                for (int i = 0; i < vectorizedLength; i += 8)
                {
                    var vecA = Avx.LoadVector256(ptrA + i);
                    var vecB = Avx.LoadVector256(ptrB + i);
                    var product = Avx.Multiply(vecA, vecB);
                    sumVec = Avx.Add(sumVec, product);
                }

                // Sum all elements in the vector
                var sum = sumVec.GetElement(0) + sumVec.GetElement(1) + 
                         sumVec.GetElement(2) + sumVec.GetElement(3) +
                         sumVec.GetElement(4) + sumVec.GetElement(5) + 
                         sumVec.GetElement(6) + sumVec.GetElement(7);

                // Handle remainder
                for (int i = vectorizedLength; i < VectorSize; i++)
                {
                    sum += ptrA[i] * ptrB[i];
                }

                return sum;
            }
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
    public void FmaOperations()
    {
        if (!Fma.IsSupported)
        {
            // Simulate FMA with separate multiply and add
            for (int i = 0; i < VectorSize; i++)
            {
                result![i] = vectorA![i] * vectorB![i] + result![i];
            }
            return;
        }

        unsafe
        {
            fixed (float* ptrA = vectorA)
            fixed (float* ptrB = vectorB)
            fixed (float* ptrResult = result)
            {
                int vectorizedLength = VectorSize & ~7;
                
                for (int i = 0; i < vectorizedLength; i += 8)
                {
                    var vecA = Avx.LoadVector256(ptrA + i);
                    var vecB = Avx.LoadVector256(ptrB + i);
                    var vecC = Avx.LoadVector256(ptrResult + i);
                    var fmaResult = Fma.MultiplyAdd(vecA, vecB, vecC);
                    Avx.Store(ptrResult + i, fmaResult);
                }

                // Handle remainder
                for (int i = vectorizedLength; i < VectorSize; i++)
                {
                    ptrResult[i] = ptrA[i] * ptrB[i] + ptrResult[i];
                }
            }
        }
    }

    [Benchmark]
    public void PlatformDetectionOverhead()
    {
        // Measure the overhead of platform detection
        for (int i = 0; i < 1000; i++)
        {
            bool hasAvx = Avx.IsSupported;
            bool hasSse = Sse.IsSupported;
            bool hasNeon = AdvSimd.IsSupported;
            bool hasFma = Fma.IsSupported;
            
            // Prevent optimization
            _ = hasAvx || hasSse || hasNeon || hasFma;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        vectorA = null;
        vectorB = null;
        result = null;
    }
}