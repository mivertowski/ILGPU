// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MixedPrecisionBenchmarks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Numerics;
using ILGPU.Runtime;
using ILGPU.TensorCores;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for mixed precision operations (FP16, BF16, TF32, INT8).
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class MixedPrecisionBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? accelerator;

    [Params(128, 256, 512)]
    public int MatrixSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            context = Context.Create(builder => builder.Cuda().CPU());
            accelerator = context.GetPreferredDevice(AcceleratorType.Cuda) ??
                         context.GetPreferredDevice(AcceleratorType.CPU);
        }
        catch
        {
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUAccelerator(0);
        }
    }

    [Benchmark]
    public void FP16ToFP32Conversion()
    {
        var size = MatrixSize * MatrixSize;
        using var fp16Buffer = accelerator!.Allocate1D<Half>(size);
        using var fp32Buffer = accelerator.Allocate1D<float>(size);

        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<Half>, ArrayView<float>>(
            FP16ToFP32Kernel);

        kernel(accelerator.DefaultStream, size, fp16Buffer.View, fp32Buffer.View);
        accelerator.Synchronize();
    }

    [Benchmark]
    public void BF16Operations()
    {
        var size = MatrixSize * MatrixSize;
        var bf16Data = new BFloat16[size];
        var fp32Result = new float[size];

        var random = new Random(42);
        for (int i = 0; i < size; i++)
        {
            bf16Data[i] = new BFloat16(random.NextSingle());
        }

        for (int i = 0; i < size; i++)
        {
            fp32Result[i] = bf16Data[i].ToFloat() * 2.0f;
        }
    }

    [Benchmark]
    public void MixedPrecisionGEMM()
    {
        try
        {
            using var matrixA = accelerator!.Allocate1D<Half>(MatrixSize * MatrixSize);
            using var matrixB = accelerator.Allocate1D<Half>(MatrixSize * MatrixSize);
            using var result = accelerator.Allocate1D<float>(MatrixSize * MatrixSize);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<Half>, ArrayView<Half>, ArrayView<float>, int>(
                MixedPrecisionGEMMKernel);

            kernel(accelerator.DefaultStream, new Index2D(MatrixSize, MatrixSize),
                matrixA.View, matrixB.View, result.View, MatrixSize);
            accelerator.Synchronize();
        }
        catch
        {
            // Fallback operation
            _ = MatrixSize;
        }
    }

    [Benchmark]
    public void QuantizedOperations()
    {
        var size = MatrixSize * MatrixSize;
        var int8Data = new sbyte[size];
        var fp32Result = new float[size];

        var random = new Random(42);
        for (int i = 0; i < size; i++)
        {
            int8Data[i] = (sbyte)(random.Next(-128, 128));
        }

        // Simulate quantized to float conversion
        const float scale = 1.0f / 127.0f;
        for (int i = 0; i < size; i++)
        {
            fp32Result[i] = int8Data[i] * scale;
        }
    }

    #region Kernels

    private static void FP16ToFP32Kernel(
        Index1D index,
        ArrayView<Half> input,
        ArrayView<float> output)
    {
        output[index] = (float)input[index];
    }

    private static void MixedPrecisionGEMMKernel(
        Index2D index,
        ArrayView<Half> matrixA,
        ArrayView<Half> matrixB,
        ArrayView<float> result,
        int size)
    {
        if (index.X >= size || index.Y >= size)
            return;

        float sum = 0.0f;
        for (int k = 0; k < size; k++)
        {
            var a = (float)matrixA[index.X * size + k];
            var b = (float)matrixB[k * size + index.Y];
            sum += a * b;
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
        accelerator?.Dispose();
        context?.Dispose();
    }
}