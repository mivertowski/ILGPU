// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryPoolConfiguration.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU.Runtime.MemoryPooling
{
    /// <summary>
    /// Configuration settings for memory pools.
    /// </summary>
    public sealed class MemoryPoolConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryPoolConfiguration"/> class with default settings.
        /// </summary>
        public MemoryPoolConfiguration()
        {
            // Default to 1GB max pool size on 64-bit, 256MB on 32-bit
            MaxPoolSizeBytes = Environment.Is64BitProcess 
                ? 1024L * 1024 * 1024 
                : 256L * 1024 * 1024;
            
            MaxBufferSizeBytes = 100L * 1024 * 1024; // 100MB max single buffer
            RetentionPolicy = PoolRetentionPolicy.Adaptive;
            BufferTrimInterval = TimeSpan.FromMinutes(5);
            MinBuffersToKeep = 2;
            MaxBuffersPerSize = 8;
            AllocationAlignment = 256; // 256-byte alignment for optimal GPU performance
            EnableStatistics = true;
            PrewarmCommonSizes = true;
        }

        /// <summary>
        /// Gets or sets the maximum pool size in bytes.
        /// </summary>
        public long MaxPoolSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the maximum size of a single buffer in bytes.
        /// </summary>
        public long MaxBufferSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the retention policy for unused buffers.
        /// </summary>
        public PoolRetentionPolicy RetentionPolicy { get; set; }

        /// <summary>
        /// Gets or sets the interval for trimming unused buffers.
        /// </summary>
        public TimeSpan BufferTrimInterval { get; set; }

        /// <summary>
        /// Gets or sets the minimum number of buffers to keep per size bucket.
        /// </summary>
        public int MinBuffersToKeep { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of buffers to keep per size bucket.
        /// </summary>
        public int MaxBuffersPerSize { get; set; }

        /// <summary>
        /// Gets or sets the allocation alignment in bytes.
        /// </summary>
        public int AllocationAlignment { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to collect detailed statistics.
        /// </summary>
        public bool EnableStatistics { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to pre-warm the pool with common buffer sizes.
        /// </summary>
        public bool PrewarmCommonSizes { get; set; }

        /// <summary>
        /// Gets or sets the factor for buffer size buckets (e.g., 2.0 for powers of 2).
        /// </summary>
        public double SizeBucketFactor { get; set; } = 1.5;

        /// <summary>
        /// Gets or sets the threshold for considering a buffer "large" (different pooling strategy).
        /// </summary>
        public long LargeBufferThreshold { get; set; } = 10L * 1024 * 1024; // 10MB

        /// <summary>
        /// Validates the configuration and throws an exception if invalid.
        /// </summary>
        public void Validate()
        {
            if (MaxPoolSizeBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxPoolSizeBytes), "Must be positive");

            if (MaxBufferSizeBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxBufferSizeBytes), "Must be positive");

            if (MaxBufferSizeBytes > MaxPoolSizeBytes)
                throw new ArgumentException("MaxBufferSizeBytes cannot exceed MaxPoolSizeBytes");

            if (BufferTrimInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(BufferTrimInterval), "Must be positive");

            if (MinBuffersToKeep < 0)
                throw new ArgumentOutOfRangeException(nameof(MinBuffersToKeep), "Must be non-negative");

            if (MaxBuffersPerSize < MinBuffersToKeep)
                throw new ArgumentException("MaxBuffersPerSize must be >= MinBuffersToKeep");

            if (AllocationAlignment <= 0 || (AllocationAlignment & (AllocationAlignment - 1)) != 0)
                throw new ArgumentException("AllocationAlignment must be a positive power of 2");

            if (SizeBucketFactor <= 1.0)
                throw new ArgumentOutOfRangeException(nameof(SizeBucketFactor), "Must be greater than 1.0");

            if (LargeBufferThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(LargeBufferThreshold), "Must be positive");
        }

        /// <summary>
        /// Creates a configuration optimized for high-performance scenarios.
        /// </summary>
        /// <returns>A high-performance memory pool configuration.</returns>
        public static MemoryPoolConfiguration CreateHighPerformance() => new MemoryPoolConfiguration
        {
            MaxPoolSizeBytes = Environment.Is64BitProcess ? 2L * 1024 * 1024 * 1024 : 512L * 1024 * 1024,
            MaxBufferSizeBytes = 200L * 1024 * 1024,
            RetentionPolicy = PoolRetentionPolicy.KeepAll,
            BufferTrimInterval = TimeSpan.FromMinutes(10),
            MinBuffersToKeep = 4,
            MaxBuffersPerSize = 16,
            AllocationAlignment = 512,
            PrewarmCommonSizes = true,
            SizeBucketFactor = 1.25, // More granular size buckets
        };

        /// <summary>
        /// Creates a configuration optimized for low memory usage.
        /// </summary>
        /// <returns>A memory-efficient pool configuration.</returns>
        public static MemoryPoolConfiguration CreateMemoryEfficient() => new MemoryPoolConfiguration
        {
            MaxPoolSizeBytes = Environment.Is64BitProcess ? 256L * 1024 * 1024 : 64L * 1024 * 1024,
            MaxBufferSizeBytes = 32L * 1024 * 1024,
            RetentionPolicy = PoolRetentionPolicy.Aggressive,
            BufferTrimInterval = TimeSpan.FromMinutes(2),
            MinBuffersToKeep = 1,
            MaxBuffersPerSize = 3,
            AllocationAlignment = 128,
            PrewarmCommonSizes = false,
            SizeBucketFactor = 2.0, // Fewer size buckets
        };

        /// <summary>
        /// Creates a configuration optimized for development and debugging.
        /// </summary>
        /// <returns>A development-friendly pool configuration.</returns>
        public static MemoryPoolConfiguration CreateDevelopment() => new MemoryPoolConfiguration
        {
            MaxPoolSizeBytes = 128L * 1024 * 1024,
            MaxBufferSizeBytes = 16L * 1024 * 1024,
            RetentionPolicy = PoolRetentionPolicy.Adaptive,
            BufferTrimInterval = TimeSpan.FromMinutes(1),
            MinBuffersToKeep = 1,
            MaxBuffersPerSize = 4,
            AllocationAlignment = 64,
            EnableStatistics = true,
            PrewarmCommonSizes = false,
        };
    }
}