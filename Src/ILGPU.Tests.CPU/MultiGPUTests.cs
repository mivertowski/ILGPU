// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: MultiGPUTests.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.MultiGPU;
using ILGPU.Runtime.CPU;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ILGPU.Tests.CPU
{
    /// <summary>
    /// Tests for multi-GPU orchestration capabilities.
    /// </summary>
    public class MultiGPUTests : IDisposable
    {
        #region Fields

        private readonly Context context;
        private readonly Accelerator accelerator;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiGPUTests"/> class.
        /// </summary>
        public MultiGPUTests()
        {
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUaccelerator(0);
        }

        #endregion

        #region Test Work Items

        /// <summary>
        /// Simple test work item for multi-GPU testing.
        /// </summary>
        private class TestWorkItem : MultiGPUWorkItem
        {
            private readonly int processingTime;
            private readonly long memorySize;

            public TestWorkItem(string id, int processingTime = 100, long memorySize = 1024)
                : base(id)
            {
                this.processingTime = processingTime;
                this.memorySize = memorySize;
                EstimatedExecutionTime = processingTime;
                MemoryRequirement = memorySize;
            }

            public override async Task ExecuteAsync(GPUInfo gpu, CancellationToken cancellationToken)
            {
                // Simulate work
                await Task.Delay(processingTime, cancellationToken).ConfigureAwait(false);
                
                // Simple computation to verify GPU usage
                using var buffer = gpu.accelerator.Allocate1D<int>(100);
                var kernel = gpu.accelerator.LoadAutoGroupedKernel<Index1D, ArrayView<int>>(
                    (index, data) => data[index] = index);
                
                kernel(gpu.accelerator.DefaultStream, (Index1D)100, buffer.View);
                gpu.accelerator.Synchronize();
            }
        }

        /// <summary>
        /// High priority test work item.
        /// </summary>
        private class HighPriorityWorkItem : TestWorkItem
        {
            public HighPriorityWorkItem(string id) : base(id, 50, 512)
            {
                // High priority items have priority 10
            }
        }

        /// <summary>
        /// Memory-intensive test work item.
        /// </summary>
        private class MemoryIntensiveWorkItem : TestWorkItem
        {
            public MemoryIntensiveWorkItem(string id) : base(id, 200, 1024 * 1024 * 100) // 100MB
            {
            }

            public override bool CanExecuteOn(GPUInfo gpu)
            {
                // Require significant memory
                return base.CanExecuteOn(gpu) && gpu.MemoryInfo.TotalMemory > MemoryRequirement * 2;
            }
        }

        #endregion

        #region Basic Orchestrator Tests

        [Fact]
        public void MultiGPU_OrchestratorCreation()
        {
            var accelerators = new[] { accelerator };
            var options = new MultiGPUOptions
            {
                DistributionStrategy = WorkDistributionStrategy.RoundRobin,
                SynchronizationMode = SynchronizationMode.PerBatch,
                EnableLoadBalancing = false
            };

            using var orchestrator = new MultiGPUOrchestrator(accelerators, options);

            Assert.NotNull(orchestrator);
            Assert.Equal(1, orchestrator.AvailableGPUs.Count);
            Assert.Equal(1, orchestrator.ActiveGPUs.Count);
            Assert.Equal(options.DistributionStrategy, orchestrator.Options.DistributionStrategy);
        }

        [Fact]
        public void MultiGPU_GPUInfo()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            var gpu = orchestrator.AvailableGPUs[0];
            
            Assert.NotNull(gpu);
            Assert.Equal(0, gpu.Index);
            Assert.True(gpu.PerformanceScore > 0);
            Assert.Equal(0.0, gpu.CurrentLoad);
            Assert.True(gpu.IsActive);
            Assert.NotNull(gpu.DeviceName);
            Assert.NotNull(gpu.MemoryInfo);
        }

        [Fact]
        public void MultiGPU_EnableDisableGPU()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            Assert.Equal(1, orchestrator.ActiveGPUs.Count);

            // Disable GPU
            orchestrator.SetGPUEnabled(0, false);
            Assert.Equal(0, orchestrator.ActiveGPUs.Count);
            Assert.False(orchestrator.AvailableGPUs[0].IsActive);

            // Re-enable GPU
            orchestrator.SetGPUEnabled(0, true);
            Assert.Equal(1, orchestrator.ActiveGPUs.Count);
            Assert.True(orchestrator.AvailableGPUs[0].IsActive);
        }

        #endregion

        #region Work Item Tests

        [Fact]
        public async Task MultiGPU_SingleWorkItem()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            var workItem = new TestWorkItem("test1");
            var result = await orchestrator.ExecuteSingleAsync(workItem);

            Assert.NotNull(result);
            Assert.Contains("Completed on GPU 0", result.ToString());
        }

        [Fact]
        public async Task MultiGPU_MultipleWorkItems()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Add multiple work items
            orchestrator.AddWorkItem(new TestWorkItem("work1"));
            orchestrator.AddWorkItem(new TestWorkItem("work2"));
            orchestrator.AddWorkItem(new TestWorkItem("work3"));

            var result = await orchestrator.ExecuteAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ParticipatingGPUs);
            Assert.True(result.TotalExecutionTime > TimeSpan.Zero);
            Assert.True(result.GPUResults.ContainsKey(0));
        }

        [Fact]
        public async Task MultiGPU_PriorityWorkItems()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Add items with different priorities
            orchestrator.AddWorkItem(new TestWorkItem("low1", 50) { }); // Default priority 0
            orchestrator.AddWorkItem(new HighPriorityWorkItem("high1")); // Priority 10
            orchestrator.AddWorkItem(new TestWorkItem("low2", 50) { }); // Default priority 0

            var result = await orchestrator.ExecuteAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ParticipatingGPUs);
        }

        [Fact]
        public async Task MultiGPU_MemoryIntensiveWorkItems()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            var workItem = new MemoryIntensiveWorkItem("memory_test");
            
            // This might fail if GPU doesn't have enough memory, which is expected
            try
            {
                var result = await orchestrator.ExecuteSingleAsync(workItem);
                Assert.NotNull(result);
            }
            catch (InvalidOperationException ex)
            {
                Assert.Contains("No suitable GPU found", ex.Message);
            }
        }

        #endregion

        #region Distribution Strategy Tests

        [Fact]
        public async Task MultiGPU_RoundRobinDistribution()
        {
            var accelerators = new[] { accelerator };
            var options = new MultiGPUOptions
            {
                DistributionStrategy = WorkDistributionStrategy.RoundRobin,
                EnableLoadBalancing = false
            };
            using var orchestrator = new MultiGPUOrchestrator(accelerators, options);

            // Add multiple work items
            for (int i = 0; i < 5; i++)
            {
                orchestrator.AddWorkItem(new TestWorkItem($"work{i}"));
            }

            var result = await orchestrator.ExecuteAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ParticipatingGPUs);
        }

        [Fact]
        public async Task MultiGPU_PerformanceBasedDistribution()
        {
            var accelerators = new[] { accelerator };
            var options = new MultiGPUOptions
            {
                DistributionStrategy = WorkDistributionStrategy.PerformanceBased,
                EnableLoadBalancing = false
            };
            using var orchestrator = new MultiGPUOrchestrator(accelerators, options);

            // Add multiple work items
            for (int i = 0; i < 5; i++)
            {
                orchestrator.AddWorkItem(new TestWorkItem($"work{i}"));
            }

            var result = await orchestrator.ExecuteAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ParticipatingGPUs);
        }

        [Fact]
        public async Task MultiGPU_LoadBasedDistribution()
        {
            var accelerators = new[] { accelerator };
            var options = new MultiGPUOptions
            {
                DistributionStrategy = WorkDistributionStrategy.LoadBased,
                EnableLoadBalancing = false
            };
            using var orchestrator = new MultiGPUOrchestrator(accelerators, options);

            // Add multiple work items
            for (int i = 0; i < 5; i++)
            {
                orchestrator.AddWorkItem(new TestWorkItem($"work{i}"));
            }

            var result = await orchestrator.ExecuteAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ParticipatingGPUs);
        }

        #endregion

        #region Synchronization Tests

        [Fact]
        public async Task MultiGPU_SynchronizationModes()
        {
            var accelerators = new[] { accelerator };
            
            // Test different synchronization modes
            var modes = new[]
            {
                SynchronizationMode.None,
                SynchronizationMode.PerKernel,
                SynchronizationMode.PerBatch,
                SynchronizationMode.Explicit
            };

            foreach (var mode in modes)
            {
                var options = new MultiGPUOptions
                {
                    SynchronizationMode = mode,
                    EnableLoadBalancing = false
                };
                using var orchestrator = new MultiGPUOrchestrator(accelerators, options);

                orchestrator.AddWorkItem(new TestWorkItem("sync_test"));
                var result = await orchestrator.ExecuteAsync();

                Assert.True(result.IsSuccess);
            }
        }

        [Fact]
        public async Task MultiGPU_ExplicitSynchronization()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Test explicit synchronization
            await orchestrator.SynchronizeAsync();
            
            // Should complete without errors
            Assert.True(true);
        }

        #endregion

        #region Array Distribution Tests

        [Fact]
        public async Task MultiGPU_ArrayDistribution()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            var inputData = Enumerable.Range(0, 1000).ToArray();
            
            // Simple processor that doubles each element
            var result = await orchestrator.DistributeArrayAsync(
                inputData,
                async (chunk, gpu, cancellationToken) =>
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false); // Simulate processing
                    return chunk.Select(x => x * 2).ToArray();
                });

            Assert.Equal(inputData.Length, result.Length);
            for (int i = 0; i < inputData.Length; i++)
            {
                Assert.Equal(inputData[i] * 2, result[i]);
            }
        }

        [Fact]
        public async Task MultiGPU_LargeArrayDistribution()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            const int size = 10000;
            var inputData = new float[size];
            for (int i = 0; i < size; i++)
            {
                inputData[i] = i * 0.5f;
            }
            
            // Processor that applies a mathematical function
            var result = await orchestrator.DistributeArrayAsync(
                inputData,
                async (chunk, gpu, cancellationToken) =>
                {
                    // Use GPU for actual computation
                    using var buffer = gpu.accelerator.Allocate1D(chunk);
                    using var resultBuffer = gpu.accelerator.Allocate1D<float>(chunk.Length);
                    
                    var kernel = gpu.accelerator.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                        (index, input, output) => output[index] = input[index] * input[index] + 1.0f);
                    
                    kernel(gpu.accelerator.DefaultStream, (Index1D)chunk.Length, buffer.View, resultBuffer.View);
                    gpu.accelerator.Synchronize();
                    
                    return resultBuffer.GetAsArray1D();
                });

            Assert.Equal(size, result.Length);
            
            // Verify results
            for (int i = 0; i < Math.Min(100, size); i++) // Check first 100 elements
            {
                var expected = inputData[i] * inputData[i] + 1.0f;
                Assert.Equal(expected, result[i], 3);
            }
        }

        #endregion

        #region Performance and Statistics Tests

        [Fact]
        public async Task MultiGPU_LoadStatistics()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Initially, load should be zero
            var initialStats = orchestrator.GetLoadStatistics();
            Assert.Equal(0.0, initialStats[0]);

            // Execute some work
            orchestrator.AddWorkItem(new TestWorkItem("load_test"));
            await orchestrator.ExecuteAsync();

            // Load should have been updated during execution
            var finalStats = orchestrator.GetLoadStatistics();
            Assert.True(finalStats.ContainsKey(0));
        }

        [Fact]
        public async Task MultiGPU_PerformanceMetrics()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Execute some work to generate metrics
            for (int i = 0; i < 5; i++)
            {
                orchestrator.AddWorkItem(new TestWorkItem($"metrics_test_{i}"));
            }
            await orchestrator.ExecuteAsync();

            var metrics = orchestrator.GetPerformanceMetrics();

            Assert.True(metrics.TotalOperations > 0);
            Assert.True(metrics.TotalThroughput >= 0);
            Assert.True(metrics.AverageExecutionTime >= TimeSpan.Zero);
            Assert.True(metrics.GPUUtilization.ContainsKey(0));
            Assert.True(metrics.MemoryUtilization.ContainsKey(0));
        }

        [Fact]
        public async Task MultiGPU_LoadBalancing()
        {
            var accelerators = new[] { accelerator };
            var options = new MultiGPUOptions
            {
                EnableLoadBalancing = true,
                LoadBalancingInterval = 50 // Short interval for testing
            };
            using var orchestrator = new MultiGPUOrchestrator(accelerators, options);

            // Execute load balancing
            await orchestrator.BalanceLoadAsync();
            
            // Should complete without errors
            Assert.True(true);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task MultiGPU_NoActiveGPUs()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Disable all GPUs
            orchestrator.SetGPUEnabled(0, false);

            orchestrator.AddWorkItem(new TestWorkItem("no_gpu_test"));
            var result = await orchestrator.ExecuteAsync();

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
            Assert.Contains("No active GPUs available", result.Error.Message);
        }

        [Fact]
        public async Task MultiGPU_CancellationToken()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(50); // Cancel after 50ms

            orchestrator.AddWorkItem(new TestWorkItem("cancel_test", 1000)); // Long running task

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await orchestrator.ExecuteAsync(cts.Token).ConfigureAwait(false);
            });
        }

        [Fact]
        public async Task MultiGPU_WorkItemException()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            // Create a work item that throws an exception
            var faultyWorkItem = new TestWorkItem("faulty")
            {
                EstimatedExecutionTime = 0
            };

            // Override ExecuteAsync to throw
            var workItem = new FaultyWorkItem("exception_test");
            
            orchestrator.AddWorkItem(workItem);
            var result = await orchestrator.ExecuteAsync();

            // Should still succeed overall, but individual item should fail
            Assert.True(result.IsSuccess);
            Assert.True(result.GPUResults[0].ToString()!.Contains("Failed"));
        }

        #endregion

        #region Concurrency Tests

        [Fact]
        public async Task MultiGPU_ConcurrentExecution()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            const int taskCount = 10;
            var tasks = new Task[taskCount];

            for (int i = 0; i < taskCount; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    var workItem = new TestWorkItem($"concurrent_{taskId}", 50);
                    await orchestrator.ExecuteSingleAsync(workItem).ConfigureAwait(false);
                });
            }

            // All tasks should complete successfully
            await Task.WhenAll(tasks);
            Assert.True(true);
        }

        [Fact]
        public async Task MultiGPU_StressTest()
        {
            var accelerators = new[] { accelerator };
            using var orchestrator = new MultiGPUOrchestrator(accelerators);

            const int workItemCount = 50;
            
            // Add many work items
            for (int i = 0; i < workItemCount; i++)
            {
                orchestrator.AddWorkItem(new TestWorkItem($"stress_{i}", 10)); // Short tasks
            }

            var result = await orchestrator.ExecuteAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ParticipatingGPUs);
            
            var metrics = orchestrator.GetPerformanceMetrics();
            Assert.Equal(workItemCount, metrics.TotalOperations);
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Work item that throws an exception for testing error handling.
        /// </summary>
        private class FaultyWorkItem : MultiGPUWorkItem
        {
            public FaultyWorkItem(string id) : base(id)
            {
            }

            public override Task ExecuteAsync(GPUInfo gpu, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Test exception from work item");
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes of the test resources.
        /// </summary>
        public void Dispose()
        {
            accelerator?.Dispose();
            context?.Dispose();
        }

        #endregion
    }
}
