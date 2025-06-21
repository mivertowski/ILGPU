// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: MemoryBenchmarks.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Runtime;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for memory operations and zero-copy scenarios.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class MemoryBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? accelerator;
    private float[]? testData;

    [Params(1024, 16384, 262144, 4194304)] // 4KB to 16MB
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        context = Context.CreateDefault();
        var device = context.GetPreferredDevice(preferCPU: false); // GPU preferred, CPU fallback
        accelerator = device?.CreateAccelerator(context);

        testData = new float[BufferSize];
        var random = new Random(42);
        for (int i = 0; i < BufferSize; i++)
        {
            testData[i] = random.NextSingle();
        }
    }

    [Benchmark(Baseline = true)]
    public void StandardMemoryAllocation()
    {
        using var buffer = accelerator!.Allocate1D<float>(BufferSize);
        buffer.CopyFromCPU(testData!);
        var result = buffer.GetAsArray1D();
    }

    [Benchmark]
    public void PageLockedMemoryTransfer()
    {
        try
        {
            using var pageLockedScope = accelerator!.CreatePageLockFromPinned(testData!);
            using var buffer = accelerator.Allocate1D<float>(BufferSize);
            
            buffer.CopyFromCPU(testData!);
            var result = buffer.GetAsArray1D();
        }
        catch
        {
            // Fallback to standard allocation
            StandardMemoryAllocation();
        }
    }

    [Benchmark]
    public async Task AsynchronousMemoryTransfer()
    {
        using var buffer = accelerator!.Allocate1D<float>(BufferSize);
        using var stream = accelerator.CreateStream();
        
        buffer.CopyFromCPU(stream, testData!);
        await stream.SynchronizeAsync();
        
        var result = new float[BufferSize];
        buffer.CopyToCPU(stream, result);
        await stream.SynchronizeAsync();
    }

    [Benchmark]
    public void MemoryPoolAllocation()
    {
        // Simulate memory pool behavior by allocating/deallocating multiple buffers
        var buffers = new List<MemoryBuffer1D<float, Stride1D.Dense>>();
        
        try
        {
            for (int i = 0; i < 10; i++)
            {
                buffers.Add(accelerator!.Allocate1D<float>(BufferSize / 10));
            }
            
            // Transfer data to all buffers
            var chunkSize = BufferSize / 10;
            for (int i = 0; i < buffers.Count; i++)
            {
                var chunk = new float[chunkSize];
                Array.Copy(testData!, i * chunkSize, chunk, 0, chunkSize);
                buffers[i].CopyFromCPU(chunk);
            }
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer.Dispose();
            }
        }
    }

    [Benchmark]
    public void ZeroCopyOperations()
    {
        try
        {
            // Simulate zero-copy by using views and avoiding unnecessary copies
            using var buffer = accelerator!.Allocate1D<float>(BufferSize);
            buffer.CopyFromCPU(testData!);
            
            // Create views for different operations
            var view1 = buffer.View.SubView(0, BufferSize / 2);
            var view2 = buffer.View.SubView(BufferSize / 2, BufferSize / 2);
            
            // Process views in-place
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>>(InPlaceProcessKernel);
                
            kernel( BufferSize / 2, view1);
            kernel( BufferSize / 2, view2);
            
            accelerator.Synchronize();
        }
        catch
        {
            StandardMemoryAllocation();
        }
    }

    [Benchmark]
    public void MemoryCoalescing()
    {
        try
        {
            using var buffer = accelerator!.Allocate1D<float>(BufferSize);
            buffer.CopyFromCPU(testData!);
            
            // Test coalesced vs non-coalesced memory access patterns
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, int>(CoalescedAccessKernel);
                
            kernel( BufferSize, buffer.View, BufferSize);
            accelerator.Synchronize();
        }
        catch
        {
            StandardMemoryAllocation();
        }
    }

    [Benchmark]
    public void StridePatternAccess()
    {
        try
        {
            using var buffer = accelerator!.Allocate1D<float>(BufferSize);
            buffer.CopyFromCPU(testData!);
            
            // Test strided access patterns
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, int, int>(StridedAccessKernel);
                
            kernel( BufferSize / 4, buffer.View, 4, BufferSize);
            accelerator.Synchronize();
        }
        catch
        {
            StandardMemoryAllocation();
        }
    }

    [Benchmark]
    public void MultiStreamMemoryOperations()
    {
        try
        {
            const int numStreams = 4;
            var streams = new AcceleratorStream[numStreams];
            var buffers = new MemoryBuffer1D<float, Stride1D.Dense>[numStreams];
            
            try
            {
                for (int i = 0; i < numStreams; i++)
                {
                    streams[i] = accelerator!.CreateStream();
                    buffers[i] = accelerator.Allocate1D<float>(BufferSize / numStreams);
                }
                
                // Concurrent memory operations
                var tasks = new Task[numStreams];
                for (int i = 0; i < numStreams; i++)
                {
                    int streamIndex = i;
                    tasks[i] = Task.Run(async () =>
                    {
                        var chunk = new float[BufferSize / numStreams];
                        Array.Copy(testData!, streamIndex * chunk.Length, chunk, 0, chunk.Length);
                        
                        buffers[streamIndex].CopyFromCPU(streams[streamIndex], chunk);
                        await streams[streamIndex].SynchronizeAsync();
                    });
                }
                
                Task.WaitAll(tasks);
            }
            finally
            {
                for (int i = 0; i < numStreams; i++)
                {
                    buffers[i]?.Dispose();
                    streams[i]?.Dispose();
                }
            }
        }
        catch
        {
            StandardMemoryAllocation();
        }
    }

    [Benchmark]
    public void MemoryBandwidthTest()
    {
        try
        {
            using var sourceBuffer = accelerator!.Allocate1D<float>(BufferSize);
            using var destBuffer = accelerator.Allocate1D<float>(BufferSize);
            
            sourceBuffer.CopyFromCPU(testData!);
            
            // Memory-to-memory copy
            destBuffer.CopyFrom(sourceBuffer);
            accelerator.Synchronize();
            
            // Read back to measure total bandwidth
            var result = destBuffer.GetAsArray1D();
        }
        catch
        {
            StandardMemoryAllocation();
        }
    }

    [Benchmark]
    public void MemoryPrefetching()
    {
        try
        {
            using var buffer = accelerator!.Allocate1D<float>(BufferSize);
            
            // Simulate prefetching by accessing data in predictable patterns
            var chunks = BufferSize / 1024;
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                var chunkData = new float[1024];
                Array.Copy(testData!, chunk * 1024, chunkData, 0, 1024);
                
                var view = buffer.View.SubView(chunk * 1024, 1024);
                view.CopyFromCPU(chunkData);
            }
            
            accelerator.Synchronize();
        }
        catch
        {
            StandardMemoryAllocation();
        }
    }

    #region Kernels

    private static void InPlaceProcessKernel(Index1D index, ArrayView<float> data)
    {
        data[index] = data[index] * 2.0f + 1.0f;
    }

    private static void CoalescedAccessKernel(
        Index1D index,
        ArrayView<float> data,
        int size)
    {
        if (index >= size) return;
        
        // Coalesced access pattern (consecutive threads access consecutive memory)
        data[index] = data[index] * 1.1f;
    }

    private static void StridedAccessKernel(
        Index1D index,
        ArrayView<float> data,
        int stride,
        int size)
    {
        int actualIndex = index * stride;
        if (actualIndex >= size) return;
        
        // Strided access pattern (may cause memory bank conflicts)
        data[actualIndex] = data[actualIndex] * 1.1f;
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
