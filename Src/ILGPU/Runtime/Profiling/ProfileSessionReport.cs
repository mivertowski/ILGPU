// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: ProfileSessionReport.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace ILGPU.Runtime.Profiling
{
    /// <summary>
    /// Represents a comprehensive profiling session report.
    /// </summary>
    public sealed record ProfileSessionReport
    {
        /// <summary>
        /// Gets the session ID.
        /// </summary>
        public string SessionId { get; init; } = "";

        /// <summary>
        /// Gets the session name.
        /// </summary>
        public string SessionName { get; init; } = "";

        /// <summary>
        /// Gets the session start time.
        /// </summary>
        public DateTime StartTime { get; init; }

        /// <summary>
        /// Gets the session end time.
        /// </summary>
        public DateTime EndTime { get; init; }

        /// <summary>
        /// Gets the total session duration.
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Gets the performance metrics for this session.
        /// </summary>
        public PerformanceMetrics Metrics { get; init; } = new();

        /// <summary>
        /// Gets detailed kernel execution records.
        /// </summary>
        public List<KernelExecutionRecord> KernelExecutions { get; init; } = new();

        /// <summary>
        /// Gets detailed memory operation records.
        /// </summary>
        public List<MemoryOperationRecord> MemoryOperations { get; init; } = new();

        /// <summary>
        /// Gets custom event records.
        /// </summary>
        public List<CustomEventRecord> CustomEvents { get; init; } = new();

        /// <summary>
        /// Gets system information at the time of profiling.
        /// </summary>
        public SystemInformation SystemInfo { get; init; } = new();

        /// <summary>
        /// Gets accelerator information.
        /// </summary>
        public AcceleratorInformation AcceleratorInfo { get; init; } = new();

        /// <summary>
        /// Gets session-level metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();

        /// <summary>
        /// Gets performance recommendations based on the profiling data.
        /// </summary>
        public List<PerformanceRecommendation> Recommendations { get; init; } = new();
    }

    /// <summary>
    /// Represents a detailed record of a kernel execution.
    /// </summary>
    public sealed record KernelExecutionRecord
    {
        /// <summary>
        /// Gets the unique execution ID.
        /// </summary>
        public string ExecutionId { get; init; } = "";

        /// <summary>
        /// Gets the kernel name.
        /// </summary>
        public string KernelName { get; init; } = "";

        /// <summary>
        /// Gets the execution start time.
        /// </summary>
        public DateTime StartTime { get; init; }

        /// <summary>
        /// Gets the execution end time.
        /// </summary>
        public DateTime EndTime { get; init; }

        /// <summary>
        /// Gets the compilation time.
        /// </summary>
        public TimeSpan CompilationTime { get; init; }

        /// <summary>
        /// Gets the actual execution time.
        /// </summary>
        public TimeSpan ExecutionTime { get; init; }

        /// <summary>
        /// Gets the grid size used for execution.
        /// </summary>
        public Index3D GridSize { get; init; }

        /// <summary>
        /// Gets the group size used for execution.
        /// </summary>
        public Index3D GroupSize { get; init; }

        /// <summary>
        /// Gets the total number of threads executed.
        /// </summary>
        public long TotalThreads => GridSize.Size * GroupSize.Size;

        /// <summary>
        /// Gets the shared memory size used.
        /// </summary>
        public int SharedMemorySize { get; init; }

        /// <summary>
        /// Gets the register count per thread.
        /// </summary>
        public int RegisterCount { get; init; }

        /// <summary>
        /// Gets the throughput in operations per second.
        /// </summary>
        public double? Throughput { get; init; }

        /// <summary>
        /// Gets whether the kernel was compiled from cache.
        /// </summary>
        public bool WasFromCache { get; init; }

        /// <summary>
        /// Gets custom metadata for this execution.
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();

        /// <summary>
        /// Gets any errors that occurred during execution.
        /// </summary>
        public string? Error { get; init; }
    }

    /// <summary>
    /// Represents a detailed record of a memory operation.
    /// </summary>
    public sealed record MemoryOperationRecord
    {
        /// <summary>
        /// Gets the unique operation ID.
        /// </summary>
        public string OperationId { get; init; } = "";

        /// <summary>
        /// Gets the operation type.
        /// </summary>
        public MemoryOperationType OperationType { get; init; }

        /// <summary>
        /// Gets the operation start time.
        /// </summary>
        public DateTime StartTime { get; init; }

        /// <summary>
        /// Gets the operation end time.
        /// </summary>
        public DateTime EndTime { get; init; }

        /// <summary>
        /// Gets the operation duration.
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Gets the size of the operation in bytes.
        /// </summary>
        public long SizeInBytes { get; init; }

        /// <summary>
        /// Gets the bandwidth achieved in bytes per second.
        /// </summary>
        public double? Bandwidth { get; init; }

        /// <summary>
        /// Gets the source of the operation.
        /// </summary>
        public string Source { get; init; } = "";

        /// <summary>
        /// Gets the destination of the operation.
        /// </summary>
        public string Destination { get; init; } = "";

        /// <summary>
        /// Gets whether the operation was served from a memory pool.
        /// </summary>
        public bool WasFromPool { get; init; }

        /// <summary>
        /// Gets any error that occurred during the operation.
        /// </summary>
        public string? Error { get; init; }

        /// <summary>
        /// Gets custom metadata for this operation.
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();
    }

    /// <summary>
    /// Represents a custom event record.
    /// </summary>
    public sealed record CustomEventRecord
    {
        /// <summary>
        /// Gets the event ID.
        /// </summary>
        public string EventId { get; init; } = "";

        /// <summary>
        /// Gets the event name.
        /// </summary>
        public string EventName { get; init; } = "";

        /// <summary>
        /// Gets the event timestamp.
        /// </summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// Gets the event duration.
        /// </summary>
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Gets the event metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();
    }

    /// <summary>
    /// Contains system information at the time of profiling.
    /// </summary>
    public sealed record SystemInformation
    {
        /// <summary>
        /// Gets the operating system information.
        /// </summary>
        public string OperatingSystem { get; init; } = "";

        /// <summary>
        /// Gets the .NET runtime version.
        /// </summary>
        public string RuntimeVersion { get; init; } = "";

        /// <summary>
        /// Gets the ILGPU version.
        /// </summary>
        public string ILGPUVersion { get; init; } = "";

        /// <summary>
        /// Gets the total system memory in bytes.
        /// </summary>
        public long TotalSystemMemory { get; init; }

        /// <summary>
        /// Gets the available system memory in bytes.
        /// </summary>
        public long AvailableSystemMemory { get; init; }

        /// <summary>
        /// Gets the processor count.
        /// </summary>
        public int ProcessorCount { get; init; }

        /// <summary>
        /// Gets whether the process is running in 64-bit mode.
        /// </summary>
        public bool Is64BitProcess { get; init; }
    }

    /// <summary>
    /// Contains accelerator information.
    /// </summary>
    public sealed record AcceleratorInformation
    {
        /// <summary>
        /// Gets the accelerator type.
        /// </summary>
        public AcceleratorType AcceleratorType { get; init; }

        /// <summary>
        /// Gets the accelerator name.
        /// </summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// Gets the device memory size in bytes.
        /// </summary>
        public long DeviceMemorySize { get; init; }

        /// <summary>
        /// Gets the maximum grid size.
        /// </summary>
        public Index3D MaxGridSize { get; init; }

        /// <summary>
        /// Gets the maximum group size.
        /// </summary>
        public Index3D MaxGroupSize { get; init; }

        /// <summary>
        /// Gets the warp size.
        /// </summary>
        public int WarpSize { get; init; }

        /// <summary>
        /// Gets accelerator-specific capabilities.
        /// </summary>
        public Dictionary<string, object> Capabilities { get; init; } = new();
    }

    /// <summary>
    /// Represents a performance recommendation.
    /// </summary>
    public sealed record PerformanceRecommendation
    {
        /// <summary>
        /// Gets the recommendation category.
        /// </summary>
        public RecommendationCategory Category { get; init; }

        /// <summary>
        /// Gets the recommendation priority.
        /// </summary>
        public RecommendationPriority Priority { get; init; }

        /// <summary>
        /// Gets the recommendation title.
        /// </summary>
        public string Title { get; init; } = "";

        /// <summary>
        /// Gets the recommendation description.
        /// </summary>
        public string Description { get; init; } = "";

        /// <summary>
        /// Gets specific suggestions for improvement.
        /// </summary>
        public List<string> Suggestions { get; init; } = new();

        /// <summary>
        /// Gets the estimated performance impact.
        /// </summary>
        public string EstimatedImpact { get; init; } = "";

        /// <summary>
        /// Gets related metrics that support this recommendation.
        /// </summary>
        public Dictionary<string, object> SupportingMetrics { get; init; } = new();
    }

    /// <summary>
    /// Represents recommendation categories.
    /// </summary>
    public enum RecommendationCategory
    {
        /// <summary>
        /// Kernel optimization recommendations.
        /// </summary>
        KernelOptimization,

        /// <summary>
        /// Memory access pattern recommendations.
        /// </summary>
        MemoryOptimization,

        /// <summary>
        /// Launch configuration recommendations.
        /// </summary>
        LaunchConfiguration,

        /// <summary>
        /// Resource utilization recommendations.
        /// </summary>
        ResourceUtilization,

        /// <summary>
        /// Algorithmic improvements.
        /// </summary>
        Algorithm,

        /// <summary>
        /// Configuration recommendations.
        /// </summary>
        Configuration
    }

    /// <summary>
    /// Represents recommendation priorities.
    /// </summary>
    public enum RecommendationPriority
    {
        /// <summary>
        /// Low priority recommendation.
        /// </summary>
        Low,

        /// <summary>
        /// Medium priority recommendation.
        /// </summary>
        Medium,

        /// <summary>
        /// High priority recommendation.
        /// </summary>
        High,

        /// <summary>
        /// Critical recommendation.
        /// </summary>
        Critical
    }
}
