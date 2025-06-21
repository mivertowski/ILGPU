// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: BenchmarkRunner.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using ILGPU.Benchmarks.Benchmarks;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace ILGPU.Benchmarks.Infrastructure;

/// <summary>
/// Orchestrates the execution of different benchmark suites.
/// </summary>
public class BenchmarkRunner
{
    private readonly ILogger<BenchmarkRunner> logger;
    private readonly BenchmarkConfig config;

    public BenchmarkRunner(ILogger<BenchmarkRunner> logger, BenchmarkConfig config)
    {
        this.logger = logger;
        this.config = config;
    }

    /// <summary>
    /// Runs a quick performance suite for basic validation.
    /// </summary>
    public async Task RunQuickSuiteAsync()
    {
        AnsiConsole.Write(
            new Panel("[cyan1]Quick Performance Suite[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green));

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Running quick benchmarks...[/]", maxValue: 100);

                // Basic SIMD operations
                task.Description = "[green]SIMD Vector Operations[/]";
                BenchmarkRunner.Run<SimdVectorBenchmarks>(config.QuickConfig);
                task.Increment(25);

                // Basic tensor operations
                task.Description = "[green]Basic Tensor Operations[/]";
                BenchmarkRunner.Run<TensorCoreBenchmarks>(config.QuickConfig);
                task.Increment(25);

                // Memory operations
                task.Description = "[green]Memory Operations[/]";
                BenchmarkRunner.Run<MemoryBenchmarks>(config.QuickConfig);
                task.Increment(25);

                // Hybrid processing
                task.Description = "[green]Hybrid Processing[/]";
                BenchmarkRunner.Run<HybridProcessingBenchmarks>(config.QuickConfig);
                task.Increment(25);

                task.Description = "[green]Quick suite completed![/]";
                await Task.Delay(500); // Brief pause to show completion
            });
    }

    /// <summary>
    /// Runs comprehensive tensor core benchmarks.
    /// </summary>
    public async Task RunTensorCoreBenchmarksAsync()
    {
        AnsiConsole.Write(
            new Panel("[cyan1]Tensor Core Performance Benchmarks[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Blue));

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[blue]Running tensor core benchmarks...[/]", maxValue: 100);

                task.Description = "[blue]Matrix Multiply-Accumulate (MMA)[/]";
                BenchmarkRunner.Run<TensorCoreBenchmarks>(config.StandardConfig);
                task.Increment(33);

                task.Description = "[blue]Mixed Precision Operations[/]";
                BenchmarkRunner.Run<MixedPrecisionBenchmarks>(config.StandardConfig);
                task.Increment(33);

                task.Description = "[blue]BFloat16 Operations[/]";
                BenchmarkRunner.Run<BFloat16Benchmarks>(config.StandardConfig);
                task.Increment(34);

                task.Description = "[blue]Tensor core benchmarks completed![/]";
                await Task.Delay(500);
            });
    }

    /// <summary>
    /// Runs SIMD performance benchmarks.
    /// </summary>
    public async Task RunSimdBenchmarksAsync()
    {
        AnsiConsole.Write(
            new Panel("[cyan1]SIMD Performance Benchmarks[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Yellow));

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[yellow]Running SIMD benchmarks...[/]", maxValue: 100);

                task.Description = "[yellow]Vector Operations (Add, Multiply, Dot Product)[/]";
                BenchmarkRunner.Run<SimdVectorBenchmarks>(config.StandardConfig);
                task.Increment(25);

                task.Description = "[yellow]Platform-Specific Intrinsics (AVX, SSE, NEON)[/]";
                BenchmarkRunner.Run<PlatformIntrinsicsBenchmarks>(config.StandardConfig);
                task.Increment(25);

                task.Description = "[yellow]Matrix-Vector Operations[/]";
                BenchmarkRunner.Run<MatrixVectorBenchmarks>(config.StandardConfig);
                task.Increment(25);

                task.Description = "[yellow]CPU vs GPU Vectorization[/]";
                BenchmarkRunner.Run<CpuGpuComparisonBenchmarks>(config.StandardConfig);
                task.Increment(25);

                task.Description = "[yellow]SIMD benchmarks completed![/]";
                await Task.Delay(500);
            });
    }

    /// <summary>
    /// Runs hybrid processing benchmarks.
    /// </summary>
    public async Task RunHybridBenchmarksAsync()
    {
        AnsiConsole.Write(
            new Panel("[cyan1]Hybrid Processing Benchmarks[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Magenta));

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[magenta]Running hybrid benchmarks...[/]", maxValue: 100);

                task.Description = "[magenta]Workload Distribution Strategies[/]";
                BenchmarkRunner.Run<HybridProcessingBenchmarks>(config.StandardConfig);
                task.Increment(50);

                task.Description = "[magenta]CPU/GPU Pipeline Performance[/]";
                BenchmarkRunner.Run<PipelineBenchmarks>(config.StandardConfig);
                task.Increment(50);

                task.Description = "[magenta]Hybrid benchmarks completed![/]";
                await Task.Delay(500);
            });
    }

    /// <summary>
    /// Runs memory operation benchmarks.
    /// </summary>
    public async Task RunMemoryBenchmarksAsync()
    {
        AnsiConsole.Write(
            new Panel("[cyan1]Memory Operations Benchmarks[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Orange1));

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[orange1]Running memory benchmarks...[/]", maxValue: 100);

                task.Description = "[orange1]Zero-Copy Operations[/]";
                BenchmarkRunner.Run<MemoryBenchmarks>(config.StandardConfig);
                task.Increment(33);

                task.Description = "[orange1]Memory Layout Optimization[/]";
                BenchmarkRunner.Run<MemoryLayoutBenchmarks>(config.StandardConfig);
                task.Increment(33);

                task.Description = "[orange1]Unified Memory Performance[/]";
                BenchmarkRunner.Run<UnifiedMemoryBenchmarks>(config.StandardConfig);
                task.Increment(34);

                task.Description = "[orange1]Memory benchmarks completed![/]";
                await Task.Delay(500);
            });
    }

    /// <summary>
    /// Runs the comprehensive benchmark suite.
    /// </summary>
    public async Task RunComprehensiveSuiteAsync()
    {
        AnsiConsole.Write(
            new Panel("[cyan1]Comprehensive Benchmark Suite[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Red));

        AnsiConsole.MarkupLine("[yellow]Warning: This will run all benchmarks and may take several hours.[/]");
        
        if (!AnsiConsole.Confirm("Continue with comprehensive benchmarks?"))
            return;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var mainTask = ctx.AddTask("[red]Comprehensive Benchmarks[/]", maxValue: 500);

                // Run all benchmark suites
                mainTask.Description = "[red]SIMD Benchmarks[/]";
                await RunSimdBenchmarksAsync();
                mainTask.Increment(100);

                mainTask.Description = "[red]Tensor Core Benchmarks[/]";
                await RunTensorCoreBenchmarksAsync();
                mainTask.Increment(100);

                mainTask.Description = "[red]Hybrid Processing Benchmarks[/]";
                await RunHybridBenchmarksAsync();
                mainTask.Increment(100);

                mainTask.Description = "[red]Memory Benchmarks[/]";
                await RunMemoryBenchmarksAsync();
                mainTask.Increment(100);

                mainTask.Description = "[red]Performance Scaling Tests[/]";
                BenchmarkRunner.Run<ScalabilityBenchmarks>(config.ComprehensiveConfig);
                mainTask.Increment(100);

                mainTask.Description = "[red]All benchmarks completed![/]";
                await Task.Delay(1000);
            });

        AnsiConsole.MarkupLine("[green]Comprehensive benchmark suite completed! Check BenchmarkDotNet results for detailed analysis.[/]");
    }
}