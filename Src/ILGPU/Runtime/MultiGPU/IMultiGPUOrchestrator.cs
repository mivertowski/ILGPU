// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: IMultiGPUOrchestrator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.MultiGPU
{
    /// <summary>
    /// Represents the strategy for distributing work across multiple GPUs.
    /// </summary>
    public enum WorkDistributionStrategy
    {
        /// <summary>
        /// Distribute work equally across all available GPUs.
        /// </summary>
        RoundRobin,

        /// <summary>
        /// Distribute work based on GPU performance characteristics.
        /// </summary>
        PerformanceBased,

        /// <summary>
        /// Distribute work based on current GPU utilization.
        /// </summary>
        LoadBased,

        /// <summary>
        /// Use custom distribution logic.
        /// </summary>
        Custom
    }

    /// <summary>
    /// Represents the synchronization mode for multi-GPU operations.
    /// </summary>
    public enum SynchronizationMode
    {
        /// <summary>
        /// No synchronization between GPUs.
        /// </summary>
        None,

        /// <summary>
        /// Synchronize after each kernel launch.
        /// </summary>
        PerKernel,

        /// <summary>
        /// Synchronize after each batch of operations.
        /// </summary>
        PerBatch,

        /// <summary>
        /// Synchronize only at explicit barriers.
        /// </summary>
        Explicit
    }

    /// <summary>
    /// Configuration options for multi-GPU orchestration.
    /// </summary>
    public sealed class MultiGPUOptions
    {
        /// <summary>
        /// Gets or sets the work distribution strategy (default: PerformanceBased).
        /// </summary>
        public WorkDistributionStrategy DistributionStrategy { get; set; } = WorkDistributionStrategy.PerformanceBased;

        /// <summary>
        /// Gets or sets the synchronization mode (default: PerBatch).
        /// </summary>
        public SynchronizationMode SynchronizationMode { get; set; } = SynchronizationMode.PerBatch;

        /// <summary>
        /// Gets or sets whether to enable automatic load balancing (default: true).
        /// </summary>
        public bool EnableLoadBalancing { get; set; } = true;

        /// <summary>
        /// Gets or sets the load balancing interval in milliseconds (default: 100).
        /// </summary>
        public int LoadBalancingInterval { get; set; } = 100;

        /// <summary>
        /// Gets or sets whether to enable automatic memory management (default: true).
        /// </summary>
        public bool EnableAutoMemoryManagement { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of GPUs to use (default: all available).
        /// </summary>
        public int? MaxGPUs { get; set; }

        /// <summary>
        /// Gets or sets whether to enable GPU affinity optimization (default: true).
        /// </summary>
        public bool EnableAffinityOptimization { get; set; } = true;

        /// <summary>
        /// Gets or sets the timeout for GPU operations in milliseconds (default: 30000).
        /// </summary>
        public int OperationTimeout { get; set; } = 30000;
    }

    /// <summary>
    /// Represents information about a GPU in the multi-GPU setup.
    /// </summary>
    public sealed class GPUInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GPUInfo"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="index">The GPU index.</param>
        /// <param name="performanceScore">The performance score.</param>
        public GPUInfo(Accelerator accelerator, int index, double performanceScore)
        {
            Accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            Index = index;
            PerformanceScore = performanceScore;
            CurrentLoad = 0.0;
            IsActive = true;
        }

        /// <summary>
        /// Gets the accelerator for this GPU.
        /// </summary>
        public Accelerator Accelerator { get; }

        /// <summary>
        /// Gets the GPU index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the performance score (higher is better).
        /// </summary>
        public double PerformanceScore { get; }

        /// <summary>
        /// Gets or sets the current load percentage (0.0 to 1.0).
        /// </summary>
        public double CurrentLoad { get; set; }

        /// <summary>
        /// Gets or sets whether this GPU is currently active.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets the device name.
        /// </summary>
        public string DeviceName => Accelerator.Device.Name;

        /// <summary>
        /// Gets the memory information.
        /// </summary>
        public MemoryInfo MemoryInfo => Accelerator.Memory;
    }

    /// <summary>
    /// Represents a work item to be distributed across GPUs.
    /// </summary>
    public abstract class MultiGPUWorkItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiGPUWorkItem"/> class.
        /// </summary>
        /// <param name="id">The work item ID.</param>
        /// <param name="priority">The priority.</param>
        protected MultiGPUWorkItem(string id, int priority = 0)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Priority = priority;
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the work item ID.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the priority (higher values = higher priority).
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Gets the creation timestamp.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Gets or sets the estimated execution time in milliseconds.
        /// </summary>
        public double EstimatedExecutionTime { get; set; }

        /// <summary>
        /// Gets or sets the memory requirements in bytes.
        /// </summary>
        public long MemoryRequirement { get; set; }

        /// <summary>
        /// Executes the work item on the specified GPU.
        /// </summary>
        /// <param name="gpu">The GPU to execute on.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the execution.</returns>
        public abstract Task ExecuteAsync(GPUInfo gpu, CancellationToken cancellationToken);

        /// <summary>
        /// Determines if this work item can be executed on the specified GPU.
        /// </summary>
        /// <param name="gpu">The GPU to check.</param>
        /// <returns>True if the work item can be executed on the GPU.</returns>
        public virtual bool CanExecuteOn(GPUInfo gpu) => gpu.IsActive && gpu.MemoryInfo.TotalMemory >= MemoryRequirement;
    }

    /// <summary>
    /// Represents the result of a multi-GPU operation.
    /// </summary>
    public sealed class MultiGPUResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiGPUResult"/> class.
        /// </summary>
        /// <param name="isSuccess">Whether the operation was successful.</param>
        /// <param name="totalExecutionTime">The total execution time.</param>
        /// <param name="gpuResults">Results from individual GPUs.</param>
        /// <param name="error">Error information if failed.</param>
        public MultiGPUResult(
            bool isSuccess,
            TimeSpan totalExecutionTime,
            Dictionary<int, object> gpuResults,
            Exception? error = null)
        {
            IsSuccess = isSuccess;
            TotalExecutionTime = totalExecutionTime;
            GPUResults = gpuResults ?? new Dictionary<int, object>();
            Error = error;
        }

        /// <summary>
        /// Gets a value indicating whether the operation was successful.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// Gets the total execution time.
        /// </summary>
        public TimeSpan TotalExecutionTime { get; }

        /// <summary>
        /// Gets the results from individual GPUs.
        /// </summary>
        public Dictionary<int, object> GPUResults { get; }

        /// <summary>
        /// Gets the error information if the operation failed.
        /// </summary>
        public Exception? Error { get; }

        /// <summary>
        /// Gets the number of GPUs that participated in the operation.
        /// </summary>
        public int ParticipatingGPUs => GPUResults.Count;
    }

    /// <summary>
    /// Defines the interface for multi-GPU orchestration.
    /// </summary>
    public interface IMultiGPUOrchestrator : IDisposable
    {
        /// <summary>
        /// Gets the available GPUs.
        /// </summary>
        IReadOnlyList<GPUInfo> AvailableGPUs { get; }

        /// <summary>
        /// Gets the active GPUs.
        /// </summary>
        IReadOnlyList<GPUInfo> ActiveGPUs { get; }

        /// <summary>
        /// Gets the orchestration options.
        /// </summary>
        MultiGPUOptions Options { get; }

        /// <summary>
        /// Adds a work item to the execution queue.
        /// </summary>
        /// <param name="workItem">The work item to add.</param>
        void AddWorkItem(MultiGPUWorkItem workItem);

        /// <summary>
        /// Executes all queued work items across multiple GPUs.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the execution with results.</returns>
        Task<MultiGPUResult> ExecuteAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a single work item with optimal GPU selection.
        /// </summary>
        /// <param name="workItem">The work item to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the execution.</returns>
        Task<object> ExecuteSingleAsync(MultiGPUWorkItem workItem, CancellationToken cancellationToken = default);

        /// <summary>
        /// Distributes an array across multiple GPUs for parallel processing.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="data">The data to distribute.</param>
        /// <param name="processor">The processing function.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the distributed processing.</returns>
        Task<T[]> DistributeArrayAsync<T>(
            T[] data,
            Func<T[], GPUInfo, CancellationToken, Task<T[]>> processor,
            CancellationToken cancellationToken = default)
            where T : unmanaged;

        /// <summary>
        /// Synchronizes all active GPUs.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the synchronization.</returns>
        Task SynchronizeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current load statistics for all GPUs.
        /// </summary>
        /// <returns>Load statistics for each GPU.</returns>
        Dictionary<int, double> GetLoadStatistics();

        /// <summary>
        /// Gets performance metrics for the orchestrator.
        /// </summary>
        /// <returns>Performance metrics.</returns>
        MultiGPUPerformanceMetrics GetPerformanceMetrics();

        /// <summary>
        /// Enables or disables a specific GPU.
        /// </summary>
        /// <param name="gpuIndex">The GPU index.</param>
        /// <param name="enabled">Whether to enable the GPU.</param>
        void SetGPUEnabled(int gpuIndex, bool enabled);

        /// <summary>
        /// Balances the load across all active GPUs.
        /// </summary>
        /// <returns>A task representing the load balancing operation.</returns>
        Task BalanceLoadAsync();
    }

    /// <summary>
    /// Performance metrics for multi-GPU operations.
    /// </summary>
    public sealed class MultiGPUPerformanceMetrics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiGPUPerformanceMetrics"/> class.
        /// </summary>
        /// <param name="totalOperations">Total operations executed.</param>
        /// <param name="averageExecutionTime">Average execution time.</param>
        /// <param name="totalThroughput">Total throughput (operations/second).</param>
        /// <param name="gpuUtilization">GPU utilization percentages.</param>
        /// <param name="memoryUtilization">Memory utilization percentages.</param>
        public MultiGPUPerformanceMetrics(
            long totalOperations,
            TimeSpan averageExecutionTime,
            double totalThroughput,
            Dictionary<int, double> gpuUtilization,
            Dictionary<int, double> memoryUtilization)
        {
            TotalOperations = totalOperations;
            AverageExecutionTime = averageExecutionTime;
            TotalThroughput = totalThroughput;
            GPUUtilization = gpuUtilization ?? new Dictionary<int, double>();
            MemoryUtilization = memoryUtilization ?? new Dictionary<int, double>();
        }

        /// <summary>
        /// Gets the total number of operations executed.
        /// </summary>
        public long TotalOperations { get; }

        /// <summary>
        /// Gets the average execution time per operation.
        /// </summary>
        public TimeSpan AverageExecutionTime { get; }

        /// <summary>
        /// Gets the total throughput in operations per second.
        /// </summary>
        public double TotalThroughput { get; }

        /// <summary>
        /// Gets the GPU utilization percentages.
        /// </summary>
        public Dictionary<int, double> GPUUtilization { get; }

        /// <summary>
        /// Gets the memory utilization percentages.
        /// </summary>
        public Dictionary<int, double> MemoryUtilization { get; }
    }
}