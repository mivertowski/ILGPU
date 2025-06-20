// ---------------------------------------------------------------------------------------
//                                   ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: ILGPUOptions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;

namespace ILGPU.Runtime.DependencyInjection
{
    /// <summary>
    /// Configuration options for ILGPU dependency injection.
    /// </summary>
    public sealed class ILGPUOptions
    {
        /// <summary>
        /// Gets or sets the preferred accelerator type.
        /// </summary>
        public AcceleratorType PreferredAcceleratorType { get; set; } = AcceleratorType.CPU;

        /// <summary>
        /// Gets or sets the device selector function.
        /// </summary>
        public Func<IReadOnlyList<Device>, Device>? DeviceSelector { get; set; }

        /// <summary>
        /// Gets or sets whether profiling is enabled.
        /// </summary>
        public bool EnableProfiling { get; set; } = false;

        /// <summary>
        /// Gets or sets whether memory pooling is enabled.
        /// </summary>
        public bool EnableMemoryPooling { get; set; } = true;

        /// <summary>
        /// Gets or sets the memory pool options.
        /// </summary>
        public MemoryPoolOptions MemoryPoolOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets whether debug mode is enabled.
        /// </summary>
        public bool EnableDebugAssertions { get; set; } = false;

        /// <summary>
        /// Gets or sets the context builder configurator.
        /// </summary>
        public Action<Context.Builder>? ContextConfigurator { get; set; }
    }

    /// <summary>
    /// Memory pool configuration options.
    /// </summary>
    public sealed class MemoryPoolOptions
    {
        /// <summary>
        /// Gets or sets the maximum pool size in bytes.
        /// </summary>
        public long MaxPoolSizeBytes { get; set; } = 1024L * 1024 * 1024; // 1GB

        /// <summary>
        /// Gets or sets the maximum buffer size in bytes.
        /// </summary>
        public long MaxBufferSizeBytes { get; set; } = 100L * 1024 * 1024; // 100MB

        /// <summary>
        /// Gets or sets the pool retention policy.
        /// </summary>
        public PoolRetentionPolicy RetentionPolicy { get; set; } = PoolRetentionPolicy.Adaptive;

        /// <summary>
        /// Gets or sets the buffer trim interval.
        /// </summary>
        public TimeSpan BufferTrimInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets whether to enable pool statistics tracking.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;
    }

    /// <summary>
    /// Performance profiling configuration options.
    /// </summary>
    public sealed class ProfilingOptions
    {
        /// <summary>
        /// Gets or sets whether kernel profiling is enabled.
        /// </summary>
        public bool EnableKernelProfiling { get; set; } = true;

        /// <summary>
        /// Gets or sets whether memory profiling is enabled.
        /// </summary>
        public bool EnableMemoryProfiling { get; set; } = true;

        /// <summary>
        /// Gets or sets whether detailed timing is enabled.
        /// </summary>
        public bool EnableDetailedTiming { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum number of profiling sessions to retain.
        /// </summary>
        public int MaxSessionHistory { get; set; } = 10;

        /// <summary>
        /// Gets or sets the profiling output format.
        /// </summary>
        public ProfilingOutputFormat OutputFormat { get; set; } = ProfilingOutputFormat.Json;
    }

    /// <summary>
    /// Pool retention policy enumeration.
    /// </summary>
    public enum PoolRetentionPolicy
    {
        /// <summary>
        /// Immediately return buffers to the system.
        /// </summary>
        Immediate,

        /// <summary>
        /// Keep buffers for a fixed time period.
        /// </summary>
        Fixed,

        /// <summary>
        /// Adaptively manage buffer retention based on usage patterns.
        /// </summary>
        Adaptive
    }

    /// <summary>
    /// Profiling output format enumeration.
    /// </summary>
    public enum ProfilingOutputFormat
    {
        /// <summary>
        /// JSON format output.
        /// </summary>
        Json,

        /// <summary>
        /// XML format output.
        /// </summary>
        Xml,

        /// <summary>
        /// CSV format output.
        /// </summary>
        Csv
    }
}

#endif