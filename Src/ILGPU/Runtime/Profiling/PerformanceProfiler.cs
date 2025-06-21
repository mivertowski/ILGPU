// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: PerformanceProfiler.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime.Profiling
{
    /// <summary>
    /// Default implementation of the performance profiler.
    /// </summary>
    public sealed class PerformanceProfiler : IPerformanceProfiler
    {
        #region Static Fields

        /// <summary>
        /// Cached JSON serializer options for export operations.
        /// </summary>
        private static readonly JsonSerializerOptions JsonExportOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        #endregion

        #region Fields

        private readonly object sessionLock = new();
        private readonly ConcurrentBag<ProfileSessionReport> completedSessions = new();
        private readonly ConcurrentDictionary<string, KernelExecutionRecord> activeKernelExecutions = new();
        private readonly ConcurrentDictionary<string, MemoryOperationRecord> activeMemoryOperations = new();
        private readonly List<KernelExecutionRecord> currentKernelExecutions = new();
        private readonly List<MemoryOperationRecord> currentMemoryOperations = new();
        private readonly List<CustomEventRecord> currentCustomEvents = new();
        private readonly Accelerator accelerator;
        
        private volatile bool isProfilingEnabled;
        private volatile bool disposed;
        private string currentSessionId = "";
        private string currentSessionName = "";
        private DateTime currentSessionStart;
        private long totalExecutions;
        private long totalMemoryOperations;

        /// <summary>
        /// Initializes a new instance of the <see cref="PerformanceProfiler"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator to profile.</param>
        /// <param name="enabledByDefault">Whether profiling is enabled by default.</param>
        public PerformanceProfiler(Accelerator accelerator, bool enabledByDefault = false)
        {
            this.accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            isProfilingEnabled = enabledByDefault;
        }

        /// <inheritdoc/>
        public bool IsProfilingEnabled => isProfilingEnabled;

        /// <inheritdoc/>
        public string CurrentSessionId => currentSessionId;

        /// <inheritdoc/>
        public string StartSession(string sessionName, string? sessionId = null)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(PerformanceProfiler));

            lock (sessionLock)
            {
                // End current session if active
                if (isProfilingEnabled && !string.IsNullOrEmpty(currentSessionId))
                {
                    EndSession();
                }

                currentSessionId = sessionId ?? Guid.NewGuid().ToString();
                currentSessionName = sessionName ?? "Unnamed Session";
                currentSessionStart = DateTime.UtcNow;
                
                // Clear current session data
                currentKernelExecutions.Clear();
                currentMemoryOperations.Clear();
                currentCustomEvents.Clear();
                activeKernelExecutions.Clear();
                activeMemoryOperations.Clear();
                
                isProfilingEnabled = true;
                
                return currentSessionId;
            }
        }

        /// <inheritdoc/>
        public ProfileSessionReport EndSession()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(PerformanceProfiler));

            lock (sessionLock)
            {
                if (!isProfilingEnabled || string.IsNullOrEmpty(currentSessionId))
                    throw new InvalidOperationException("No active profiling session");

                var endTime = DateTime.UtcNow;
                
                // Wait for any active operations to complete
                WaitForActiveOperations();

                var report = CreateSessionReport(endTime);
                completedSessions.Add(report);

                // Reset session state
                isProfilingEnabled = false;
                currentSessionId = "";
                currentSessionName = "";

                return report;
            }
        }

        /// <inheritdoc/>
        public IKernelProfilingContext StartKernelProfiling(string kernelName, Index3D gridSize, Index3D groupSize)
        {
            if (!isProfilingEnabled || disposed)
                return new NoOpKernelProfilingContext();

            var executionId = $"kernel_{Interlocked.Increment(ref totalExecutions)}";
            var context = new KernelProfilingContext(this, executionId, kernelName, gridSize, groupSize);
            
            var record = new KernelExecutionRecord
            {
                ExecutionId = executionId,
                KernelName = kernelName,
                StartTime = DateTime.UtcNow,
                GridSize = gridSize,
                GroupSize = groupSize
            };

            activeKernelExecutions.TryAdd(executionId, record);
            return context;
        }

        /// <inheritdoc/>
        public IMemoryProfilingContext StartMemoryProfiling(
            MemoryOperationType operationType,
            long sizeInBytes,
            string source = "",
            string destination = "")
        {
            if (!isProfilingEnabled || disposed)
                return new NoOpMemoryProfilingContext();

            var operationId = $"memory_{Interlocked.Increment(ref totalMemoryOperations)}";
            var context = new MemoryProfilingContext(this, operationId, operationType, sizeInBytes, source, destination);
            
            var record = new MemoryOperationRecord
            {
                OperationId = operationId,
                OperationType = operationType,
                StartTime = DateTime.UtcNow,
                SizeInBytes = sizeInBytes,
                Source = source,
                Destination = destination
            };

            activeMemoryOperations.TryAdd(operationId, record);
            return context;
        }

        /// <inheritdoc/>
        public void RecordEvent(string eventName, TimeSpan duration, Dictionary<string, object>? metadata = null)
        {
            if (!isProfilingEnabled || disposed)
                return;

            var eventRecord = new CustomEventRecord
            {
                EventId = Guid.NewGuid().ToString(),
                EventName = eventName,
                Timestamp = DateTime.UtcNow,
                Duration = duration,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            lock (currentCustomEvents)
            {
                currentCustomEvents.Add(eventRecord);
            }
        }

        /// <inheritdoc/>
        public PerformanceMetrics GetCurrentMetrics()
        {
            if (!isProfilingEnabled)
                return new PerformanceMetrics();

            lock (sessionLock)
            {
                return CalculateMetrics(currentKernelExecutions, currentMemoryOperations, currentCustomEvents);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<ProfileSessionReport> GetSessionReports() => completedSessions.ToList();

        /// <inheritdoc/>
        public async Task ExportAsync(string filePath, ProfileExportFormat format, CancellationToken cancellationToken = default)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(PerformanceProfiler));

            var reports = GetSessionReports();
            
            switch (format)
            {
                case ProfileExportFormat.Json:
                    await ExportToJsonAsync(filePath, reports, cancellationToken).ConfigureAwait(false);
                    break;
                case ProfileExportFormat.Csv:
                    await ExportToCsvAsync(filePath, reports, cancellationToken).ConfigureAwait(false);
                    break;
                case ProfileExportFormat.ChromeTracing:
                    await ExportToChromeTracingAsync(filePath, reports, cancellationToken).ConfigureAwait(false);
                    break;
                case ProfileExportFormat.Binary:
                    await ExportToBinaryAsync(filePath, reports, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException($"Unsupported export format: {format}");
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            lock (sessionLock)
            {
                completedSessions.Clear();
                currentKernelExecutions.Clear();
                currentMemoryOperations.Clear();
                currentCustomEvents.Clear();
                activeKernelExecutions.Clear();
                activeMemoryOperations.Clear();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            try
            {
                if (isProfilingEnabled && !string.IsNullOrEmpty(currentSessionId))
                {
                    EndSession();
                }
            }
            catch
            {
                // Ignore errors during disposal
            }

            Clear();
        }

        internal void CompleteKernelExecution(string executionId, KernelExecutionRecord completedRecord)
        {
            if (activeKernelExecutions.TryRemove(executionId, out _))
            {
                lock (currentKernelExecutions)
                {
                    currentKernelExecutions.Add(completedRecord);
                }
            }
        }

        internal void CompleteMemoryOperation(string operationId, MemoryOperationRecord completedRecord)
        {
            if (activeMemoryOperations.TryRemove(operationId, out _))
            {
                lock (currentMemoryOperations)
                {
                    currentMemoryOperations.Add(completedRecord);
                }
            }
        }

        private void WaitForActiveOperations()
        {
            // Wait for active operations to complete with a timeout
            var timeout = TimeSpan.FromSeconds(5);
            var stopwatch = Stopwatch.StartNew();

            while ((activeKernelExecutions.Count > 0 || activeMemoryOperations.Count > 0) && 
                   stopwatch.Elapsed < timeout)
            {
                Thread.Sleep(10);
            }
        }

        private ProfileSessionReport CreateSessionReport(DateTime endTime)
        {
            var metrics = CalculateMetrics(currentKernelExecutions, currentMemoryOperations, currentCustomEvents);
            var recommendations = GenerateRecommendations(metrics, currentKernelExecutions, currentMemoryOperations);

            return new ProfileSessionReport
            {
                SessionId = currentSessionId,
                SessionName = currentSessionName,
                StartTime = currentSessionStart,
                EndTime = endTime,
                Metrics = metrics,
                KernelExecutions = new List<KernelExecutionRecord>(currentKernelExecutions),
                MemoryOperations = new List<MemoryOperationRecord>(currentMemoryOperations),
                CustomEvents = new List<CustomEventRecord>(currentCustomEvents),
                SystemInfo = GetSystemInformation(),
                AcceleratorInfo = GetAcceleratorInformation(),
                Recommendations = recommendations
            };
        }

        private PerformanceMetrics CalculateMetrics(
            List<KernelExecutionRecord> kernelExecutions,
            List<MemoryOperationRecord> memoryOperations,
            List<CustomEventRecord> customEvents)
        {
            var kernelMetrics = CalculateKernelMetrics(kernelExecutions);
            var memoryMetrics = CalculateMemoryMetrics(memoryOperations);
            var acceleratorMetrics = CalculateAcceleratorMetrics(kernelExecutions, memoryOperations);
            var customEventMetrics = CalculateCustomEventMetrics(customEvents);

            return new PerformanceMetrics
            {
                SessionStartTime = currentSessionStart,
                SessionDuration = DateTime.UtcNow - currentSessionStart,
                Kernels = kernelMetrics,
                Memory = memoryMetrics,
                Accelerator = acceleratorMetrics,
                CustomEvents = customEventMetrics
            };
        }

        private KernelMetrics CalculateKernelMetrics(List<KernelExecutionRecord> executions)
        {
            if (executions.Count == 0)
                return new KernelMetrics();

            var totalExecTime = executions.Sum(e => e.ExecutionTime.Ticks);
            var totalCompTime = executions.Sum(e => e.CompilationTime.Ticks);
            var totalThreads = executions.Sum(e => e.TotalThreads);
            var throughputs = executions.Where(e => e.Throughput.HasValue).Select(e => e.Throughput!.Value);

            var kernelStats = executions
                .GroupBy(e => e.KernelName)
                .ToDictionary(g => g.Key, g => CalculateKernelStatistics(g.ToList()));

            var cacheHits = executions.Count(e => e.WasFromCache);
            var cacheHitRatio = executions.Count > 0 ? (double)cacheHits / executions.Count : 0.0;

            return new KernelMetrics
            {
                TotalKernels = executions.Count,
                TotalExecutionTime = TimeSpan.FromTicks(totalExecTime),
                AverageExecutionTime = TimeSpan.FromTicks(totalExecTime / executions.Count),
                FastestExecution = TimeSpan.FromTicks(executions.Min(e => e.ExecutionTime.Ticks)),
                SlowestExecution = TimeSpan.FromTicks(executions.Max(e => e.ExecutionTime.Ticks)),
                TotalCompilationTime = TimeSpan.FromTicks(totalCompTime),
                AverageThroughput = throughputs.Any() ? throughputs.Average() : 0.0,
                KernelStats = kernelStats,
                TotalThreadsExecuted = totalThreads,
                CompilationCacheHitRatio = cacheHitRatio
            };
        }

        private MemoryMetrics CalculateMemoryMetrics(List<MemoryOperationRecord> operations)
        {
            if (operations.Count == 0)
                return new MemoryMetrics();

            var totalBytes = operations.Sum(o => o.SizeInBytes);
            var bandwidths = operations.Where(o => o.Bandwidth.HasValue).Select(o => o.Bandwidth!.Value);
            var operationStats = operations
                .GroupBy(o => o.OperationType)
                .ToDictionary(g => g.Key, g => CalculateMemoryOperationStatistics(g.ToList()));

            var allocations = operations.Count(o => o.OperationType == MemoryOperationType.Allocation);
            var deallocations = operations.Count(o => o.OperationType == MemoryOperationType.Deallocation);
            var poolHits = operations.Count(o => o.WasFromPool);
            var poolHitRatio = operations.Count > 0 ? (double)poolHits / operations.Count : 0.0;

            return new MemoryMetrics
            {
                TotalOperations = operations.Count,
                TotalBytesTransferred = totalBytes,
                AverageBandwidth = bandwidths.Any() ? bandwidths.Average() : 0.0,
                PeakBandwidth = bandwidths.Any() ? bandwidths.Max() : 0.0,
                TotalAllocationTime = TimeSpan.FromTicks(
                    operations.Where(o => o.OperationType == MemoryOperationType.Allocation)
                             .Sum(o => o.Duration.Ticks)),
                TotalCopyTime = TimeSpan.FromTicks(
                    operations.Where(o => o.OperationType is MemoryOperationType.HostToDevice or 
                                                            MemoryOperationType.DeviceToHost or 
                                                            MemoryOperationType.DeviceToDevice)
                             .Sum(o => o.Duration.Ticks)),
                OperationStats = operationStats,
                PoolHitRatio = poolHitRatio,
                TotalAllocations = allocations,
                TotalDeallocations = deallocations,
                CurrentMemoryUsage = accelerator.Memory.UsedMemory,
                PeakMemoryUsage = accelerator.Memory.TotalMemory - accelerator.Memory.AvailableMemory
            };
        }

        private AcceleratorMetrics CalculateAcceleratorMetrics(
            List<KernelExecutionRecord> kernelExecutions,
            List<MemoryOperationRecord> memoryOperations)
        {
            var sessionDuration = DateTime.UtcNow - currentSessionStart;
            var totalActiveTime = kernelExecutions.Sum(e => e.ExecutionTime.Ticks) + 
                                 memoryOperations.Sum(o => o.Duration.Ticks);
            var utilization = sessionDuration.Ticks > 0 ? 
                (double)totalActiveTime / sessionDuration.Ticks * 100.0 : 0.0;

            return new AcceleratorMetrics
            {
                UtilizationPercentage = Math.Min(utilization, 100.0),
                TotalActiveTime = TimeSpan.FromTicks(totalActiveTime),
                TotalIdleTime = sessionDuration - TimeSpan.FromTicks(totalActiveTime),
                ContextSwitches = 0, // Would need accelerator-specific implementation
                StreamSynchronizations = 0, // Would need accelerator-specific implementation
                AverageStreamUtilization = utilization,
                AcceleratorSpecificMetrics = GetAcceleratorSpecificMetrics()
            };
        }

        private CustomEventMetrics CalculateCustomEventMetrics(List<CustomEventRecord> events)
        {
            var eventStats = events
                .GroupBy(e => e.EventName)
                .ToDictionary(g => g.Key, g => CalculateEventStatistics(g.ToList()));

            return new CustomEventMetrics
            {
                TotalEvents = events.Count,
                EventStats = eventStats
            };
        }

        private static KernelStatistics CalculateKernelStatistics(List<KernelExecutionRecord> executions)
        {
            var executionTimes = executions.Select(e => e.ExecutionTime.Ticks).ToList();
            var totalTime = executionTimes.Sum();
            var avgTime = totalTime / executions.Count;
            var variance = executionTimes.Sum(t => Math.Pow(t - avgTime, 2)) / executions.Count;
            var stdDev = Math.Sqrt(variance);

            return new KernelStatistics
            {
                KernelName = executions.First().KernelName,
                ExecutionCount = executions.Count,
                TotalExecutionTime = TimeSpan.FromTicks(totalTime),
                AverageExecutionTime = TimeSpan.FromTicks((long)avgTime),
                MinExecutionTime = TimeSpan.FromTicks(executionTimes.Min()),
                MaxExecutionTime = TimeSpan.FromTicks(executionTimes.Max()),
                ExecutionTimeStdDev = TimeSpan.FromTicks((long)stdDev),
                TotalCompilationTime = TimeSpan.FromTicks(executions.Sum(e => e.CompilationTime.Ticks)),
                AverageThroughput = executions.Where(e => e.Throughput.HasValue).Select(e => e.Throughput!.Value).DefaultIfEmpty(0).Average(),
                TotalThreads = executions.Sum(e => e.TotalThreads)
            };
        }

        private static MemoryOperationStatistics CalculateMemoryOperationStatistics(List<MemoryOperationRecord> operations)
        {
            var totalBytes = operations.Sum(o => o.SizeInBytes);
            var totalTime = operations.Sum(o => o.Duration.Ticks);
            var bandwidths = operations.Where(o => o.Bandwidth.HasValue).Select(o => o.Bandwidth!.Value);
            var failedOps = operations.Count(o => !string.IsNullOrEmpty(o.Error));

            return new MemoryOperationStatistics
            {
                OperationType = operations.First().OperationType,
                OperationCount = operations.Count,
                TotalBytes = totalBytes,
                TotalTime = TimeSpan.FromTicks(totalTime),
                AverageTime = TimeSpan.FromTicks(totalTime / operations.Count),
                AverageBandwidth = bandwidths.Any() ? bandwidths.Average() : 0.0,
                PeakBandwidth = bandwidths.Any() ? bandwidths.Max() : 0.0,
                FailedOperations = failedOps
            };
        }

        private static EventStatistics CalculateEventStatistics(List<CustomEventRecord> events)
        {
            var totalDuration = events.Sum(e => e.Duration.Ticks);
            var durations = events.Select(e => e.Duration.Ticks).ToList();

            return new EventStatistics
            {
                EventName = events.First().EventName,
                Count = events.Count,
                TotalDuration = TimeSpan.FromTicks(totalDuration),
                AverageDuration = TimeSpan.FromTicks(totalDuration / events.Count),
                MinDuration = TimeSpan.FromTicks(durations.Min()),
                MaxDuration = TimeSpan.FromTicks(durations.Max())
            };
        }

        private static SystemInformation GetSystemInformation() => new SystemInformation
        {
            OperatingSystem = Environment.OSVersion.ToString(),
            RuntimeVersion = Environment.Version.ToString(),
            ILGPUVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
            TotalSystemMemory = GC.GetTotalMemory(false),
            AvailableSystemMemory = GC.GetTotalMemory(false), // Simplified
            ProcessorCount = Environment.ProcessorCount,
            Is64BitProcess = Environment.Is64BitProcess
        };

        private AcceleratorInformation GetAcceleratorInformation() => new AcceleratorInformation
        {
            AcceleratorType = accelerator.AcceleratorType,
            Name = accelerator.Name,
            DeviceMemorySize = accelerator.Memory.TotalMemory,
            MaxGridSize = accelerator.MaxGridSize,
            MaxGroupSize = accelerator.MaxGroupSize,
            WarpSize = accelerator.WarpSize,
            Capabilities = new Dictionary<string, object>
            {
                ["SupportsUnifiedMemory"] = accelerator.Device.SupportsUnifiedMemory,
                ["SupportsMemoryPools"] = accelerator.Device.SupportsMemoryPools,
                ["DeviceStatus"] = accelerator.Device.Status.ToString()
            }
        };

        private Dictionary<string, object> GetAcceleratorSpecificMetrics()
        {
            var metrics = new Dictionary<string, object>();

            // Add accelerator-specific metrics based on type
            switch (accelerator.AcceleratorType)
            {
                case AcceleratorType.CPU:
                    metrics["ThreadCount"] = Environment.ProcessorCount;
                    break;
                case AcceleratorType.Cuda:
                    // Would add CUDA-specific metrics here
                    break;
                case AcceleratorType.OpenCL:
                    // Would add OpenCL-specific metrics here
                    break;
            }

            return metrics;
        }

        private static List<PerformanceRecommendation> GenerateRecommendations(
            PerformanceMetrics metrics,
            List<KernelExecutionRecord> kernelExecutions,
            List<MemoryOperationRecord> memoryOperations)
        {
            var recommendations = new List<PerformanceRecommendation>();

            // Analyze kernel performance
            if (metrics.Kernels.CompilationCacheHitRatio < 0.8)
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Category = RecommendationCategory.KernelOptimization,
                    Priority = RecommendationPriority.Medium,
                    Title = "Low Kernel Compilation Cache Hit Ratio",
                    Description = "Many kernels are being recompiled instead of using cached versions.",
                    Suggestions = { "Consider kernel caching strategies", "Review kernel parameter variations" },
                    EstimatedImpact = "10-30% performance improvement"
                });
            }

            // Analyze memory performance
            if (metrics.Memory.PoolHitRatio < 0.7)
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Category = RecommendationCategory.MemoryOptimization,
                    Priority = RecommendationPriority.High,
                    Title = "Low Memory Pool Hit Ratio",
                    Description = "Memory allocations are not effectively using the memory pool.",
                    Suggestions = { "Adjust memory pool configuration", "Review allocation patterns", "Consider pre-warming pool" },
                    EstimatedImpact = "20-50% allocation performance improvement"
                });
            }

            // Analyze accelerator utilization
            if (metrics.Accelerator.UtilizationPercentage < 50)
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Category = RecommendationCategory.ResourceUtilization,
                    Priority = RecommendationPriority.High,
                    Title = "Low Accelerator Utilization",
                    Description = "The accelerator is idle for significant portions of time.",
                    Suggestions = { "Increase parallel work", "Optimize kernel launch configurations", "Consider batching operations" },
                    EstimatedImpact = "Up to 2x performance improvement"
                });
            }

            return recommendations;
        }

        private static async Task ExportToJsonAsync(string filePath, IReadOnlyList<ProfileSessionReport> reports, CancellationToken cancellationToken)
        {

            // Create a simplified export data structure to avoid serialization issues
            var exportData = reports.Select(r => new
            {
                SessionId = r.SessionId,
                SessionName = r.SessionName,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                Duration = r.Duration.TotalMilliseconds,
                KernelCount = r.KernelExecutions.Count,
                MemoryOperationCount = r.MemoryOperations.Count,
                CustomEventCount = r.CustomEvents.Count,
                CustomEvents = r.CustomEvents.Select(e => new
                {
                    e.EventId,
                    e.EventName,
                    e.Timestamp,
                    DurationMs = e.Duration.TotalMilliseconds
                }).ToList(),
                SystemInfo = new
                {
                    r.SystemInfo.OperatingSystem,
                    r.SystemInfo.RuntimeVersion,
                    r.SystemInfo.ILGPUVersion
                },
                AcceleratorInfo = new
                {
                    AcceleratorType = r.AcceleratorInfo.AcceleratorType.ToString(),
                    r.AcceleratorInfo.Name,
                    r.AcceleratorInfo.DeviceMemorySize
                },
                Metrics = new
                {
                    Kernels = new
                    {
                        r.Metrics.Kernels.TotalKernels,
                        TotalExecutionTimeMs = r.Metrics.Kernels.TotalExecutionTime.TotalMilliseconds,
                        AverageExecutionTimeMs = r.Metrics.Kernels.AverageExecutionTime.TotalMilliseconds
                    },
                    Memory = new
                    {
                        r.Metrics.Memory.TotalOperations,
                        r.Metrics.Memory.TotalBytesTransferred,
                        r.Metrics.Memory.AverageBandwidth
                    }
                }
            }).ToList();

            using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, exportData, JsonExportOptions, cancellationToken).ConfigureAwait(false);
        }

        private static async Task ExportToCsvAsync(string filePath, IReadOnlyList<ProfileSessionReport> reports, CancellationToken cancellationToken)
        {
            using var writer = new StreamWriter(filePath);
            
            // Write kernel execution CSV
            await writer.WriteLineAsync("SessionId,KernelName,StartTime,ExecutionTime,CompilationTime,GridSize,GroupSize,TotalThreads").ConfigureAwait(false);
            
            foreach (var report in reports)
            {
                foreach (var kernel in report.KernelExecutions)
                {
                    await writer.WriteLineAsync($"{report.SessionId},{kernel.KernelName},{kernel.StartTime:O},{kernel.ExecutionTime.TotalMilliseconds},{kernel.CompilationTime.TotalMilliseconds},{kernel.GridSize},{kernel.GroupSize},{kernel.TotalThreads}").ConfigureAwait(false);
                }
            }
        }

        private static async Task ExportToChromeTracingAsync(string filePath, IReadOnlyList<ProfileSessionReport> reports, CancellationToken cancellationToken) =>
            // Chrome Tracing format implementation would go here
            // For now, fallback to JSON
            await ExportToJsonAsync(filePath, reports, cancellationToken).ConfigureAwait(false);

        private static async Task ExportToBinaryAsync(string filePath, IReadOnlyList<ProfileSessionReport> reports, CancellationToken cancellationToken) =>
            // Binary format implementation would go here
            // For now, fallback to JSON
            await ExportToJsonAsync(filePath, reports, cancellationToken).ConfigureAwait(false);

        #endregion
    }
}
