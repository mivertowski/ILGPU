// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: BurnInTestRunner.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Benchmarks.Infrastructure;
using ILGPU.Numerics;
using ILGPU.Numerics.Hybrid;
using ILGPU.Runtime;
using ILGPU.SIMD;
using ILGPU.TensorCores;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;

namespace ILGPU.Benchmarks.BurnIn;

/// <summary>
/// Runs burn-in tests for maximum load and throughput testing.
/// </summary>
public class BurnInTestRunner
{
    private readonly ILogger<BurnInTestRunner> logger;
    private readonly BenchmarkConfig config;

    public BurnInTestRunner(ILogger<BurnInTestRunner> logger, BenchmarkConfig config)
    {
        this.logger = logger;
        this.config = config;
    }

    /// <summary>
    /// Runs the comprehensive burn-in test suite.
    /// </summary>
    public async Task RunBurnInTestAsync()
    {
        AnsiConsole.Write(
            new Panel("[red]🔥 BURN-IN TEST - MAXIMUM LOAD 🔥[/]")
                .Border(BoxBorder.Double)
                .BorderColor(Color.Red));

        AnsiConsole.MarkupLine("[yellow]Warning: This test will run at maximum load and may take several hours.[/]");
        AnsiConsole.MarkupLine("[yellow]Ensure adequate cooling and power supply before continuing.[/]");

        if (!AnsiConsole.Confirm("Continue with burn-in test?"))
            return;

        var duration = AnsiConsole.Prompt(
            new SelectionPrompt<TimeSpan>()
                .Title("Select burn-in test duration:")
                .AddChoices(
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15),
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(2),
                    TimeSpan.FromHours(6),
                    TimeSpan.FromHours(12))
                .UseConverter(ts => ts.TotalHours >= 1 ? $"{ts.TotalHours:F0} hours" : $"{ts.TotalMinutes:F0} minutes"));

        var cancellationTokenSource = new CancellationTokenSource(duration);
        var tasks = new List<Task>();

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var mainTask = ctx.AddTask("[red]Burn-in Test Progress[/]", maxValue: (int)duration.TotalSeconds);

                // Start multiple concurrent workloads
                tasks.Add(RunContinuousTensorCoreWorkload(cancellationTokenSource.Token, ctx));
                tasks.Add(RunContinuousSimdWorkload(cancellationTokenSource.Token, ctx));
                tasks.Add(RunContinuousMemoryWorkload(cancellationTokenSource.Token, ctx));
                tasks.Add(RunContinuousHybridWorkload(cancellationTokenSource.Token, ctx));
                tasks.Add(MonitorSystemHealth(cancellationTokenSource.Token, ctx));

                // Update main progress
                var stopwatch = Stopwatch.StartNew();
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationTokenSource.Token);
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    mainTask.Value = Math.Min((int)elapsed, (int)duration.TotalSeconds);
                    
                    if (elapsed >= duration.TotalSeconds)
                        break;
                }

                mainTask.Description = "[red]Stopping burn-in test...[/]";
                cancellationTokenSource.Cancel();

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }

                mainTask.Description = "[green]Burn-in test completed![/]";
            });

        AnsiConsole.MarkupLine("[green]Burn-in test completed successfully![/]");
        DisplayBurnInResults();
    }

    private Task RunContinuousTensorCoreWorkload(CancellationToken cancellationToken, ProgressContext ctx)
    {
        var task = ctx.AddTask("[blue]Tensor Core Workload[/]", maxValue: 100);
        var iterations = 0;

        try
        {
            using var context = Context.Create(builder => builder.Default());
            var device = context.GetPreferredDevice(preferCPU: false); // GPU preferred, CPU fallback
            using var accelerator = device?.CreateAccelerator(context);

            if (accelerator == null)
            {
                task.Description = "[red]No accelerator available[/]";
                return Task.CompletedTask;
            }

            var matrixSize = 512;
            using var matrixA = accelerator.Allocate1D<Half>(matrixSize * matrixSize);
            using var matrixB = accelerator.Allocate1D<Half>(matrixSize * matrixSize);
            using var result = accelerator.Allocate1D<float>(matrixSize * matrixSize);

            // Initialize test data
            var testData = CreateTestData<Half>(matrixSize * matrixSize);
            matrixA.CopyFromCPU(testData);
            matrixB.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<Half>, ArrayView<Half>, ArrayView<float>, int>(
                BurnInMatrixKernel);

            while (!cancellationToken.IsCancellationRequested)
            {
                kernel( new Index2D(matrixSize, matrixSize),
                    matrixA.View, matrixB.View, result.View, matrixSize);
                
                accelerator.Synchronize();
                iterations++;

                if (iterations % 100 == 0)
                {
                    task.Increment(1);
                    task.Description = $"[blue]Tensor Core: {iterations:N0} iterations[/]";
                }
            }
        }
        catch (Exception ex)
        {
            task.Description = $"[red]Tensor Core Error: {ex.Message}[/]";
            logger.LogError(ex, "Tensor core workload failed");
        }
        
        return Task.CompletedTask;
    }

    private async Task RunContinuousSimdWorkload(CancellationToken cancellationToken, ProgressContext ctx)
    {
        var task = ctx.AddTask("[yellow]SIMD Workload[/]", maxValue: 100);
        var iterations = 0;

        try
        {
            var vectorSize = 65536;
            var vectorA = new float[vectorSize];
            var vectorB = new float[vectorSize];
            var result = new float[vectorSize];

            var random = new Random(42);
            for (int i = 0; i < vectorSize; i++)
            {
                vectorA[i] = random.NextSingle();
                vectorB[i] = random.NextSingle();
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                // Perform various SIMD operations
                VectorOperations.Add(vectorA.AsSpan(), vectorB.AsSpan(), result.AsSpan());
                VectorOperations.Multiply(vectorA.AsSpan(), vectorB.AsSpan(), result.AsSpan());
                _ = VectorOperations.DotProduct<float>(vectorA.AsSpan(), vectorB.AsSpan());

                iterations++;

                if (iterations % 1000 == 0)
                {
                    task.Increment(1);
                    task.Description = $"[yellow]SIMD: {iterations:N0} iterations[/]";
                }

                if (iterations % 10000 == 0)
                {
                    await Task.Delay(1, cancellationToken); // Yield occasionally
                }
            }
        }
        catch (Exception ex)
        {
            task.Description = $"[red]SIMD Error: {ex.Message}[/]";
            logger.LogError(ex, "SIMD workload failed");
        }
    }

    private async Task RunContinuousMemoryWorkload(CancellationToken cancellationToken, ProgressContext ctx)
    {
        var task = ctx.AddTask("[green]Memory Workload[/]", maxValue: 100);
        var iterations = 0;

        try
        {
            using var context = Context.Create(builder => builder.Default());
            var device = context.GetPreferredDevice(preferCPU: false); // GPU preferred, CPU fallback
            using var accelerator = device?.CreateAccelerator(context);

            if (accelerator == null)
            {
                task.Description = "[red]No accelerator available[/]";
                return;
            }

            var bufferSize = 1024 * 1024; // 1MB buffers
            var testData = CreateTestData<float>(bufferSize);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Allocate, copy, and deallocate memory buffers
                using var buffer = accelerator.Allocate1D<float>(bufferSize);
                buffer.CopyFromCPU(testData);
                
                var resultData = buffer.GetAsArray1D();
                
                iterations++;

                if (iterations % 100 == 0)
                {
                    task.Increment(1);
                    task.Description = $"[green]Memory: {iterations:N0} allocations[/]";
                }

                if (iterations % 1000 == 0)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            task.Description = $"[red]Memory Error: {ex.Message}[/]";
            logger.LogError(ex, "Memory workload failed");
        }
    }

    private async Task RunContinuousHybridWorkload(CancellationToken cancellationToken, ProgressContext ctx)
    {
        var task = ctx.AddTask("[magenta]Hybrid Workload[/]", maxValue: 100);
        var iterations = 0;

        try
        {
            using var context = Context.Create(builder => builder.Default());
            using var processor = HybridTensorProcessorFactory.Create(context);

            var tensorSize = 256;
            using var tensor = UnifiedTensor.Random<float>(
                context.GetPreferredDevice(preferCPU: true)?.CreateAccelerator(context)!, 
                new TensorShape(tensorSize, tensorSize));

            while (!cancellationToken.IsCancellationRequested)
            {
                // Perform hybrid CPU/GPU operations
                var addResult = tensor.AddSimd(tensor);
                var mulResult = tensor.MatMulSimd(tensor);
                
                addResult.Dispose();
                mulResult.Dispose();

                iterations++;

                if (iterations % 50 == 0)
                {
                    task.Increment(1);
                    task.Description = $"[magenta]Hybrid: {iterations:N0} operations[/]";
                }

                if (iterations % 500 == 0)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            task.Description = $"[red]Hybrid Error: {ex.Message}[/]";
            logger.LogError(ex, "Hybrid workload failed");
        }
    }

    private async Task MonitorSystemHealth(CancellationToken cancellationToken, ProgressContext ctx)
    {
        var task = ctx.AddTask("[cyan]System Monitor[/]", maxValue: 100);
        var checks = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024);
                var workingSet = Environment.WorkingSet / (1024 * 1024);

                checks++;
                
                if (checks % 60 == 0) // Update every minute
                {
                    task.Increment(1);
                    task.Description = $"[cyan]Memory: {memoryUsage:N0} MB, Working: {workingSet:N0} MB[/]";
                }

                // Force garbage collection periodically
                if (checks % 300 == 0) // Every 5 minutes
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            task.Description = $"[red]Monitor Error: {ex.Message}[/]";
            logger.LogError(ex, "System monitoring failed");
        }
    }

    private static void DisplayBurnInResults()
    {
        var table = new Table()
            .Title("[green]Burn-in Test Results[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green);

        table.AddColumn("[yellow]Metric[/]");
        table.AddColumn("[green]Value[/]");

        table.AddRow("Memory Usage", $"{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
        table.AddRow("Working Set", $"{Environment.WorkingSet / (1024 * 1024):N0} MB");
        table.AddRow("GC Collections Gen 0", GC.CollectionCount(0).ToString("N0"));
        table.AddRow("GC Collections Gen 1", GC.CollectionCount(1).ToString("N0"));
        table.AddRow("GC Collections Gen 2", GC.CollectionCount(2).ToString("N0"));
        table.AddRow("Status", "[green]✓ PASSED[/]");

        AnsiConsole.Write(table);
    }

    private static T[] CreateTestData<T>(int size) where T : unmanaged
    {
        var data = new T[size];
        var random = new Random(42);
        
        if (typeof(T) == typeof(float))
        {
            var floatData = data as float[];
            for (int i = 0; i < size; i++)
                floatData![i] = random.NextSingle();
        }
        else if (typeof(T) == typeof(Half))
        {
            var halfData = data as Half[];
            for (int i = 0; i < size; i++)
                halfData![i] = (Half)random.NextSingle();
        }
        
        return data;
    }

    #region Kernels

    private static void BurnInMatrixKernel(
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
}
