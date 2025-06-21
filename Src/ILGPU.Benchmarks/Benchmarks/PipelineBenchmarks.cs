// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: PipelineBenchmarks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using ILGPU.Runtime;
using System.Threading.Channels;

namespace ILGPU.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for pipeline processing patterns and async operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class PipelineBenchmarks : IDisposable
{
    private Context? context;
    private Accelerator? accelerator;
    private float[][]? testData;

    [Params(16, 64, 256)]
    public int BatchSize { get; set; }

    [Params(4, 8, 16)]
    public int PipelineStages { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        context = Context.Create(builder => builder.Cuda().CPU());
        accelerator = context.GetPreferredDevice(AcceleratorType.Cuda) ??
                     context.GetPreferredDevice(AcceleratorType.CPU);

        // Create test data batches
        testData = new float[BatchSize][];
        var random = new Random(42);
        
        for (int i = 0; i < BatchSize; i++)
        {
            testData[i] = new float[1024];
            for (int j = 0; j < 1024; j++)
            {
                testData[i][j] = random.NextSingle();
            }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task SequentialProcessing()
    {
        var results = new float[BatchSize][];
        
        for (int i = 0; i < BatchSize; i++)
        {
            results[i] = await ProcessDataAsync(testData![i]);
        }
    }

    [Benchmark]
    public async Task ParallelProcessing()
    {
        var tasks = new Task<float[]>[BatchSize];
        
        for (int i = 0; i < BatchSize; i++)
        {
            tasks[i] = ProcessDataAsync(testData![i]);
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task PipelineProcessing()
    {
        var channel = Channel.CreateBounded<float[]>(PipelineStages);
        var writer = channel.Writer;
        var reader = channel.Reader;
        
        // Producer task
        var producerTask = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < BatchSize; i++)
                {
                    await writer.WriteAsync(testData![i]);
                }
            }
            finally
            {
                writer.Complete();
            }
        });
        
        // Consumer tasks (pipeline stages)
        var consumerTasks = new List<Task>();
        for (int stage = 0; stage < PipelineStages; stage++)
        {
            consumerTasks.Add(Task.Run(async () =>
            {
                await foreach (var data in reader.ReadAllAsync())
                {
                    await ProcessDataAsync(data);
                }
            }));
        }
        
        await Task.WhenAll(producerTask);
        await Task.WhenAll(consumerTasks);
    }

    [Benchmark]
    public async Task StreamingPipeline()
    {
        var semaphore = new SemaphoreSlim(PipelineStages, PipelineStages);
        var tasks = new List<Task>();
        
        for (int i = 0; i < BatchSize; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await ProcessDataAsync(testData![i]);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task BatchedProcessing()
    {
        const int batchSize = 4;
        var batches = new List<List<float[]>>();
        
        for (int i = 0; i < BatchSize; i += batchSize)
        {
            var batch = new List<float[]>();
            for (int j = 0; j < batchSize && (i + j) < BatchSize; j++)
            {
                batch.Add(testData![i + j]);
            }
            batches.Add(batch);
        }
        
        foreach (var batch in batches)
        {
            var batchTasks = batch.Select(ProcessDataAsync);
            await Task.WhenAll(batchTasks);
        }
    }

    [Benchmark]
    public async Task AsynchronousMemoryTransfer()
    {
        if (accelerator == null) return;
        
        var tasks = new List<Task>();
        
        for (int i = 0; i < BatchSize; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var buffer = accelerator.Allocate1D<float>(testData![i].Length);
                
                // Asynchronous upload
                buffer.CopyFromCPU(accelerator.DefaultStream, testData[i]);
                
                // Simulate processing
                await Task.Delay(1);
                
                // Asynchronous download
                var result = new float[testData[i].Length];
                buffer.CopyToCPU(accelerator.DefaultStream, result);
                
                await accelerator.DefaultStream.SynchronizeAsync();
            }));
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task OverlappedComputeTransfer()
    {
        if (accelerator == null) return;
        
        using var computeStream = accelerator.CreateStream();
        using var transferStream = accelerator.CreateStream();
        
        var tasks = new List<Task>();
        
        for (int i = 0; i < BatchSize; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var inputBuffer = accelerator.Allocate1D<float>(testData![i].Length);
                using var outputBuffer = accelerator.Allocate1D<float>(testData[i].Length);
                
                // Upload on transfer stream
                inputBuffer.CopyFromCPU(transferStream, testData[i]);
                
                // Ensure transfer completes before compute
                await transferStream.SynchronizeAsync();
                
                // Compute on compute stream
                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<float>, ArrayView<float>>(
                    ProcessKernel);
                
                kernel(computeStream, testData[i].Length,
                    inputBuffer.View, outputBuffer.View);
                
                // Overlapped synchronization
                await Task.WhenAll(
                    computeStream.SynchronizeAsync(),
                    transferStream.SynchronizeAsync());
            }));
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task ProducerConsumerPipeline()
    {
        var channel = Channel.CreateBounded<(float[] data, int id)>(PipelineStages * 2);
        var writer = channel.Writer;
        var reader = channel.Reader;
        
        // Producer
        var producerTask = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < BatchSize; i++)
                {
                    await writer.WriteAsync((testData![i], i));
                }
            }
            finally
            {
                writer.Complete();
            }
        });
        
        // Multiple consumers
        var consumerTasks = new List<Task>();
        for (int c = 0; c < Math.Min(PipelineStages, Environment.ProcessorCount); c++)
        {
            consumerTasks.Add(Task.Run(async () =>
            {
                await foreach (var (data, id) in reader.ReadAllAsync())
                {
                    await ProcessDataAsync(data);
                }
            }));
        }
        
        await Task.WhenAll(new[] { producerTask }.Concat(consumerTasks));
    }

    private async Task<float[]> ProcessDataAsync(float[] input)
    {
        if (accelerator == null)
        {
            // Fallback CPU processing
            var result = new float[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[i] * 2.0f + 1.0f;
            }
            await Task.Delay(1); // Simulate async work
            return result;
        }
        
        try
        {
            using var inputBuffer = accelerator.Allocate1D<float>(input.Length);
            using var outputBuffer = accelerator.Allocate1D<float>(input.Length);
            
            inputBuffer.CopyFromCPU(input);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>>(
                ProcessKernel);
            
            kernel(accelerator.DefaultStream, input.Length,
                inputBuffer.View, outputBuffer.View);
            
            await accelerator.DefaultStream.SynchronizeAsync();
            
            return outputBuffer.GetAsArray1D();
        }
        catch
        {
            // Fallback to CPU processing
            var result = new float[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[i] * 2.0f + 1.0f;
            }
            return result;
        }
    }

    private static void ProcessKernel(
        Index1D index,
        ArrayView<float> input,
        ArrayView<float> output)
    {
        output[index] = input[index] * 2.0f + 1.0f;
    }

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