// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: Program.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using ILGPU.Benchmarks.Benchmarks;
using ILGPU.Benchmarks.BurnIn;
using ILGPU.Benchmarks.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Reflection;

namespace ILGPU.Benchmarks;

/// <summary>
/// Main program entry point for ILGPU Phase 6 benchmarks.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Check for unattended mode
            if (args.Contains("--unattended") || args.Contains("-u"))
            {
                return await RunUnattendedBenchmarksAsync(args);
            }
            
            // Check for diagnostics mode
            if (args.Contains("--diagnose"))
            {
                TestBenchmarkRunner.RunDiagnostics();
                return 0;
            }
            
            // Report generation removed for now
            
            // Check for debug mode
            if (args.Contains("--debug-benchmark"))
            {
                DebugBenchmarkRunner.RunSingleBenchmark();
                return 0;
            }
            
            if (args.Contains("--debug-all"))
            {
                DebugBenchmarkRunner.TestAllBenchmarkTypes();
                return 0;
            }

            AnsiConsole.Write(
                new FigletText("ILGPU Phase 6")
                    .Centered()
                    .Color(Color.Cyan1));

            AnsiConsole.Write(
                new Panel(new Text("Tensor Core Integration & .NET SIMD Unification Benchmarks", new Style(Color.Yellow)))
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Cyan1)
                    .Padding(1, 0));

            // Create host for dependency injection
            using var host = CreateHostBuilder(args).Build();
            await host.StartAsync();

            var benchmarkRunner = host.Services.GetRequiredService<Infrastructure.BenchmarkRunner>();
            var burnInRunner = host.Services.GetRequiredService<BurnInTestRunner>();

            // Show main menu
            while (true)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan1]Select benchmark suite to run:[/]")
                        .PageSize(10)
                        .AddChoices(new[]
                        {
                            "🚀 Quick Performance Suite",
                            "🎯 Tensor Core Benchmarks",
                            "⚡ SIMD Performance Tests",
                            "🔄 Hybrid Processing Benchmarks",
                            "💾 Memory Operations Benchmarks",
                            "🔥 Burn-in Test (Maximum Load)",
                            "📊 Comprehensive Benchmark Suite",
                            "🤖 Unattended Benchmarks (GitHub Ready)",
                            "⚙️ System Information",
                            "❌ Exit"
                        }));

                switch (choice)
                {
                    case "🚀 Quick Performance Suite":
                        await benchmarkRunner.RunQuickSuiteAsync();
                        break;
                    case "🎯 Tensor Core Benchmarks":
                        await benchmarkRunner.RunTensorCoreBenchmarksAsync();
                        break;
                    case "⚡ SIMD Performance Tests":
                        await benchmarkRunner.RunSimdBenchmarksAsync();
                        break;
                    case "🔄 Hybrid Processing Benchmarks":
                        await benchmarkRunner.RunHybridBenchmarksAsync();
                        break;
                    case "💾 Memory Operations Benchmarks":
                        await benchmarkRunner.RunMemoryBenchmarksAsync();
                        break;
                    case "🔥 Burn-in Test (Maximum Load)":
                        await burnInRunner.RunBurnInTestAsync();
                        break;
                    case "📊 Comprehensive Benchmark Suite":
                        await benchmarkRunner.RunComprehensiveSuiteAsync();
                        break;
                    case "🤖 Unattended Benchmarks (GitHub Ready)":
                        var unattendedRunner = host.Services.GetRequiredService<UnattendedBenchmarkRunner>();
                        await unattendedRunner.RunUnattendedBenchmarksAsync();
                        break;
                    case "⚙️ System Information":
                        SystemInfo.DisplaySystemInformation();
                        break;
                    case "❌ Exit":
                        AnsiConsole.MarkupLine("[green]Thank you for using ILGPU Phase 6 Benchmarks![/]");
                        return 0;
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
                Console.ReadKey();
                Console.Clear();
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 1;
        }
    }

    /// <summary>
    /// Runs benchmarks in unattended mode for CI/CD integration.
    /// </summary>
    private static async Task<int> RunUnattendedBenchmarksAsync(string[] args)
    {
        Console.WriteLine("Starting ILGPU Phase 6 Benchmarks in unattended mode...");
        Console.WriteLine($"Output directory: {Path.Combine(Environment.CurrentDirectory, "BenchmarkResults")}");
        Console.WriteLine();

        try
        {
            // Create host for dependency injection (without interactive console)
            using var host = CreateHostBuilder(args).Build();
            await host.StartAsync();

            var unattendedRunner = host.Services.GetRequiredService<UnattendedBenchmarkRunner>();
            await unattendedRunner.RunUnattendedBenchmarksAsync();

            Console.WriteLine();
            Console.WriteLine("✅ Unattended benchmarks completed successfully!");
            Console.WriteLine($"📁 Results saved to: {Path.Combine(Environment.CurrentDirectory, "BenchmarkResults")}");
            Console.WriteLine();
            Console.WriteLine("Output files:");
            Console.WriteLine("  📄 README_Benchmarks.md    - GitHub README-ready benchmark results");
            Console.WriteLine("  📊 benchmark_results.json  - JSON data for programmatic consumption");
            Console.WriteLine("  📈 benchmark_results.csv   - CSV data for analysis");
            Console.WriteLine("  📋 comprehensive_report.md - Detailed technical report");
            Console.WriteLine();
            Console.WriteLine("💡 Use '--help' for more command-line options");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during unattended benchmark execution: {ex.Message}");
            Console.WriteLine($"📝 Stack trace: {ex.StackTrace}");
            return 1;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                services.AddSingleton<Infrastructure.BenchmarkRunner>();
                services.AddSingleton<BurnInTestRunner>();
                services.AddSingleton<UnattendedBenchmarkRunner>();
                services.AddSingleton<BenchmarkConfig>();
            });
}

/// <summary>
/// Displays system information relevant to benchmarking.
/// </summary>
public static class SystemInfo
{
    public static void DisplaySystemInformation()
    {
        var table = new Table()
            .Title("[cyan1]System Information[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn("[yellow]Component[/]");
        table.AddColumn("[green]Information[/]");

        // Basic system info
        table.AddRow("OS", Environment.OSVersion.ToString());
        table.AddRow("Runtime", Environment.Version.ToString());
        table.AddRow("Architecture", Environment.OSVersion.Platform.ToString());
        table.AddRow("Processor Count", Environment.ProcessorCount.ToString());
        table.AddRow("64-bit Process", Environment.Is64BitProcess.ToString());
        table.AddRow("Working Set", $"{Environment.WorkingSet / (1024 * 1024):N0} MB");

        // SIMD capabilities
        table.AddRow("Vector<T> Support", System.Numerics.Vector.IsHardwareAccelerated.ToString());
        table.AddRow("Vector<T> Count", System.Numerics.Vector<float>.Count.ToString());

        // Hardware intrinsics
        table.AddRow("AVX Support", System.Runtime.Intrinsics.X86.Avx.IsSupported.ToString());
        table.AddRow("AVX2 Support", System.Runtime.Intrinsics.X86.Avx2.IsSupported.ToString());
        table.AddRow("SSE Support", System.Runtime.Intrinsics.X86.Sse.IsSupported.ToString());
        table.AddRow("SSE2 Support", System.Runtime.Intrinsics.X86.Sse2.IsSupported.ToString());
        table.AddRow("ARM AdvSimd Support", System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported.ToString());

        // ILGPU information
        try
        {
            using var context = Context.CreateDefault();
            table.AddRow("ILGPU Devices", context.Devices.Count().ToString());
            
            foreach (var device in context.Devices)
            {
                table.AddRow($"  {device.AcceleratorType}", device.Name);
                table.AddRow($"  Memory Size", $"{device.MemorySize / (1024 * 1024):N0} MB");
                table.AddRow($"  Max Grid Size", device.MaxGridSize.ToString());
                table.AddRow($"  Max Group Size", device.MaxGroupSize.ToString());
            }
        }
        catch (Exception ex)
        {
            table.AddRow("ILGPU Status", $"[red]Error: {ex.Message}[/]");
        }

        AnsiConsole.Write(table);
    }
}
