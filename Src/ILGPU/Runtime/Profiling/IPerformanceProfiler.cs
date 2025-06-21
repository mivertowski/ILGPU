// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: IPerformanceProfiler.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.Profiling
{
    /// <summary>
    /// Represents a comprehensive performance profiler for ILGPU operations.
    /// </summary>
    public interface IPerformanceProfiler : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether profiling is currently enabled.
        /// </summary>
        bool IsProfilingEnabled { get; }

        /// <summary>
        /// Gets the current profiling session ID.
        /// </summary>
        string CurrentSessionId { get; }

        /// <summary>
        /// Starts a new profiling session.
        /// </summary>
        /// <param name="sessionName">The name of the session.</param>
        /// <param name="sessionId">Optional custom session ID.</param>
        /// <returns>The session ID.</returns>
        string StartSession(string sessionName, string? sessionId = null);

        /// <summary>
        /// Ends the current profiling session.
        /// </summary>
        /// <returns>The session report.</returns>
        ProfileSessionReport EndSession();

        /// <summary>
        /// Starts profiling a kernel execution.
        /// </summary>
        /// <param name="kernelName">The name of the kernel.</param>
        /// <param name="gridSize">The grid size.</param>
        /// <param name="groupSize">The group size.</param>
        /// <returns>A profiling context.</returns>
        IKernelProfilingContext StartKernelProfiling(string kernelName, Index3D gridSize, Index3D groupSize);

        /// <summary>
        /// Starts profiling a memory operation.
        /// </summary>
        /// <param name="operationType">The type of memory operation.</param>
        /// <param name="sizeInBytes">The size of the operation in bytes.</param>
        /// <param name="source">The source of the operation.</param>
        /// <param name="destination">The destination of the operation.</param>
        /// <returns>A profiling context.</returns>
        IMemoryProfilingContext StartMemoryProfiling(
            MemoryOperationType operationType,
            long sizeInBytes,
            string source = "",
            string destination = "");

        /// <summary>
        /// Records a custom performance event.
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="duration">The duration of the event.</param>
        /// <param name="metadata">Optional metadata.</param>
        void RecordEvent(string eventName, TimeSpan duration, Dictionary<string, object>? metadata = null);

        /// <summary>
        /// Gets performance metrics for the current session.
        /// </summary>
        /// <returns>Current performance metrics.</returns>
        PerformanceMetrics GetCurrentMetrics();

        /// <summary>
        /// Gets all completed session reports.
        /// </summary>
        /// <returns>List of session reports.</returns>
        IReadOnlyList<ProfileSessionReport> GetSessionReports();

        /// <summary>
        /// Exports profiling data to a file.
        /// </summary>
        /// <param name="filePath">The file path to export to.</param>
        /// <param name="format">The export format.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the export operation.</returns>
        Task ExportAsync(string filePath, ProfileExportFormat format, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears all profiling data.
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// Represents a profiling context for kernel executions.
    /// </summary>
    public interface IKernelProfilingContext : IDisposable
    {
        /// <summary>
        /// Gets the kernel name.
        /// </summary>
        string KernelName { get; }

        /// <summary>
        /// Gets the start time.
        /// </summary>
        DateTime StartTime { get; }

        /// <summary>
        /// Records the completion of kernel compilation.
        /// </summary>
        /// <param name="compilationTime">The compilation time.</param>
        void RecordCompilation(TimeSpan compilationTime);

        /// <summary>
        /// Records kernel launch parameters.
        /// </summary>
        /// <param name="sharedMemorySize">The shared memory size.</param>
        /// <param name="registerCount">The register count per thread.</param>
        void RecordLaunchParameters(int sharedMemorySize, int registerCount);

        /// <summary>
        /// Records kernel execution completion.
        /// </summary>
        /// <param name="executionTime">The execution time.</param>
        /// <param name="throughput">Optional throughput information.</param>
        void RecordExecution(TimeSpan executionTime, double? throughput = null);

        /// <summary>
        /// Adds custom metadata to the profiling context.
        /// </summary>
        /// <param name="key">The metadata key.</param>
        /// <param name="value">The metadata value.</param>
        void AddMetadata(string key, object value);
    }

    /// <summary>
    /// Represents a profiling context for memory operations.
    /// </summary>
    public interface IMemoryProfilingContext : IDisposable
    {
        /// <summary>
        /// Gets the operation type.
        /// </summary>
        MemoryOperationType OperationType { get; }

        /// <summary>
        /// Gets the operation size in bytes.
        /// </summary>
        long SizeInBytes { get; }

        /// <summary>
        /// Gets the start time.
        /// </summary>
        DateTime StartTime { get; }

        /// <summary>
        /// Records the completion of the memory operation.
        /// </summary>
        /// <param name="actualDuration">The actual duration of the operation.</param>
        /// <param name="bandwidth">Optional bandwidth measurement in bytes/second.</param>
        void RecordCompletion(TimeSpan actualDuration, double? bandwidth = null);

        /// <summary>
        /// Records an error during the memory operation.
        /// </summary>
        /// <param name="error">The error that occurred.</param>
        void RecordError(Exception error);
    }

    /// <summary>
    /// Represents the type of memory operation being profiled.
    /// </summary>
    public enum MemoryOperationType
    {
        /// <summary>
        /// Memory allocation operation.
        /// </summary>
        Allocation,

        /// <summary>
        /// Memory deallocation operation.
        /// </summary>
        Deallocation,

        /// <summary>
        /// Host to device copy operation.
        /// </summary>
        HostToDevice,

        /// <summary>
        /// Device to host copy operation.
        /// </summary>
        DeviceToHost,

        /// <summary>
        /// Device to device copy operation.
        /// </summary>
        DeviceToDevice,

        /// <summary>
        /// Memory set operation.
        /// </summary>
        MemorySet,

        /// <summary>
        /// Memory clear operation.
        /// </summary>
        MemoryClear
    }

    /// <summary>
    /// Represents the export format for profiling data.
    /// </summary>
    public enum ProfileExportFormat
    {
        /// <summary>
        /// JSON format.
        /// </summary>
        Json,

        /// <summary>
        /// CSV format.
        /// </summary>
        Csv,

        /// <summary>
        /// Chrome tracing format.
        /// </summary>
        ChromeTracing,

        /// <summary>
        /// Binary format for fast loading.
        /// </summary>
        Binary
    }
}
