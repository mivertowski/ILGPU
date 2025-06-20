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
using ILGPU.Runtime.MemoryPooling;

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
        /// Gets or sets the memory pool configuration.
        /// </summary>
        public MemoryPoolConfiguration MemoryPoolConfiguration { get; set; } = new();

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