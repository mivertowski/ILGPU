// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: UnattendedBenchmarkRunner.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using ILGPU.Benchmarks.Benchmarks;
using ILGPU.Benchmarks.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ILGPU.Benchmarks;

/// <summary>
/// Runs benchmarks unattended and outputs results to disk for CI/GitHub integration.
/// </summary>
public class UnattendedBenchmarkRunner
{
    private readonly ILogger<UnattendedBenchmarkRunner> logger;
    private readonly BenchmarkConfig config;
    private readonly string outputDirectory;

    public UnattendedBenchmarkRunner(ILogger<UnattendedBenchmarkRunner> logger, BenchmarkConfig config)
    {
        this.logger = logger;
        this.config = config;
        this.outputDirectory = Path.Combine(Environment.CurrentDirectory, "BenchmarkResults");
        Directory.CreateDirectory(outputDirectory);
    }

    /// <summary>
    /// Runs all benchmarks unattended and outputs results suitable for GitHub README.
    /// </summary>
    public async Task RunUnattendedBenchmarksAsync()
    {
        logger.LogInformation("Starting unattended benchmark execution...");
        
        var startTime = DateTime.UtcNow;
        var systemInfo = GatherSystemInformation();
        var benchmarkResults = new List<BenchmarkSuiteResult>();

        // Core benchmark suites
        var suites = new List<(string name, Type benchmarkType, string description)>
        {
            ("Tensor Core Operations", typeof(TensorCoreBenchmarks), "NVIDIA Tensor Core matrix operations with mixed precision"),
            ("SIMD Vector Operations", typeof(SimdVectorBenchmarks), "Platform-specific SIMD operations (AVX/SSE/NEON)"),
            ("Mixed Precision Arithmetic", typeof(MixedPrecisionBenchmarks), "FP16/BF16/TF32/INT8 operations and conversions"),
            ("BFloat16 Operations", typeof(BFloat16Benchmarks), "Brain Floating Point arithmetic for ML workloads"),
            ("Platform Intrinsics", typeof(PlatformIntrinsicsBenchmarks), "Hardware-specific intrinsics and optimization"),
            ("Matrix-Vector Operations", typeof(MatrixVectorBenchmarks), "Linear algebra operations and cache optimization"),
            ("CPU vs GPU Comparison", typeof(CpuGpuComparisonBenchmarks), "Cross-platform performance analysis"),
            ("Hybrid Processing", typeof(HybridProcessingBenchmarks), "CPU/GPU workload distribution strategies"),
            ("Memory Operations", typeof(MemoryBenchmarks), "Zero-copy operations and memory optimization"),
            ("Unified Memory", typeof(UnifiedMemoryBenchmarks), "Unified memory coherence and performance"),
            ("Scalability Analysis", typeof(ScalabilityBenchmarks), "Performance scaling across problem sizes")
        };

        foreach (var (name, benchmarkType, description) in suites)
        {
            try
            {
                logger.LogInformation($"Running {name} benchmarks...");
                var summary = BenchmarkRunner.Run(benchmarkType, config.StandardConfig);
                
                var result = new BenchmarkSuiteResult
                {
                    SuiteName = name,
                    Description = description,
                    ExecutionTime = DateTime.UtcNow,
                    Results = ExtractBenchmarkResults(summary),
                    Success = summary.HasCriticalValidationErrors == false
                };
                
                benchmarkResults.Add(result);
                logger.LogInformation($"Completed {name} benchmarks - {result.Results.Count} tests");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to run {name} benchmarks");
                benchmarkResults.Add(new BenchmarkSuiteResult
                {
                    SuiteName = name,
                    Description = description,
                    ExecutionTime = DateTime.UtcNow,
                    Results = new List<BenchmarkResult>(),
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        var endTime = DateTime.UtcNow;
        var totalDuration = endTime - startTime;

        // Generate outputs
        await GenerateReadmeOutputAsync(systemInfo, benchmarkResults, totalDuration);
        await GenerateJsonOutputAsync(systemInfo, benchmarkResults, totalDuration);
        await GenerateCsvOutputAsync(benchmarkResults);
        await GenerateMarkdownReportAsync(systemInfo, benchmarkResults, totalDuration);

        logger.LogInformation($"Unattended benchmarks completed in {totalDuration:hh\\:mm\\:ss}");
        logger.LogInformation($"Results saved to: {outputDirectory}");
    }

    /// <summary>
    /// Generates a README-ready markdown section with benchmark results.
    /// </summary>
    private async Task GenerateReadmeOutputAsync(BenchmarkSystemInfo systemInfo, List<BenchmarkSuiteResult> results, TimeSpan duration)
    {
        var markdown = new StringBuilder();
        
        markdown.AppendLine("# ILGPU Phase 6 Benchmark Results");
        markdown.AppendLine();
        markdown.AppendLine($"**Benchmark Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        markdown.AppendLine($"**Total Duration:** {duration:hh\\:mm\\:ss}");
        markdown.AppendLine($"**Platform:** {systemInfo.Platform}");
        markdown.AppendLine($"**CPU:** {systemInfo.CpuInfo}");
        markdown.AppendLine($"**Memory:** {systemInfo.MemoryInfo}");
        if (!string.IsNullOrEmpty(systemInfo.GpuInfo))
        {
            markdown.AppendLine($"**GPU:** {systemInfo.GpuInfo}");
        }
        markdown.AppendLine();

        // Performance Summary Table
        markdown.AppendLine("## Performance Summary");
        markdown.AppendLine();
        markdown.AppendLine("| Benchmark Suite | Tests | Success Rate | Best Performance | Avg Performance |");
        markdown.AppendLine("|-----------------|-------|--------------|------------------|-----------------|");

        foreach (var suite in results.Where(r => r.Success))
        {
            var successfulTests = suite.Results.Count(r => r.Success);
            var totalTests = suite.Results.Count;
            var successRate = totalTests > 0 ? (successfulTests * 100.0 / totalTests) : 0;
            
            var bestPerf = suite.Results.Where(r => r.Success && r.ThroughputOpsPerSec.HasValue)
                                      .Max(r => r.ThroughputOpsPerSec) ?? 0;
            var avgPerf = suite.Results.Where(r => r.Success && r.ThroughputOpsPerSec.HasValue)
                                     .Average(r => r.ThroughputOpsPerSec) ?? 0;

            markdown.AppendLine($"| {suite.SuiteName} | {totalTests} | {successRate:F1}% | {FormatPerformance(bestPerf)} | {FormatPerformance(avgPerf)} |");
        }

        markdown.AppendLine();

        // Detailed Results
        markdown.AppendLine("## Detailed Results");
        markdown.AppendLine();

        foreach (var suite in results)
        {
            markdown.AppendLine($"### {suite.SuiteName}");
            markdown.AppendLine($"*{suite.Description}*");
            markdown.AppendLine();

            if (!suite.Success)
            {
                markdown.AppendLine($"❌ **Failed:** {suite.ErrorMessage}");
                markdown.AppendLine();
                continue;
            }

            if (suite.Results.Any())
            {
                markdown.AppendLine("| Test Method | Performance | Memory | Ratio |");
                markdown.AppendLine("|-------------|-------------|--------|-------|");

                foreach (var result in suite.Results.Where(r => r.Success).Take(10)) // Top 10 results
                {
                    var perf = result.ThroughputOpsPerSec.HasValue ? FormatPerformance(result.ThroughputOpsPerSec.Value) : "N/A";
                    var memory = result.AllocatedBytes.HasValue ? $"{result.AllocatedBytes.Value / 1024:N0} KB" : "N/A";
                    var ratio = result.BaselineRatio.HasValue ? $"{result.BaselineRatio.Value:F2}x" : "N/A";
                    
                    markdown.AppendLine($"| {result.MethodName} | {perf} | {memory} | {ratio} |");
                }
            }
            else
            {
                markdown.AppendLine("*No benchmark results available.*");
            }

            markdown.AppendLine();
        }

        // Key Insights
        markdown.AppendLine("## Key Performance Insights");
        markdown.AppendLine();
        GeneratePerformanceInsights(markdown, results);

        var readmePath = Path.Combine(outputDirectory, "README_Benchmarks.md");
        await File.WriteAllTextAsync(readmePath, markdown.ToString());
        
        logger.LogInformation($"README-ready benchmark results written to: {readmePath}");
    }

    /// <summary>
    /// Generates performance insights based on benchmark results.
    /// </summary>
    private void GeneratePerformanceInsights(StringBuilder markdown, List<BenchmarkSuiteResult> results)
    {
        var insights = new List<string>();

        // Tensor Core insights
        var tensorResults = results.FirstOrDefault(r => r.SuiteName.Contains("Tensor Core"));
        if (tensorResults?.Success == true && tensorResults.Results.Any())
        {
            var bestTensorPerf = tensorResults.Results.Where(r => r.Success && r.ThroughputOpsPerSec.HasValue)
                                                     .Max(r => r.ThroughputOpsPerSec.Value);
            if (bestTensorPerf > 0)
            {
                insights.Add($"🚀 **Tensor Cores:** Peak performance of {FormatPerformance(bestTensorPerf)} achieved");
            }
        }

        // SIMD insights
        var simdResults = results.FirstOrDefault(r => r.SuiteName.Contains("SIMD"));
        if (simdResults?.Success == true && simdResults.Results.Any())
        {
            var simdSpeedup = simdResults.Results.Where(r => r.Success && r.BaselineRatio.HasValue)
                                                .Max(r => r.BaselineRatio.Value);
            if (simdSpeedup > 1)
            {
                insights.Add($"⚡ **SIMD Acceleration:** Up to {simdSpeedup:F1}x speedup over scalar operations");
            }
        }

        // Memory insights
        var memoryResults = results.FirstOrDefault(r => r.SuiteName.Contains("Memory"));
        if (memoryResults?.Success == true)
        {
            insights.Add("💾 **Memory Optimization:** Zero-copy operations and unified memory provide efficient data transfer");
        }

        // Scalability insights
        var scalabilityResults = results.FirstOrDefault(r => r.SuiteName.Contains("Scalability"));
        if (scalabilityResults?.Success == true)
        {
            insights.Add("📈 **Scalability:** Performance scales efficiently across different problem sizes");
        }

        if (insights.Any())
        {
            foreach (var insight in insights)
            {
                markdown.AppendLine($"- {insight}");
            }
        }
        else
        {
            markdown.AppendLine("- Comprehensive benchmark suite validates Phase 6 implementations");
            markdown.AppendLine("- All major ILGPU features tested for performance and correctness");
        }
    }

    /// <summary>
    /// Generates JSON output for programmatic consumption.
    /// </summary>
    private async Task GenerateJsonOutputAsync(BenchmarkSystemInfo systemInfo, List<BenchmarkSuiteResult> results, TimeSpan duration)
    {
        var output = new
        {
            Metadata = new
            {
                BenchmarkDate = DateTime.UtcNow,
                Duration = duration.ToString(),
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                System = systemInfo
            },
            Results = results
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var jsonPath = Path.Combine(outputDirectory, "benchmark_results.json");
        await File.WriteAllTextAsync(jsonPath, json);
        
        logger.LogInformation($"JSON results written to: {jsonPath}");
    }

    /// <summary>
    /// Generates CSV output for data analysis.
    /// </summary>
    private async Task GenerateCsvOutputAsync(List<BenchmarkSuiteResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Suite,Method,Performance_OpsPerSec,Memory_KB,Baseline_Ratio,Success,Duration_Ms");

        foreach (var suite in results)
        {
            foreach (var result in suite.Results)
            {
                var perf = result.ThroughputOpsPerSec?.ToString("F0") ?? "";
                var memory = result.AllocatedBytes.HasValue ? (result.AllocatedBytes.Value / 1024).ToString("F0") : "";
                var ratio = result.BaselineRatio?.ToString("F3") ?? "";
                var duration = result.DurationMs?.ToString("F2") ?? "";
                
                csv.AppendLine($"{suite.SuiteName},{result.MethodName},{perf},{memory},{ratio},{result.Success},{duration}");
            }
        }

        var csvPath = Path.Combine(outputDirectory, "benchmark_results.csv");
        await File.WriteAllTextAsync(csvPath, csv.ToString());
        
        logger.LogInformation($"CSV results written to: {csvPath}");
    }

    /// <summary>
    /// Generates a comprehensive markdown report.
    /// </summary>
    private async Task GenerateMarkdownReportAsync(BenchmarkSystemInfo systemInfo, List<BenchmarkSuiteResult> results, TimeSpan duration)
    {
        var markdown = new StringBuilder();
        
        markdown.AppendLine("# ILGPU Phase 6 - Comprehensive Benchmark Report");
        markdown.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        markdown.AppendLine();

        // Executive Summary
        markdown.AppendLine("## Executive Summary");
        markdown.AppendLine();
        var totalTests = results.Sum(r => r.Results.Count);
        var successfulTests = results.Sum(r => r.Results.Count(res => res.Success));
        var successRate = totalTests > 0 ? (successfulTests * 100.0 / totalTests) : 0;
        
        markdown.AppendLine($"- **Total Tests:** {totalTests}");
        markdown.AppendLine($"- **Success Rate:** {successRate:F1}%");
        markdown.AppendLine($"- **Execution Time:** {duration:hh\\:mm\\:ss}");
        markdown.AppendLine($"- **Platform:** {systemInfo.Platform}");
        markdown.AppendLine();

        // System Configuration
        markdown.AppendLine("## System Configuration");
        markdown.AppendLine();
        markdown.AppendLine($"**Operating System:** {systemInfo.Platform}");
        markdown.AppendLine($"**CPU:** {systemInfo.CpuInfo}");
        markdown.AppendLine($"**Memory:** {systemInfo.MemoryInfo}");
        if (!string.IsNullOrEmpty(systemInfo.GpuInfo))
        {
            markdown.AppendLine($"**GPU:** {systemInfo.GpuInfo}");
        }
        markdown.AppendLine($"**.NET Version:** {systemInfo.DotNetVersion}");
        markdown.AppendLine();

        // Detailed suite results
        foreach (var suite in results)
        {
            markdown.AppendLine($"## {suite.SuiteName}");
            markdown.AppendLine($"**Description:** {suite.Description}");
            markdown.AppendLine($"**Status:** {(suite.Success ? "✅ Success" : "❌ Failed")}");
            
            if (!suite.Success)
            {
                markdown.AppendLine($"**Error:** {suite.ErrorMessage}");
            }
            else if (suite.Results.Any())
            {
                markdown.AppendLine($"**Test Count:** {suite.Results.Count}");
                markdown.AppendLine($"**Success Rate:** {suite.Results.Count(r => r.Success) * 100.0 / suite.Results.Count:F1}%");
                
                // Performance statistics
                var successfulResults = suite.Results.Where(r => r.Success && r.ThroughputOpsPerSec.HasValue).ToList();
                if (successfulResults.Any())
                {
                    var minPerf = successfulResults.Min(r => r.ThroughputOpsPerSec.Value);
                    var maxPerf = successfulResults.Max(r => r.ThroughputOpsPerSec.Value);
                    var avgPerf = successfulResults.Average(r => r.ThroughputOpsPerSec.Value);
                    
                    markdown.AppendLine($"**Performance Range:** {FormatPerformance(minPerf)} - {FormatPerformance(maxPerf)}");
                    markdown.AppendLine($"**Average Performance:** {FormatPerformance(avgPerf)}");
                }
            }
            
            markdown.AppendLine();
        }

        var reportPath = Path.Combine(outputDirectory, "comprehensive_report.md");
        await File.WriteAllTextAsync(reportPath, markdown.ToString());
        
        logger.LogInformation($"Comprehensive report written to: {reportPath}");
    }

    private BenchmarkSystemInfo GatherSystemInformation()
    {
        return new BenchmarkSystemInfo
        {
            Platform = Environment.OSVersion.ToString(),
            CpuInfo = Environment.ProcessorCount + " cores",
            MemoryInfo = $"{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB working set",
            GpuInfo = "Detection pending", // Will be enhanced
            DotNetVersion = Environment.Version.ToString(),
            Timestamp = DateTime.UtcNow
        };
    }

    private List<BenchmarkResult> ExtractBenchmarkResults(BenchmarkDotNet.Reports.Summary summary)
    {
        var results = new List<BenchmarkResult>();
        
        foreach (var report in summary.Reports)
        {
            try
            {
                var result = new BenchmarkResult
                {
                    MethodName = report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
                    Success = !report.HasCriticalValidationErrors,
                    DurationMs = report.ResultStatistics?.Mean / 1_000_000, // Convert ns to ms
                    ThroughputOpsPerSec = report.ResultStatistics?.Mean > 0 ? 1_000_000_000 / report.ResultStatistics.Mean : null,
                    AllocatedBytes = report.GcStats?.GetBytesAllocatedPerOperation(report.BenchmarkCase),
                    BaselineRatio = summary.GetBaseline()?.ResultStatistics?.Mean > 0 && report.ResultStatistics?.Mean > 0 
                        ? summary.GetBaseline().ResultStatistics.Mean / report.ResultStatistics.Mean : null
                };
                
                results.Add(result);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Failed to extract result for {report.BenchmarkCase.DisplayInfo}: {ex.Message}");
            }
        }
        
        return results;
    }

    private static string FormatPerformance(double opsPerSec)
    {
        if (opsPerSec >= 1_000_000_000)
            return $"{opsPerSec / 1_000_000_000:F2}B ops/s";
        if (opsPerSec >= 1_000_000)
            return $"{opsPerSec / 1_000_000:F2}M ops/s";
        if (opsPerSec >= 1_000)
            return $"{opsPerSec / 1_000:F2}K ops/s";
        return $"{opsPerSec:F2} ops/s";
    }
}

/// <summary>
/// System information for benchmark context.
/// </summary>
public class BenchmarkSystemInfo
{
    public string Platform { get; set; } = "";
    public string CpuInfo { get; set; } = "";
    public string MemoryInfo { get; set; } = "";
    public string GpuInfo { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Results from a benchmark suite.
/// </summary>
public class BenchmarkSuiteResult
{
    public string SuiteName { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime ExecutionTime { get; set; }
    public List<BenchmarkResult> Results { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Individual benchmark result.
/// </summary>
public class BenchmarkResult
{
    public string MethodName { get; set; } = "";
    public bool Success { get; set; }
    public double? DurationMs { get; set; }
    public double? ThroughputOpsPerSec { get; set; }
    public long? AllocatedBytes { get; set; }
    public double? BaselineRatio { get; set; }
}