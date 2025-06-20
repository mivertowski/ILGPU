// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: PerformanceProfilingTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Profiling;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ILGPU.Tests.CPU
{
    public class PerformanceProfilingTests : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;
        
        public PerformanceProfilingTests()
        {
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUAccelerator(0);
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                accelerator?.Dispose();
                context?.Dispose();
            }
        }

        [Fact]
        public void Profiler_Basic_SessionManagement()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            
            Assert.False(profiler.IsProfilingEnabled);
            Assert.Empty(profiler.CurrentSessionId);

            var sessionId = profiler.StartSession("Test Session");
            Assert.True(profiler.IsProfilingEnabled);
            Assert.Equal(sessionId, profiler.CurrentSessionId);

            var report = profiler.EndSession();
            Assert.False(profiler.IsProfilingEnabled);
            Assert.Equal("Test Session", report.SessionName);
            Assert.Equal(sessionId, report.SessionId);
        }

        [Fact]
        public void Profiler_KernelProfiling_BasicFlow()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Kernel Test");

            var gridSize = new Index3D(10, 1, 1);
            var groupSize = new Index3D(1, 1, 1);

            using var kernelContext = profiler.StartKernelProfiling("TestKernel", gridSize, groupSize);
            Assert.Equal("TestKernel", kernelContext.KernelName);

            kernelContext.RecordCompilation(TimeSpan.FromMilliseconds(50));
            kernelContext.RecordLaunchParameters(256, 32);
            kernelContext.RecordExecution(TimeSpan.FromMilliseconds(10), 1000.0);

            var report = profiler.EndSession();
            Assert.Single(report.KernelExecutions);
            
            var execution = report.KernelExecutions.First();
            Assert.Equal("TestKernel", execution.KernelName);
            Assert.Equal(gridSize, execution.GridSize);
            Assert.Equal(groupSize, execution.GroupSize);
            Assert.Equal(TimeSpan.FromMilliseconds(50), execution.CompilationTime);
            Assert.Equal(TimeSpan.FromMilliseconds(10), execution.ExecutionTime);
            Assert.Equal(256, execution.SharedMemorySize);
            Assert.Equal(32, execution.RegisterCount);
            Assert.Equal(1000.0, execution.Throughput);
        }

        [Fact]
        public void Profiler_MemoryProfiling_BasicFlow()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Memory Test");

            using var memoryContext = profiler.StartMemoryProfiling(
                MemoryOperationType.HostToDevice, 
                1024, 
                "CPU", 
                "GPU");

            Assert.Equal(MemoryOperationType.HostToDevice, memoryContext.OperationType);
            Assert.Equal(1024, memoryContext.SizeInBytes);

            memoryContext.RecordCompletion(TimeSpan.FromMilliseconds(5), 200.0);

            var report = profiler.EndSession();
            Assert.Single(report.MemoryOperations);
            
            var operation = report.MemoryOperations.First();
            Assert.Equal(MemoryOperationType.HostToDevice, operation.OperationType);
            Assert.Equal(1024, operation.SizeInBytes);
            Assert.Equal("CPU", operation.Source);
            Assert.Equal("GPU", operation.Destination);
            Assert.Equal(200.0, operation.Bandwidth);
        }

        [Fact]
        public void Profiler_CustomEvents()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Custom Events Test");

            var metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["EventType"] = "UserDefined",
                ["Value"] = 42
            };

            profiler.RecordEvent("CustomEvent", TimeSpan.FromMilliseconds(15), metadata);

            var report = profiler.EndSession();
            Assert.Single(report.CustomEvents);
            
            var customEvent = report.CustomEvents.First();
            Assert.Equal("CustomEvent", customEvent.EventName);
            Assert.Equal(TimeSpan.FromMilliseconds(15), customEvent.Duration);
            Assert.Equal("UserDefined", customEvent.Metadata["EventType"]);
            Assert.Equal(42, customEvent.Metadata["Value"]);
        }

        [Fact]
        public void Profiler_MetricsCalculation()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Metrics Test");

            // Record multiple kernel executions
            for (int i = 0; i < 3; i++)
            {
                using var kernelContext = profiler.StartKernelProfiling($"Kernel{i}", new Index3D(10), new Index3D(1));
                kernelContext.RecordExecution(TimeSpan.FromMilliseconds(i + 1), 100.0 * (i + 1));
            }

            // Record memory operations
            for (int i = 0; i < 2; i++)
            {
                using var memoryContext = profiler.StartMemoryProfiling(MemoryOperationType.Allocation, 1024 * (i + 1));
                memoryContext.RecordCompletion(TimeSpan.FromMilliseconds(i + 1), 500.0);
            }

            var metrics = profiler.GetCurrentMetrics();
            
            // Kernel metrics
            Assert.Equal(3, metrics.Kernels.TotalKernels);
            Assert.True(metrics.Kernels.TotalExecutionTime > TimeSpan.Zero);
            Assert.True(metrics.Kernels.AverageThroughput > 0);
            Assert.Equal(3, metrics.Kernels.KernelStats.Count);

            // Memory metrics
            Assert.Equal(2, metrics.Memory.TotalOperations);
            Assert.Equal(3072, metrics.Memory.TotalBytesTransferred); // 1024 + 2048
            Assert.True(metrics.Memory.AverageBandwidth > 0);

            var report = profiler.EndSession();
            Assert.Equal(3, report.KernelExecutions.Count);
            Assert.Equal(2, report.MemoryOperations.Count);
        }

        [Fact]
        public void Profiler_SessionReports()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            
            // Create multiple sessions
            profiler.StartSession("Session 1");
            profiler.RecordEvent("Event1", TimeSpan.FromMilliseconds(10));
            profiler.EndSession();

            profiler.StartSession("Session 2");
            profiler.RecordEvent("Event2", TimeSpan.FromMilliseconds(20));
            profiler.EndSession();

            var reports = profiler.GetSessionReports();
            Assert.Equal(2, reports.Count);
            Assert.Contains(reports, r => r.SessionName == "Session 1");
            Assert.Contains(reports, r => r.SessionName == "Session 2");
        }

        [Fact]
        public void Profiler_ClearData()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            
            profiler.StartSession("Test Session");
            profiler.RecordEvent("TestEvent", TimeSpan.FromMilliseconds(10));
            profiler.EndSession();

            Assert.Single(profiler.GetSessionReports());

            profiler.Clear();
            Assert.Empty(profiler.GetSessionReports());
        }

        [Fact]
        public void Profiler_DisabledState_NoOp()
        {
            using var profiler = new PerformanceProfiler(accelerator, enabledByDefault: false);
            
            // When profiling is disabled, these should return no-op contexts
            using var kernelContext = profiler.StartKernelProfiling("TestKernel", new Index3D(1), new Index3D(1));
            using var memoryContext = profiler.StartMemoryProfiling(MemoryOperationType.Allocation, 1024);

            Assert.IsType<NoOpKernelProfilingContext>(kernelContext);
            Assert.IsType<NoOpMemoryProfilingContext>(memoryContext);
        }

        [Fact]
        public async Task Profiler_Export_Json()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            
            profiler.StartSession("Export Test");
            profiler.RecordEvent("TestEvent", TimeSpan.FromMilliseconds(10));
            profiler.EndSession();

            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                await profiler.ExportAsync(tempFile, ProfileExportFormat.Json);
                Assert.True(System.IO.File.Exists(tempFile));
                
                var content = await System.IO.File.ReadAllTextAsync(tempFile);
                Assert.Contains("Export Test", content);
                Assert.Contains("TestEvent", content);
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task Profiler_Export_Csv()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            
            profiler.StartSession("CSV Export Test");
            using var kernelContext = profiler.StartKernelProfiling("TestKernel", new Index3D(1), new Index3D(1));
            kernelContext.RecordExecution(TimeSpan.FromMilliseconds(5));
            profiler.EndSession();

            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                await profiler.ExportAsync(tempFile, ProfileExportFormat.Csv);
                Assert.True(System.IO.File.Exists(tempFile));
                
                var content = await System.IO.File.ReadAllTextAsync(tempFile);
                Assert.Contains("SessionId,KernelName", content);
                Assert.Contains("TestKernel", content);
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public void Profiler_KernelStatistics()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Statistics Test");

            // Record multiple executions of the same kernel
            var executionTimes = new[] { 10, 15, 12, 18, 11 }; // milliseconds
            
            foreach (var time in executionTimes)
            {
                using var kernelContext = profiler.StartKernelProfiling("StatKernel", new Index3D(1), new Index3D(1));
                kernelContext.RecordExecution(TimeSpan.FromMilliseconds(time));
            }

            var report = profiler.EndSession();
            var kernelStats = report.Metrics.Kernels.KernelStats["StatKernel"];
            
            Assert.Equal(5, kernelStats.ExecutionCount);
            Assert.Equal(TimeSpan.FromMilliseconds(10), kernelStats.MinExecutionTime);
            Assert.Equal(TimeSpan.FromMilliseconds(18), kernelStats.MaxExecutionTime);
            Assert.True(kernelStats.AverageExecutionTime > TimeSpan.Zero);
            Assert.True(kernelStats.ExecutionTimeStdDev >= TimeSpan.Zero);
        }

        [Fact]
        public void Profiler_MemoryOperationStatistics()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Memory Stats Test");

            // Record various memory operations
            var operations = new[]
            {
                (MemoryOperationType.Allocation, 1024L, 100.0),
                (MemoryOperationType.Allocation, 2048L, 150.0),
                (MemoryOperationType.HostToDevice, 1024L, 200.0),
                (MemoryOperationType.DeviceToHost, 1024L, 180.0)
            };

            foreach (var (opType, size, bandwidth) in operations)
            {
                using var memoryContext = profiler.StartMemoryProfiling(opType, size);
                memoryContext.RecordCompletion(TimeSpan.FromMilliseconds(5), bandwidth);
            }

            var report = profiler.EndSession();
            var memoryStats = report.Metrics.Memory;
            
            Assert.Equal(4, memoryStats.TotalOperations);
            Assert.Equal(5120, memoryStats.TotalBytesTransferred); // 1024 + 2048 + 1024 + 1024
            Assert.True(memoryStats.AverageBandwidth > 0);
            Assert.Equal(2, memoryStats.OperationStats[MemoryOperationType.Allocation].OperationCount);
            Assert.Equal(1, memoryStats.OperationStats[MemoryOperationType.HostToDevice].OperationCount);
            Assert.Equal(1, memoryStats.OperationStats[MemoryOperationType.DeviceToHost].OperationCount);
        }

        [Fact]
        public void Profiler_Recommendations()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Recommendations Test");

            // Create conditions that should trigger recommendations
            
            // Low cache hit ratio (all kernels compiled)
            for (int i = 0; i < 10; i++)
            {
                using var kernelContext = profiler.StartKernelProfiling($"UniqueKernel{i}", new Index3D(1), new Index3D(1));
                kernelContext.RecordCompilation(TimeSpan.FromMilliseconds(50)); // All compiled (not cached)
                kernelContext.RecordExecution(TimeSpan.FromMilliseconds(5));
            }

            // Low memory pool usage
            for (int i = 0; i < 5; i++)
            {
                using var memoryContext = profiler.StartMemoryProfiling(MemoryOperationType.Allocation, 1024);
                memoryContext.RecordCompletion(TimeSpan.FromMilliseconds(2));
                // No pool usage recorded
            }

            var report = profiler.EndSession();
            
            Assert.NotEmpty(report.Recommendations);
            Assert.Contains(report.Recommendations, r => r.Category == RecommendationCategory.KernelOptimization);
            Assert.Contains(report.Recommendations, r => r.Category == RecommendationCategory.MemoryOptimization);
        }

        [Fact]
        public void Profiler_AcceleratorMetrics()
        {
            using var profiler = new PerformanceProfiler(accelerator);
            profiler.StartSession("Accelerator Metrics Test");

            // Simulate some work
            using (var kernelContext = profiler.StartKernelProfiling("WorkKernel", new Index3D(100), new Index3D(1)))
            {
                Thread.Sleep(50); // Simulate work
                kernelContext.RecordExecution(TimeSpan.FromMilliseconds(30));
            }

            using (var memoryContext = profiler.StartMemoryProfiling(MemoryOperationType.HostToDevice, 4096))
            {
                Thread.Sleep(20); // Simulate work
                memoryContext.RecordCompletion(TimeSpan.FromMilliseconds(10));
            }

            var report = profiler.EndSession();
            var acceleratorMetrics = report.Metrics.Accelerator;
            
            Assert.True(acceleratorMetrics.UtilizationPercentage >= 0);
            Assert.True(acceleratorMetrics.TotalActiveTime >= TimeSpan.Zero);
            Assert.True(acceleratorMetrics.TotalIdleTime >= TimeSpan.Zero);
            Assert.NotEmpty(acceleratorMetrics.AcceleratorSpecificMetrics);
        }
    }
}