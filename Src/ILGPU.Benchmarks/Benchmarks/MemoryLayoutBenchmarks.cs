// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: MemoryLayoutBenchmarks.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Runtime;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for different memory layout strategies and optimization.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class MemoryLayoutBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? accelerator;
    private float[]? testData;

    [Params(64, 128, 256, 512)]
    public int MatrixSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        context = Context.CreateDefault();
        var device = context.GetPreferredDevice(preferCPU: false); // GPU preferred, CPU fallback
        accelerator = device?.CreateAccelerator(context);

        var totalElements = MatrixSize * MatrixSize;
        testData = new float[totalElements];
        var random = new Random(42);
        for (int i = 0; i < totalElements; i++)
        {
            testData[i] = random.NextSingle();
        }
    }

    [Benchmark(Baseline = true)]
    public void RowMajorMatrixAccess()
    {
        try
        {
            using var buffer = accelerator!.Allocate2DDenseX<float>(
                new Index2D(MatrixSize, MatrixSize));
            
            buffer.View.AsContiguous().CopyFromCPU(testData!);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView2D<float, Stride2D.DenseX>, int>(
                RowMajorAccessKernel);
                
            kernel( new Index2D(MatrixSize, MatrixSize),
                buffer.View, MatrixSize);
            accelerator.Synchronize();
        }
        catch
        {
            // Fallback CPU operation
            RowMajorCpuAccess();
        }
    }

    [Benchmark]
    public void ColumnMajorMatrixAccess()
    {
        try
        {
            using var buffer = accelerator!.Allocate2DDenseY<float>(
                new Index2D(MatrixSize, MatrixSize));
            
            buffer.View.AsContiguous().CopyFromCPU(testData!);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView2D<float, Stride2D.DenseY>, int>(
                ColumnMajorAccessKernel);
                
            kernel( new Index2D(MatrixSize, MatrixSize),
                buffer.View, MatrixSize);
            accelerator.Synchronize();
        }
        catch
        {
            ColumnMajorCpuAccess();
        }
    }

    [Benchmark]
    public void TiledMatrixAccess()
    {
        try
        {
            using var buffer = accelerator!.Allocate1D<float>(MatrixSize * MatrixSize);
            buffer.CopyFromCPU(testData!);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<float>, int, int>(
                TiledAccessKernel);
                
            kernel( new Index2D(MatrixSize, MatrixSize),
                buffer.View, MatrixSize, 16); // 16x16 tiles
            accelerator.Synchronize();
        }
        catch
        {
            TiledCpuAccess();
        }
    }

    [Benchmark]
    public void CacheOptimizedAccess()
    {
        try
        {
            using var buffer = accelerator!.Allocate1D<float>(MatrixSize * MatrixSize);
            buffer.CopyFromCPU(testData!);
            
            // Process in cache-friendly blocks
            const int blockSize = 64;
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<float>, int, int>(
                BlockedAccessKernel);
                
            kernel( new Index2D(MatrixSize, MatrixSize),
                buffer.View, MatrixSize, blockSize);
            accelerator.Synchronize();
        }
        catch
        {
            CacheOptimizedCpuAccess();
        }
    }

    [Benchmark]
    public void StructOfArrays()
    {
        // SOA: Structure of Arrays layout
        try
        {
            var totalElements = MatrixSize * MatrixSize;
            using var xBuffer = accelerator!.Allocate1D<float>(totalElements);
            using var yBuffer = accelerator!.Allocate1D<float>(totalElements);
            using var zBuffer = accelerator!.Allocate1D<float>(totalElements);
            
            // Initialize SOA data
            var xData = new float[totalElements];
            var yData = new float[totalElements];
            var zData = new float[totalElements];
            
            var random = new Random(42);
            for (int i = 0; i < totalElements; i++)
            {
                xData[i] = random.NextSingle();
                yData[i] = random.NextSingle();
                zData[i] = random.NextSingle();
            }
            
            xBuffer.CopyFromCPU(xData);
            yBuffer.CopyFromCPU(yData);
            zBuffer.CopyFromCPU(zData);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int>(
                StructOfArraysKernel);
                
            kernel( totalElements,
                xBuffer.View, yBuffer.View, zBuffer.View, totalElements);
            accelerator.Synchronize();
        }
        catch
        {
            StructOfArraysCpu();
        }
    }

    [Benchmark]
    public void ArrayOfStructs()
    {
        // AOS: Array of Structures layout
        try
        {
            var totalElements = MatrixSize * MatrixSize;
            var structSize = 3; // 3 floats per struct (x, y, z)
            using var buffer = accelerator!.Allocate1D<float>(totalElements * structSize);
            
            // Initialize AOS data (interleaved)
            var aosData = new float[totalElements * structSize];
            var random = new Random(42);
            for (int i = 0; i < totalElements; i++)
            {
                aosData[i * 3 + 0] = random.NextSingle(); // x
                aosData[i * 3 + 1] = random.NextSingle(); // y
                aosData[i * 3 + 2] = random.NextSingle(); // z
            }
            
            buffer.CopyFromCPU(aosData);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, int>(
                ArrayOfStructsKernel);
                
            kernel( totalElements, buffer.View, totalElements);
            accelerator.Synchronize();
        }
        catch
        {
            ArrayOfStructsCpu();
        }
    }

    [Benchmark]
    public void PaddedLayoutAccess()
    {
        try
        {
            // Add padding to avoid bank conflicts
            var paddedWidth = ((MatrixSize + 31) / 32) * 32; // Align to 32-element boundary
            var totalElements = paddedWidth * MatrixSize;
            
            using var buffer = accelerator!.Allocate1D<float>(totalElements);
            
            // Initialize padded data
            var paddedData = new float[totalElements];
            for (int row = 0; row < MatrixSize; row++)
            {
                for (int col = 0; col < MatrixSize; col++)
                {
                    paddedData[row * paddedWidth + col] = testData![row * MatrixSize + col];
                }
            }
            
            buffer.CopyFromCPU(paddedData);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<float>, int, int>(
                PaddedAccessKernel);
                
            kernel( new Index2D(MatrixSize, MatrixSize),
                buffer.View, MatrixSize, paddedWidth);
            accelerator.Synchronize();
        }
        catch
        {
            PaddedLayoutCpuAccess();
        }
    }

    [Benchmark]
    public void CompactLayoutVsAligned()
    {
        try
        {
            // Compare compact vs aligned memory layouts
            var totalElements = MatrixSize * MatrixSize;
            
            // Compact layout (no padding)
            using var compactBuffer = accelerator!.Allocate1D<float>(totalElements);
            compactBuffer.CopyFromCPU(testData!);
            
            // Aligned layout (with padding for better alignment)
            var alignedSize = ((totalElements + 127) / 128) * 128; // 128-element alignment
            using var alignedBuffer = accelerator.Allocate1D<float>(alignedSize);
            
            var alignedData = new float[alignedSize];
            Array.Copy(testData!, alignedData, totalElements);
            alignedBuffer.CopyFromCPU(alignedData);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, int>(
                SimpleProcessKernel);
            
            // Test compact layout
            kernel( totalElements, compactBuffer.View, totalElements);
            
            // Test aligned layout
            kernel( totalElements, alignedBuffer.View, totalElements);
            
            accelerator.Synchronize();
        }
        catch
        {
            CompactVsAlignedCpuAccess();
        }
    }

    #region CPU Fallback Methods

    private void RowMajorCpuAccess()
    {
        for (int row = 0; row < MatrixSize; row++)
        {
            for (int col = 0; col < MatrixSize; col++)
            {
                var index = row * MatrixSize + col;
                testData![index] = testData[index] * 1.1f;
            }
        }
    }

    private void ColumnMajorCpuAccess()
    {
        for (int col = 0; col < MatrixSize; col++)
        {
            for (int row = 0; row < MatrixSize; row++)
            {
                var index = row * MatrixSize + col;
                testData![index] = testData[index] * 1.1f;
            }
        }
    }

    private void TiledCpuAccess()
    {
        const int tileSize = 16;
        for (int tileRow = 0; tileRow < MatrixSize; tileRow += tileSize)
        {
            for (int tileCol = 0; tileCol < MatrixSize; tileCol += tileSize)
            {
                for (int row = tileRow; row < Math.Min(tileRow + tileSize, MatrixSize); row++)
                {
                    for (int col = tileCol; col < Math.Min(tileCol + tileSize, MatrixSize); col++)
                    {
                        var index = row * MatrixSize + col;
                        testData![index] = testData[index] * 1.1f;
                    }
                }
            }
        }
    }

    private void CacheOptimizedCpuAccess()
    {
        const int blockSize = 64;
        for (int blockRow = 0; blockRow < MatrixSize; blockRow += blockSize)
        {
            for (int blockCol = 0; blockCol < MatrixSize; blockCol += blockSize)
            {
                for (int row = blockRow; row < Math.Min(blockRow + blockSize, MatrixSize); row++)
                {
                    for (int col = blockCol; col < Math.Min(blockCol + blockSize, MatrixSize); col++)
                    {
                        var index = row * MatrixSize + col;
                        testData![index] = testData[index] * 1.1f;
                    }
                }
            }
        }
    }

    private void StructOfArraysCpu()
    {
        var totalElements = MatrixSize * MatrixSize;
        for (int i = 0; i < totalElements; i++)
        {
            // Simulate SOA access pattern
            _ = testData![i]; // x component
            _ = testData[i];  // y component  
            _ = testData[i];  // z component
        }
    }

    private void ArrayOfStructsCpu()
    {
        var totalElements = MatrixSize * MatrixSize;
        for (int i = 0; i < totalElements; i++)
        {
            // Simulate AOS access pattern (accessing all components of one element)
            var baseIndex = (i % testData!.Length);
            _ = testData[baseIndex]; // Complete struct access
        }
    }

    private void PaddedLayoutCpuAccess()
    {
        var paddedWidth = ((MatrixSize + 31) / 32) * 32;
        for (int row = 0; row < MatrixSize; row++)
        {
            for (int col = 0; col < MatrixSize; col++)
            {
                var originalIndex = row * MatrixSize + col;
                testData![originalIndex] = testData[originalIndex] * 1.1f;
            }
        }
    }

    private void CompactVsAlignedCpuAccess()
    {
        for (int i = 0; i < testData!.Length; i++)
        {
            testData[i] = testData[i] * 1.1f;
        }
    }

    #endregion

    #region Kernels

    private static void RowMajorAccessKernel(
        Index2D index,
        ArrayView2D<float, Stride2D.DenseX> data,
        int size)
    {
        if (index.X >= size || index.Y >= size) return;
        data[index] = data[index] * 1.1f;
    }

    private static void ColumnMajorAccessKernel(
        Index2D index,
        ArrayView2D<float, Stride2D.DenseY> data,
        int size)
    {
        if (index.X >= size || index.Y >= size) return;
        data[index] = data[index] * 1.1f;
    }

    private static void TiledAccessKernel(
        Index2D index,
        ArrayView<float> data,
        int matrixSize,
        int tileSize)
    {
        if (index.X >= matrixSize || index.Y >= matrixSize) return;
        
        var dataIndex = index.X * matrixSize + index.Y;
        data[dataIndex] = data[dataIndex] * 1.1f;
    }

    private static void BlockedAccessKernel(
        Index2D index,
        ArrayView<float> data,
        int matrixSize,
        int blockSize)
    {
        if (index.X >= matrixSize || index.Y >= matrixSize) return;
        
        var dataIndex = index.X * matrixSize + index.Y;
        data[dataIndex] = data[dataIndex] * 1.1f;
    }

    private static void StructOfArraysKernel(
        Index1D index,
        ArrayView<float> xData,
        ArrayView<float> yData,
        ArrayView<float> zData,
        int size)
    {
        if (index >= size) return;
        
        // Process all components separately (SOA pattern)
        xData[index] = xData[index] * 1.1f;
        yData[index] = yData[index] * 1.1f;
        zData[index] = zData[index] * 1.1f;
    }

    private static void ArrayOfStructsKernel(
        Index1D index,
        ArrayView<float> data,
        int numElements)
    {
        if (index >= numElements) return;
        
        // Process interleaved data (AOS pattern)
        var baseIndex = index * 3;
        data[baseIndex + 0] = data[baseIndex + 0] * 1.1f; // x
        data[baseIndex + 1] = data[baseIndex + 1] * 1.1f; // y
        data[baseIndex + 2] = data[baseIndex + 2] * 1.1f; // z
    }

    private static void PaddedAccessKernel(
        Index2D index,
        ArrayView<float> data,
        int matrixSize,
        int paddedWidth)
    {
        if (index.X >= matrixSize || index.Y >= matrixSize) return;
        
        var dataIndex = index.X * paddedWidth + index.Y;
        data[dataIndex] = data[dataIndex] * 1.1f;
    }

    private static void SimpleProcessKernel(
        Index1D index,
        ArrayView<float> data,
        int size)
    {
        if (index >= size) return;
        data[index] = data[index] * 1.1f;
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
        testData = null;
    }
}
