// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: BenchmarkConfig.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace ILGPU.Benchmarks.Infrastructure;

/// <summary>
/// Configuration for different types of benchmarks.
/// </summary>
public class BenchmarkConfig
{
    /// <summary>
    /// Quick configuration for fast validation benchmarks.
    /// </summary>
    public IConfig QuickConfig { get; }

    /// <summary>
    /// Standard configuration for regular benchmarks.
    /// </summary>
    public IConfig StandardConfig { get; }

    /// <summary>
    /// Comprehensive configuration for detailed analysis.
    /// </summary>
    public IConfig ComprehensiveConfig { get; }

    /// <summary>
    /// Burn-in test configuration for maximum load testing.
    /// </summary>
    public IConfig BurnInConfig { get; }

    public BenchmarkConfig()
    {
        QuickConfig = CreateQuickConfig();
        StandardConfig = CreateStandardConfig();
        ComprehensiveConfig = CreateComprehensiveConfig();
        BurnInConfig = CreateBurnInConfig();
    }

    private static IConfig CreateQuickConfig()
    {
        return ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.ShortRun
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithInvocationCount(1)
                .WithUnrollFactor(1))
            .AddExporter(HtmlExporter.Default)
            .AddExporter(CsvExporter.Default)
            .AddLogger(ConsoleLogger.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest))
            .WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend));
    }

    private static IConfig CreateStandardConfig()
    {
        return ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.Default
                .WithWarmupCount(3)
                .WithIterationCount(5)
                .WithInvocationCount(1)
                .WithUnrollFactor(16))
            .AddExporter(HtmlExporter.Default)
            .AddExporter(CsvExporter.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddLogger(ConsoleLogger.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddDiagnoser(ThreadingDiagnoser.Default)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest))
            .WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend));
    }

    private static IConfig CreateComprehensiveConfig()
    {
        return ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.LongRun
                .WithWarmupCount(5)
                .WithIterationCount(10)
                .WithInvocationCount(1)
                .WithUnrollFactor(16))
            .AddExporter(HtmlExporter.Default)
            .AddExporter(CsvExporter.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(JsonExporter.Default)
            .AddLogger(ConsoleLogger.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddDiagnoser(ThreadingDiagnoser.Default)
            .AddDiagnoser(HardwareCounters.BranchMispredictions)
            .AddDiagnoser(HardwareCounters.CacheMisses)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest))
            .WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend));
    }

    private static IConfig CreateBurnInConfig()
    {
        return ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(1)
                .WithIterationCount(1)
                .WithInvocationCount(1000000) // High iteration count for burn-in
                .WithUnrollFactor(1))
            .AddExporter(ConsoleExporter.Default)
            .AddLogger(ConsoleLogger.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddDiagnoser(ThreadingDiagnoser.Default)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Method))
            .WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Percentage));
    }
}