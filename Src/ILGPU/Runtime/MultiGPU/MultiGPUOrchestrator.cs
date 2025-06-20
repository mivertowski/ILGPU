// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MultiGPUOrchestrator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.MultiGPU
{
    /// <summary>
    /// Advanced multi-GPU orchestrator with load balancing and work distribution.
    /// </summary>
    public sealed class MultiGPUOrchestrator : IMultiGPUOrchestrator
    {
        #region Fields

        private readonly List<GPUInfo> availableGPUs;
        private readonly ConcurrentQueue<MultiGPUWorkItem> workQueue;
        private readonly MultiGPUOptions options;
        private readonly Timer? loadBalancingTimer;
        private readonly ConcurrentDictionary<int, double> loadStats;
        private readonly object lockObject = new();

        private long totalOperations;
        private TimeSpan totalExecutionTime;
        private readonly List<TimeSpan> executionTimes = new();
        private bool disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiGPUOrchestrator"/> class.
        /// </summary>
        /// <param name="accelerators">The available accelerators.</param>
        /// <param name="options">Orchestration options.</param>
        public MultiGPUOrchestrator(IEnumerable<Accelerator> accelerators, MultiGPUOptions? options = null)
        {
            if (accelerators == null)
                throw new ArgumentNullException(nameof(accelerators));

            this.options = options ?? new MultiGPUOptions();
            workQueue = new ConcurrentQueue<MultiGPUWorkItem>();
            loadStats = new ConcurrentDictionary<int, double>();

            // Initialize GPU information
            availableGPUs = new List<GPUInfo>();
            int index = 0;
            foreach (var accelerator in accelerators)
            {
                if (this.options.MaxGPUs.HasValue && index >= this.options.MaxGPUs.Value)
                    break;

                var performanceScore = CalculatePerformanceScore(accelerator);
                var gpuInfo = new GPUInfo(accelerator, index, performanceScore);
                availableGPUs.Add(gpuInfo);
                loadStats[index] = 0.0;
                index++;
            }

            // Start load balancing timer if enabled
            if (this.options.EnableLoadBalancing && availableGPUs.Count > 1)
            {
                loadBalancingTimer = new Timer(
                    LoadBalancingCallback,
                    null,
                    TimeSpan.FromMilliseconds(this.options.LoadBalancingInterval),
                    TimeSpan.FromMilliseconds(this.options.LoadBalancingInterval));
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the available GPUs.
        /// </summary>
        public IReadOnlyList<GPUInfo> AvailableGPUs => availableGPUs.AsReadOnly();

        /// <summary>
        /// Gets the active GPUs.
        /// </summary>
        public IReadOnlyList<GPUInfo> ActiveGPUs => availableGPUs.Where(g => g.IsActive).ToList().AsReadOnly();

        /// <summary>
        /// Gets the orchestration options.
        /// </summary>
        public MultiGPUOptions Options => options;

        #endregion

        #region IMultiGPUOrchestrator Implementation

        /// <summary>
        /// Adds a work item to the execution queue.
        /// </summary>
        /// <param name="workItem">The work item to add.</param>
        public void AddWorkItem(MultiGPUWorkItem workItem)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));
            if (workItem == null)
                throw new ArgumentNullException(nameof(workItem));

            workQueue.Enqueue(workItem);
        }

        /// <summary>
        /// Executes all queued work items across multiple GPUs.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the execution with results.</returns>
        public async Task<MultiGPUResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));

            var stopwatch = Stopwatch.StartNew();
            var gpuResults = new Dictionary<int, object>();
            var activeGpus = ActiveGPUs.ToList();

            if (activeGpus.Count == 0)
            {
                return new MultiGPUResult(false, TimeSpan.Zero, gpuResults, 
                    new InvalidOperationException("No active GPUs available"));
            }

            try
            {
                // Group work items by priority and distribute
                var workItems = new List<MultiGPUWorkItem>();
                while (workQueue.TryDequeue(out var item))
                {
                    workItems.Add(item);
                }

                if (workItems.Count == 0)
                {
                    return new MultiGPUResult(true, stopwatch.Elapsed, gpuResults);
                }

                // Sort by priority (higher first)
                workItems = workItems.OrderByDescending(w => w.Priority).ToList();

                // Distribute work according to strategy
                var gpuWorkAssignments = DistributeWork(workItems, activeGpus);

                // Execute work on each GPU in parallel
                var tasks = new List<Task>();
                foreach (var assignment in gpuWorkAssignments)
                {
                    var gpu = assignment.Key;
                    var items = assignment.Value;
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        var results = new List<object>();
                        foreach (var item in items)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            try
                            {
                                UpdateGPULoad(gpu.Index, 1.0);
                                await item.ExecuteAsync(gpu, cancellationToken);
                                results.Add($"Completed: {item.Id}");
                                Interlocked.Increment(ref totalOperations);
                            }
                            catch (Exception ex)
                            {
                                results.Add($"Failed: {item.Id} - {ex.Message}");
                            }
                            finally
                            {
                                UpdateGPULoad(gpu.Index, 0.0);
                            }

                            // Synchronize if required
                            if (options.SynchronizationMode == SynchronizationMode.PerKernel)
                            {
                                gpu.Accelerator.Synchronize();
                            }
                        }
                        
                        lock (lockObject)
                        {
                            gpuResults[gpu.Index] = results;
                        }

                        // Batch synchronization
                        if (options.SynchronizationMode == SynchronizationMode.PerBatch)
                        {
                            gpu.Accelerator.Synchronize();
                        }
                    }, cancellationToken));
                }

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);

                // Final synchronization if needed
                if (options.SynchronizationMode == SynchronizationMode.PerBatch ||
                    options.SynchronizationMode == SynchronizationMode.PerKernel)
                {
                    await SynchronizeAsync(cancellationToken);
                }

                stopwatch.Stop();
                
                lock (lockObject)
                {
                    totalExecutionTime = totalExecutionTime.Add(stopwatch.Elapsed);
                    executionTimes.Add(stopwatch.Elapsed);
                    if (executionTimes.Count > 1000)
                    {
                        executionTimes.RemoveAt(0);
                    }
                }

                return new MultiGPUResult(true, stopwatch.Elapsed, gpuResults);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new MultiGPUResult(false, stopwatch.Elapsed, gpuResults, ex);
            }
        }

        /// <summary>
        /// Executes a single work item with optimal GPU selection.
        /// </summary>
        /// <param name="workItem">The work item to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the execution.</returns>
        public async Task<object> ExecuteSingleAsync(MultiGPUWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));
            if (workItem == null)
                throw new ArgumentNullException(nameof(workItem));

            var optimalGpu = SelectOptimalGPU(workItem);
            if (optimalGpu == null)
            {
                throw new InvalidOperationException("No suitable GPU found for the work item");
            }

            try
            {
                UpdateGPULoad(optimalGpu.Index, 1.0);
                await workItem.ExecuteAsync(optimalGpu, cancellationToken);
                Interlocked.Increment(ref totalOperations);
                return $"Completed on GPU {optimalGpu.Index}";
            }
            finally
            {
                UpdateGPULoad(optimalGpu.Index, 0.0);
            }
        }

        /// <summary>
        /// Distributes an array across multiple GPUs for parallel processing.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="data">The data to distribute.</param>
        /// <param name="processor">The processing function.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the distributed processing.</returns>
        public async Task<T[]> DistributeArrayAsync<T>(
            T[] data,
            Func<T[], GPUInfo, CancellationToken, Task<T[]>> processor,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (processor == null)
                throw new ArgumentNullException(nameof(processor));

            var activeGpus = ActiveGPUs.ToList();
            if (activeGpus.Count == 0)
            {
                throw new InvalidOperationException("No active GPUs available");
            }

            // Calculate chunk sizes based on GPU performance
            var chunkSizes = CalculateChunkSizes(data.Length, activeGpus);
            var chunks = new List<T[]>();
            var tasks = new List<Task<T[]>>();

            int offset = 0;
            for (int i = 0; i < activeGpus.Count && offset < data.Length; i++)
            {
                var chunkSize = Math.Min(chunkSizes[i], data.Length - offset);
                var chunk = new T[chunkSize];
                Array.Copy(data, offset, chunk, 0, chunkSize);
                chunks.Add(chunk);

                var gpu = activeGpus[i];
                var chunkCopy = chunk; // Capture for lambda
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        UpdateGPULoad(gpu.Index, 1.0);
                        return await processor(chunkCopy, gpu, cancellationToken);
                    }
                    finally
                    {
                        UpdateGPULoad(gpu.Index, 0.0);
                    }
                }, cancellationToken));

                offset += chunkSize;
            }

            // Wait for all chunks to be processed
            var results = await Task.WhenAll(tasks);

            // Combine results
            var totalLength = results.Sum(r => r.Length);
            var combinedResult = new T[totalLength];
            offset = 0;
            foreach (var result in results)
            {
                Array.Copy(result, 0, combinedResult, offset, result.Length);
                offset += result.Length;
            }

            return combinedResult;
        }

        /// <summary>
        /// Synchronizes all active GPUs.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the synchronization.</returns>
        public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));

            var tasks = ActiveGPUs.Select(gpu => Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                gpu.Accelerator.Synchronize();
            }, cancellationToken));

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Gets the current load statistics for all GPUs.
        /// </summary>
        /// <returns>Load statistics for each GPU.</returns>
        public Dictionary<int, double> GetLoadStatistics()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));

            return new Dictionary<int, double>(loadStats);
        }

        /// <summary>
        /// Gets performance metrics for the orchestrator.
        /// </summary>
        /// <returns>Performance metrics.</returns>
        public MultiGPUPerformanceMetrics GetPerformanceMetrics()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));

            lock (lockObject)
            {
                var avgExecutionTime = executionTimes.Count > 0 
                    ? TimeSpan.FromTicks((long)executionTimes.Average(t => t.Ticks))
                    : TimeSpan.Zero;

                var throughput = totalExecutionTime.TotalSeconds > 0 
                    ? totalOperations / totalExecutionTime.TotalSeconds 
                    : 0.0;

                var gpuUtilization = new Dictionary<int, double>();
                var memoryUtilization = new Dictionary<int, double>();

                foreach (var gpu in availableGPUs)
                {
                    gpuUtilization[gpu.Index] = gpu.CurrentLoad;
                    
                    var memInfo = gpu.MemoryInfo;
                    var utilization = memInfo.TotalMemory > 0 
                        ? (double)(memInfo.TotalMemory - memInfo.AvailableMemory) / memInfo.TotalMemory 
                        : 0.0;
                    memoryUtilization[gpu.Index] = utilization;
                }

                return new MultiGPUPerformanceMetrics(
                    totalOperations,
                    avgExecutionTime,
                    throughput,
                    gpuUtilization,
                    memoryUtilization);
            }
        }

        /// <summary>
        /// Enables or disables a specific GPU.
        /// </summary>
        /// <param name="gpuIndex">The GPU index.</param>
        /// <param name="enabled">Whether to enable the GPU.</param>
        public void SetGPUEnabled(int gpuIndex, bool enabled)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));

            var gpu = availableGPUs.FirstOrDefault(g => g.Index == gpuIndex);
            if (gpu != null)
            {
                gpu.IsActive = enabled;
                if (!enabled)
                {
                    UpdateGPULoad(gpuIndex, 0.0);
                }
            }
        }

        /// <summary>
        /// Balances the load across all active GPUs.
        /// </summary>
        /// <returns>A task representing the load balancing operation.</returns>
        public async Task BalanceLoadAsync()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MultiGPUOrchestrator));

            // This is a simplified load balancing implementation
            // In practice, this would involve more sophisticated algorithms
            var activeGpus = ActiveGPUs.ToList();
            var averageLoad = activeGpus.Count > 0 ? activeGpus.Average(g => g.CurrentLoad) : 0.0;

            foreach (var gpu in activeGpus)
            {
                if (gpu.CurrentLoad > averageLoad * 1.5)
                {
                    // GPU is overloaded - could implement work redistribution here
                    // For now, just log this condition
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Calculates a performance score for an accelerator.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <returns>The performance score.</returns>
        private static double CalculatePerformanceScore(Accelerator accelerator)
        {
            // Simple performance scoring based on memory and compute units
            var memoryScore = accelerator.Memory.TotalMemory / (1024.0 * 1024.0 * 1024.0); // GB
            var computeScore = accelerator.MaxNumThreadsPerGroup * accelerator.MaxNumThreadsPerMultiprocessor;
            
            return (memoryScore * 0.3) + (computeScore * 0.7);
        }

        /// <summary>
        /// Distributes work items across GPUs based on the configured strategy.
        /// </summary>
        /// <param name="workItems">The work items to distribute.</param>
        /// <param name="gpus">The available GPUs.</param>
        /// <returns>Work assignments for each GPU.</returns>
        private Dictionary<GPUInfo, List<MultiGPUWorkItem>> DistributeWork(
            List<MultiGPUWorkItem> workItems,
            List<GPUInfo> gpus)
        {
            var assignments = new Dictionary<GPUInfo, List<MultiGPUWorkItem>>();
            foreach (var gpu in gpus)
            {
                assignments[gpu] = new List<MultiGPUWorkItem>();
            }

            switch (options.DistributionStrategy)
            {
                case WorkDistributionStrategy.RoundRobin:
                    DistributeRoundRobin(workItems, gpus, assignments);
                    break;
                case WorkDistributionStrategy.PerformanceBased:
                    DistributePerformanceBased(workItems, gpus, assignments);
                    break;
                case WorkDistributionStrategy.LoadBased:
                    DistributeLoadBased(workItems, gpus, assignments);
                    break;
                default:
                    DistributeRoundRobin(workItems, gpus, assignments);
                    break;
            }

            return assignments;
        }

        /// <summary>
        /// Distributes work items using round-robin strategy.
        /// </summary>
        private static void DistributeRoundRobin(
            List<MultiGPUWorkItem> workItems,
            List<GPUInfo> gpus,
            Dictionary<GPUInfo, List<MultiGPUWorkItem>> assignments)
        {
            for (int i = 0; i < workItems.Count; i++)
            {
                var gpu = gpus[i % gpus.Count];
                if (workItems[i].CanExecuteOn(gpu))
                {
                    assignments[gpu].Add(workItems[i]);
                }
            }
        }

        /// <summary>
        /// Distributes work items based on GPU performance.
        /// </summary>
        private static void DistributePerformanceBased(
            List<MultiGPUWorkItem> workItems,
            List<GPUInfo> gpus,
            Dictionary<GPUInfo, List<MultiGPUWorkItem>> assignments)
        {
            var totalPerformance = gpus.Sum(g => g.PerformanceScore);
            var gpuWeights = gpus.ToDictionary(g => g, g => g.PerformanceScore / totalPerformance);

            var gpuWorkCounts = gpus.ToDictionary(g => g, g => 0);
            
            foreach (var workItem in workItems)
            {
                var bestGpu = gpus
                    .Where(g => workItem.CanExecuteOn(g))
                    .OrderBy(g => gpuWorkCounts[g] / gpuWeights[g])
                    .FirstOrDefault();

                if (bestGpu != null)
                {
                    assignments[bestGpu].Add(workItem);
                    gpuWorkCounts[bestGpu]++;
                }
            }
        }

        /// <summary>
        /// Distributes work items based on current GPU load.
        /// </summary>
        private void DistributeLoadBased(
            List<MultiGPUWorkItem> workItems,
            List<GPUInfo> gpus,
            Dictionary<GPUInfo, List<MultiGPUWorkItem>> assignments)
        {
            foreach (var workItem in workItems)
            {
                var bestGpu = gpus
                    .Where(g => workItem.CanExecuteOn(g))
                    .OrderBy(g => g.CurrentLoad)
                    .FirstOrDefault();

                if (bestGpu != null)
                {
                    assignments[bestGpu].Add(workItem);
                }
            }
        }

        /// <summary>
        /// Selects the optimal GPU for a work item.
        /// </summary>
        /// <param name="workItem">The work item.</param>
        /// <returns>The optimal GPU or null if none suitable.</returns>
        private GPUInfo? SelectOptimalGPU(MultiGPUWorkItem workItem)
        {
            var candidates = ActiveGPUs.Where(g => workItem.CanExecuteOn(g)).ToList();
            if (candidates.Count == 0)
                return null;

            return options.DistributionStrategy switch
            {
                WorkDistributionStrategy.PerformanceBased => candidates.OrderByDescending(g => g.PerformanceScore).First(),
                WorkDistributionStrategy.LoadBased => candidates.OrderBy(g => g.CurrentLoad).First(),
                _ => candidates.First()
            };
        }

        /// <summary>
        /// Calculates chunk sizes for array distribution.
        /// </summary>
        /// <param name="totalSize">The total array size.</param>
        /// <param name="gpus">The available GPUs.</param>
        /// <returns>Chunk sizes for each GPU.</returns>
        private int[] CalculateChunkSizes(int totalSize, List<GPUInfo> gpus)
        {
            var totalPerformance = gpus.Sum(g => g.PerformanceScore);
            var chunkSizes = new int[gpus.Count];
            int remaining = totalSize;

            for (int i = 0; i < gpus.Count - 1; i++)
            {
                var proportion = gpus[i].PerformanceScore / totalPerformance;
                chunkSizes[i] = (int)(totalSize * proportion);
                remaining -= chunkSizes[i];
            }

            // Last GPU gets remaining elements
            chunkSizes[gpus.Count - 1] = remaining;

            return chunkSizes;
        }

        /// <summary>
        /// Updates the load for a specific GPU.
        /// </summary>
        /// <param name="gpuIndex">The GPU index.</param>
        /// <param name="load">The new load value.</param>
        private void UpdateGPULoad(int gpuIndex, double load)
        {
            loadStats[gpuIndex] = load;
            var gpu = availableGPUs.FirstOrDefault(g => g.Index == gpuIndex);
            if (gpu != null)
            {
                gpu.CurrentLoad = load;
            }
        }

        /// <summary>
        /// Timer callback for load balancing.
        /// </summary>
        /// <param name="state">Timer state (unused).</param>
        private async void LoadBalancingCallback(object? state)
        {
            try
            {
                await BalanceLoadAsync();
            }
            catch (Exception)
            {
                // Log error but don't throw from timer callback
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases all resources used by the <see cref="MultiGPUOrchestrator"/>.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                loadBalancingTimer?.Dispose();
                
                // Clear work queue
                while (workQueue.TryDequeue(out _)) { }
                
                disposed = true;
            }
        }

        #endregion
    }
}