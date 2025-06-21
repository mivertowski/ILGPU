using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using ILGPU.Benchmarks.Benchmarks;
using System;

namespace ILGPU.Benchmarks
{
    public static class DebugBenchmarkRunner
    {
        public static void RunSingleBenchmark()
        {
            Console.WriteLine("=== Debug: Testing SimdVectorBenchmarks ===");
            
            try 
            {
                var config = ManualConfig.CreateEmpty()
                    .AddLogger(ConsoleLogger.Default)
                    .AddJob(Job.Dry.WithWarmupCount(1).WithIterationCount(1));
                
                var summary = BenchmarkRunner.Run<SimdVectorBenchmarks>(config);
                
                Console.WriteLine($"Benchmark cases found: {summary.BenchmarksCases.Count()}");
                Console.WriteLine($"Reports generated: {summary.Reports.Count()}");
                Console.WriteLine($"Has validation errors: {summary.HasCriticalValidationErrors}");
                
                if (summary.ValidationErrors.Any())
                {
                    Console.WriteLine("Validation errors:");
                    foreach (var error in summary.ValidationErrors)
                    {
                        Console.WriteLine($"  - {error.Message}");
                    }
                }
                
                foreach (var report in summary.Reports)
                {
                    Console.WriteLine($"Report: {report.BenchmarkCase.DisplayInfo}");
                    Console.WriteLine($"  Measurements: {report.AllMeasurements.Count()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex}");
            }
        }
        
        public static void TestAllBenchmarkTypes()
        {
            var benchmarkTypes = new[]
            {
                ("SIMD Vector", typeof(SimdVectorBenchmarks)),
                ("Mixed Precision", typeof(MixedPrecisionBenchmarks)),
                ("BFloat16", typeof(BFloat16Benchmarks)),
                ("Platform Intrinsics", typeof(PlatformIntrinsicsBenchmarks)),
                ("Matrix Vector", typeof(MatrixVectorBenchmarks))
            };

            var config = ManualConfig.CreateEmpty()
                .AddLogger(ConsoleLogger.Default)
                .AddJob(Job.Dry.WithWarmupCount(1).WithIterationCount(1));

            foreach (var (name, type) in benchmarkTypes)
            {
                Console.WriteLine($"\n=== Testing {name} ===");
                try
                {
                    var summary = BenchmarkRunner.Run(type, config);
                    Console.WriteLine($"  Cases: {summary.BenchmarksCases.Count()}, Reports: {summary.Reports.Count()}, Errors: {summary.HasCriticalValidationErrors}");
                    
                    if (!summary.Reports.Any() && summary.BenchmarksCases.Any())
                    {
                        Console.WriteLine("  → Found cases but no reports generated");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Exception: {ex.Message}");
                }
            }
        }
    }
}