// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: PerformanceMetrics.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace ILGPU.Runtime.Profiling
{
    /// <summary>
    /// Contains comprehensive performance metrics for GPU operations.
    /// </summary>
    public sealed record PerformanceMetrics
    {
        /// <summary>
        /// Gets the session start time.
        /// </summary>
        public DateTime SessionStartTime { get; init; }

        /// <summary>
        /// Gets the total session duration.
        /// </summary>
        public TimeSpan SessionDuration { get; init; }

        /// <summary>
        /// Gets kernel execution metrics.
        /// </summary>
        public KernelMetrics Kernels { get; init; } = new();

        /// <summary>
        /// Gets memory operation metrics.
        /// </summary>
        public MemoryMetrics Memory { get; init; } = new();

        /// <summary>
        /// Gets accelerator utilization metrics.
        /// </summary>
        public AcceleratorMetrics Accelerator { get; init; } = new();

        /// <summary>
        /// Gets custom event metrics.
        /// </summary>
        public CustomEventMetrics CustomEvents { get; init; } = new();
    }

    /// <summary>
    /// Contains metrics specific to kernel executions.
    /// </summary>
    public sealed record KernelMetrics
    {
        /// <summary>
        /// Gets the total number of kernels executed.
        /// </summary>
        public int TotalKernels { get; init; }

        /// <summary>
        /// Gets the total kernel execution time.
        /// </summary>
        public TimeSpan TotalExecutionTime { get; init; }

        /// <summary>
        /// Gets the average kernel execution time.
        /// </summary>
        public TimeSpan AverageExecutionTime { get; init; }

        /// <summary>
        /// Gets the fastest kernel execution time.
        /// </summary>
        public TimeSpan FastestExecution { get; init; }

        /// <summary>
        /// Gets the slowest kernel execution time.
        /// </summary>
        public TimeSpan SlowestExecution { get; init; }

        /// <summary>
        /// Gets the total compilation time.
        /// </summary>
        public TimeSpan TotalCompilationTime { get; init; }

        /// <summary>
        /// Gets the average throughput in operations per second.
        /// </summary>
        public double AverageThroughput { get; init; }

        /// <summary>
        /// Gets kernel execution statistics by kernel name.
        /// </summary>
        public Dictionary<string, KernelStatistics> KernelStats { get; init; } = new();

        /// <summary>
        /// Gets the total number of threads executed.
        /// </summary>
        public long TotalThreadsExecuted { get; init; }

        /// <summary>
        /// Gets the cache hit ratio for compiled kernels.
        /// </summary>
        public double CompilationCacheHitRatio { get; init; }
    }

    /// <summary>
    /// Contains metrics specific to memory operations.
    /// </summary>
    public sealed record MemoryMetrics
    {
        /// <summary>
        /// Gets the total number of memory operations.
        /// </summary>
        public int TotalOperations { get; init; }

        /// <summary>
        /// Gets the total bytes transferred.
        /// </summary>
        public long TotalBytesTransferred { get; init; }

        /// <summary>
        /// Gets the average memory bandwidth in bytes per second.
        /// </summary>
        public double AverageBandwidth { get; init; }

        /// <summary>
        /// Gets the peak memory bandwidth.
        /// </summary>
        public double PeakBandwidth { get; init; }

        /// <summary>
        /// Gets the total memory allocation time.
        /// </summary>
        public TimeSpan TotalAllocationTime { get; init; }

        /// <summary>
        /// Gets the total memory copy time.
        /// </summary>
        public TimeSpan TotalCopyTime { get; init; }

        /// <summary>
        /// Gets memory operation statistics by operation type.
        /// </summary>
        public Dictionary<MemoryOperationType, MemoryOperationStatistics> OperationStats { get; init; } = new();

        /// <summary>
        /// Gets the memory pool hit ratio if pooling is enabled.
        /// </summary>
        public double PoolHitRatio { get; init; }

        /// <summary>
        /// Gets the total number of memory allocations.
        /// </summary>
        public int TotalAllocations { get; init; }

        /// <summary>
        /// Gets the total number of memory deallocations.
        /// </summary>
        public int TotalDeallocations { get; init; }

        /// <summary>
        /// Gets the current memory usage in bytes.
        /// </summary>
        public long CurrentMemoryUsage { get; init; }

        /// <summary>
        /// Gets the peak memory usage in bytes.
        /// </summary>
        public long PeakMemoryUsage { get; init; }
    }

    /// <summary>
    /// Contains metrics specific to accelerator utilization.
    /// </summary>
    public sealed record AcceleratorMetrics
    {
        /// <summary>
        /// Gets the accelerator utilization percentage.
        /// </summary>
        public double UtilizationPercentage { get; init; }

        /// <summary>
        /// Gets the total idle time.
        /// </summary>
        public TimeSpan TotalIdleTime { get; init; }

        /// <summary>
        /// Gets the total active time.
        /// </summary>
        public TimeSpan TotalActiveTime { get; init; }

        /// <summary>
        /// Gets the number of context switches.
        /// </summary>
        public int ContextSwitches { get; init; }

        /// <summary>
        /// Gets the number of stream synchronizations.
        /// </summary>
        public int StreamSynchronizations { get; init; }

        /// <summary>
        /// Gets the average stream utilization.
        /// </summary>
        public double AverageStreamUtilization { get; init; }

        /// <summary>
        /// Gets accelerator-specific metrics.
        /// </summary>
        public Dictionary<string, object> AcceleratorSpecificMetrics { get; init; } = new();
    }

    /// <summary>
    /// Contains metrics for custom events.
    /// </summary>
    public sealed record CustomEventMetrics
    {
        /// <summary>
        /// Gets the total number of custom events.
        /// </summary>
        public int TotalEvents { get; init; }

        /// <summary>
        /// Gets custom event statistics by event name.
        /// </summary>
        public Dictionary<string, EventStatistics> EventStats { get; init; } = new();
    }

    /// <summary>
    /// Contains detailed statistics for a specific kernel.
    /// </summary>
    public sealed record KernelStatistics
    {
        /// <summary>
        /// Gets the kernel name.
        /// </summary>
        public string KernelName { get; init; } = "";

        /// <summary>
        /// Gets the number of executions.
        /// </summary>
        public int ExecutionCount { get; init; }

        /// <summary>
        /// Gets the total execution time.
        /// </summary>
        public TimeSpan TotalExecutionTime { get; init; }

        /// <summary>
        /// Gets the average execution time.
        /// </summary>
        public TimeSpan AverageExecutionTime { get; init; }

        /// <summary>
        /// Gets the minimum execution time.
        /// </summary>
        public TimeSpan MinExecutionTime { get; init; }

        /// <summary>
        /// Gets the maximum execution time.
        /// </summary>
        public TimeSpan MaxExecutionTime { get; init; }

        /// <summary>
        /// Gets the standard deviation of execution times.
        /// </summary>
        public TimeSpan ExecutionTimeStdDev { get; init; }

        /// <summary>
        /// Gets the total compilation time.
        /// </summary>
        public TimeSpan TotalCompilationTime { get; init; }

        /// <summary>
        /// Gets the average throughput.
        /// </summary>
        public double AverageThroughput { get; init; }

        /// <summary>
        /// Gets the total number of threads executed.
        /// </summary>
        public long TotalThreads { get; init; }

        /// <summary>
        /// Gets custom metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();
    }

    /// <summary>
    /// Contains statistics for memory operations.
    /// </summary>
    public sealed record MemoryOperationStatistics
    {
        /// <summary>
        /// Gets the operation type.
        /// </summary>
        public MemoryOperationType OperationType { get; init; }

        /// <summary>
        /// Gets the number of operations.
        /// </summary>
        public int OperationCount { get; init; }

        /// <summary>
        /// Gets the total bytes transferred.
        /// </summary>
        public long TotalBytes { get; init; }

        /// <summary>
        /// Gets the total operation time.
        /// </summary>
        public TimeSpan TotalTime { get; init; }

        /// <summary>
        /// Gets the average operation time.
        /// </summary>
        public TimeSpan AverageTime { get; init; }

        /// <summary>
        /// Gets the average bandwidth.
        /// </summary>
        public double AverageBandwidth { get; init; }

        /// <summary>
        /// Gets the peak bandwidth.
        /// </summary>
        public double PeakBandwidth { get; init; }

        /// <summary>
        /// Gets the number of failed operations.
        /// </summary>
        public int FailedOperations { get; init; }
    }

    /// <summary>
    /// Contains statistics for custom events.
    /// </summary>
    public sealed record EventStatistics
    {
        /// <summary>
        /// Gets the event name.
        /// </summary>
        public string EventName { get; init; } = "";

        /// <summary>
        /// Gets the number of occurrences.
        /// </summary>
        public int Count { get; init; }

        /// <summary>
        /// Gets the total duration.
        /// </summary>
        public TimeSpan TotalDuration { get; init; }

        /// <summary>
        /// Gets the average duration.
        /// </summary>
        public TimeSpan AverageDuration { get; init; }

        /// <summary>
        /// Gets the minimum duration.
        /// </summary>
        public TimeSpan MinDuration { get; init; }

        /// <summary>
        /// Gets the maximum duration.
        /// </summary>
        public TimeSpan MaxDuration { get; init; }

        /// <summary>
        /// Gets aggregated metadata.
        /// </summary>
        public Dictionary<string, object> AggregatedMetadata { get; init; } = new();
    }
}