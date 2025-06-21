using System;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Exporters;
using ILGPU.Benchmarks.Benchmarks;

namespace ILGPU.Benchmarks
{
    public class TestBenchmarkRunner
    {
        public static void RunDiagnostics()
        {
            Console.WriteLine("=== ILGPU Benchmark Diagnostics ===");
            Console.WriteLine();
            
            // Create a simple config for testing
            var config = ManualConfig.CreateEmpty()
                .AddLogger(ConsoleLogger.Default)
                .AddExporter(MarkdownExporter.Console)
                .AddJob(Job.Dry.WithWarmupCount(1).WithIterationCount(1));

            var benchmarkTypes = new[]
            {
                ("SIMD Vector Operations", typeof(SimdVectorBenchmarks)),
                ("Mixed Precision", typeof(MixedPrecisionBenchmarks)),
                ("BFloat16", typeof(BFloat16Benchmarks)),
                ("Platform Intrinsics", typeof(PlatformIntrinsicsBenchmarks)),
                ("Matrix-Vector", typeof(MatrixVectorBenchmarks)),
                ("CPU vs GPU", typeof(CpuGpuComparisonBenchmarks)),
                ("Hybrid Processing", typeof(HybridProcessingBenchmarks)),
                ("Memory", typeof(MemoryBenchmarks)),
                ("Unified Memory", typeof(UnifiedMemoryBenchmarks)),
                ("Scalability", typeof(ScalabilityBenchmarks))
            };

            foreach (var (name, type) in benchmarkTypes)
            {
                Console.WriteLine($"\n--- Testing {name} ---");
                try
                {
                    var summary = BenchmarkRunner.Run(type, config);
                    Console.WriteLine($"  Cases found: {summary.BenchmarksCases.Count()}");
                    Console.WriteLine($"  Reports generated: {summary.Reports.Count()}");
                    Console.WriteLine($"  Has errors: {summary.HasCriticalValidationErrors}");
                    
                    if (summary.ValidationErrors.Any())
                    {
                        Console.WriteLine("  Validation errors:");
                        foreach (var error in summary.ValidationErrors)
                        {
                            Console.WriteLine($"    - {error.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ERROR: {ex.Message}");
                }
            }
            
            Console.WriteLine("\n=== Diagnostics Complete ===");
        }
    }
}