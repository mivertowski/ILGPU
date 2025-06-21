// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: ProfilingContexts.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ILGPU.Runtime.Profiling
{
    /// <summary>
    /// Implementation of kernel profiling context.
    /// </summary>
    internal sealed class KernelProfilingContext : IKernelProfilingContext
    {
        private readonly PerformanceProfiler profiler;
        private readonly string executionId;
        private readonly Stopwatch stopwatch;
        private readonly Dictionary<string, object> metadata = new();
        private TimeSpan compilationTime;
        private int sharedMemorySize;
        private int registerCount;
        private bool disposed;

        public KernelProfilingContext(PerformanceProfiler profiler, string executionId, string kernelName, Index3D gridSize, Index3D groupSize)
        {
            this.profiler = profiler;
            this.executionId = executionId;
            KernelName = kernelName;
            GridSize = gridSize;
            GroupSize = groupSize;
            StartTime = DateTime.UtcNow;
            stopwatch = Stopwatch.StartNew();
        }

        public string KernelName { get; }
        public Index3D GridSize { get; }
        public Index3D GroupSize { get; }
        public DateTime StartTime { get; }

        public void RecordCompilation(TimeSpan compilationTime) => this.compilationTime = compilationTime;

        public void RecordLaunchParameters(int sharedMemorySize, int registerCount)
        {
            this.sharedMemorySize = sharedMemorySize;
            this.registerCount = registerCount;
        }

        public void RecordExecution(TimeSpan executionTime, double? throughput = null)
        {
            if (disposed)
                return;

            var endTime = StartTime.Add(executionTime);
            
            var record = new KernelExecutionRecord
            {
                ExecutionId = executionId,
                KernelName = KernelName,
                StartTime = StartTime,
                EndTime = endTime,
                CompilationTime = compilationTime,
                ExecutionTime = executionTime,
                GridSize = GridSize,
                GroupSize = GroupSize,
                SharedMemorySize = sharedMemorySize,
                RegisterCount = registerCount,
                Throughput = throughput,
                WasFromCache = compilationTime == TimeSpan.Zero,
                Metadata = new Dictionary<string, object>(metadata)
            };

            profiler.CompleteKernelExecution(executionId, record);
        }

        public void AddMetadata(string key, object value) => metadata[key] = value;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            stopwatch.Stop();

            // If execution wasn't explicitly recorded, record it now
            if (!disposed)
            {
                RecordExecution(stopwatch.Elapsed);
            }
        }
    }

    /// <summary>
    /// Implementation of memory operation profiling context.
    /// </summary>
    internal sealed class MemoryProfilingContext : IMemoryProfilingContext
    {
        private readonly PerformanceProfiler profiler;
        private readonly string operationId;
        private readonly string source;
        private readonly string destination;
        private readonly Stopwatch stopwatch;
        private bool disposed;

        public MemoryProfilingContext(
            PerformanceProfiler profiler, 
            string operationId, 
            MemoryOperationType operationType, 
            long sizeInBytes,
            string source,
            string destination)
        {
            this.profiler = profiler;
            this.operationId = operationId;
            this.source = source;
            this.destination = destination;
            OperationType = operationType;
            SizeInBytes = sizeInBytes;
            StartTime = DateTime.UtcNow;
            stopwatch = Stopwatch.StartNew();
        }

        public MemoryOperationType OperationType { get; }
        public long SizeInBytes { get; }
        public DateTime StartTime { get; }

        public void RecordCompletion(TimeSpan actualDuration, double? bandwidth = null)
        {
            if (disposed)
                return;

            var endTime = StartTime.Add(actualDuration);
            
            var record = new MemoryOperationRecord
            {
                OperationId = operationId,
                OperationType = OperationType,
                StartTime = StartTime,
                EndTime = endTime,
                SizeInBytes = SizeInBytes,
                Bandwidth = bandwidth,
                Source = source,
                Destination = destination,
                WasFromPool = source.Contains("pool", StringComparison.OrdinalIgnoreCase) || 
                             destination.Contains("pool", StringComparison.OrdinalIgnoreCase)
            };

            profiler.CompleteMemoryOperation(operationId, record);
        }

        public void RecordError(Exception error)
        {
            if (disposed)
                return;

            var endTime = DateTime.UtcNow;
            
            var record = new MemoryOperationRecord
            {
                OperationId = operationId,
                OperationType = OperationType,
                StartTime = StartTime,
                EndTime = endTime,
                SizeInBytes = SizeInBytes,
                Source = source,
                Destination = destination,
                Error = error.Message
            };

            profiler.CompleteMemoryOperation(operationId, record);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            stopwatch.Stop();

            // If completion wasn't explicitly recorded, record it now
            RecordCompletion(stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// No-operation kernel profiling context for when profiling is disabled.
    /// </summary>
    public sealed class NoOpKernelProfilingContext : IKernelProfilingContext
    {
        public string KernelName => "";
        public DateTime StartTime => DateTime.MinValue;

        public void RecordCompilation(TimeSpan compilationTime) { }
        public void RecordLaunchParameters(int sharedMemorySize, int registerCount) { }
        public void RecordExecution(TimeSpan executionTime, double? throughput = null) { }
        public void AddMetadata(string key, object value) { }
        public void Dispose() { }
    }

    /// <summary>
    /// No-operation memory profiling context for when profiling is disabled.
    /// </summary>
    public sealed class NoOpMemoryProfilingContext : IMemoryProfilingContext
    {
        public MemoryOperationType OperationType => MemoryOperationType.Allocation;
        public long SizeInBytes => 0;
        public DateTime StartTime => DateTime.MinValue;

        public void RecordCompletion(TimeSpan actualDuration, double? bandwidth = null) { }
        public void RecordError(Exception error) { }
        public void Dispose() { }
    }
}
