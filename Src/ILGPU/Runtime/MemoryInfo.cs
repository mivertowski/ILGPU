// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryInfo.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Provides enhanced memory information for a device.
    /// </summary>
    /// <remarks>
    /// This class offers comprehensive memory statistics and capabilities information
    /// for devices across all accelerator types, enabling efficient memory management
    /// and allocation strategies.
    /// </remarks>
    [Serializable]
    public sealed class MemoryInfo : IEquatable<MemoryInfo>, ISerializable
    {
        /// <summary>
        /// Represents unknown or unavailable memory information.
        /// </summary>
        public static readonly MemoryInfo Unknown = new MemoryInfo();

        /// <summary>
        /// Initializes a new instance of MemoryInfo with unknown values.
        /// </summary>
        private MemoryInfo()
        {
            TotalMemory = 0;
            AvailableMemory = 0;
            UsedMemory = 0;
            MaxAllocationSize = 0;
            AllocationGranularity = 1;
            SupportsVirtualMemory = false;
            SupportsMemoryMapping = false;
            SupportsZeroCopy = false;
            CacheLineSize = 64; // Common default
            MemoryBandwidth = 0;
            IsValid = false;
        }

        /// <summary>
        /// Initializes a new instance of MemoryInfo with comprehensive memory statistics.
        /// </summary>
        /// <param name="totalMemory">Total memory available on the device in bytes.</param>
        /// <param name="availableMemory">Currently available memory in bytes.</param>
        /// <param name="usedMemory">Currently used memory in bytes.</param>
        /// <param name="maxAllocationSize">Maximum single allocation size in bytes.</param>
        /// <param name="allocationGranularity">Memory allocation granularity in bytes.</param>
        /// <param name="supportsVirtualMemory">Whether the device supports virtual memory.</param>
        /// <param name="supportsMemoryMapping">Whether the device supports memory mapping.</param>
        /// <param name="supportsZeroCopy">Whether the device supports zero-copy operations.</param>
        /// <param name="cacheLineSize">Cache line size in bytes.</param>
        /// <param name="memoryBandwidth">Memory bandwidth in bytes per second.</param>
        public MemoryInfo(
            long totalMemory,
            long availableMemory,
            long usedMemory,
            long maxAllocationSize,
            int allocationGranularity = 1,
            bool supportsVirtualMemory = false,
            bool supportsMemoryMapping = false,
            bool supportsZeroCopy = false,
            int cacheLineSize = 64,
            long memoryBandwidth = 0)
        {
            if (totalMemory < 0)
                throw new ArgumentOutOfRangeException(nameof(totalMemory), "Total memory cannot be negative");
            if (availableMemory < 0)
                throw new ArgumentOutOfRangeException(nameof(availableMemory), "Available memory cannot be negative");
            if (usedMemory < 0)
                throw new ArgumentOutOfRangeException(nameof(usedMemory), "Used memory cannot be negative");
            if (maxAllocationSize < 0)
                throw new ArgumentOutOfRangeException(nameof(maxAllocationSize), "Max allocation size cannot be negative");
            if (allocationGranularity <= 0)
                throw new ArgumentOutOfRangeException(nameof(allocationGranularity), "Allocation granularity must be positive");
            if (cacheLineSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(cacheLineSize), "Cache line size must be positive");

            TotalMemory = totalMemory;
            AvailableMemory = availableMemory;
            UsedMemory = usedMemory;
            MaxAllocationSize = maxAllocationSize;
            AllocationGranularity = allocationGranularity;
            SupportsVirtualMemory = supportsVirtualMemory;
            SupportsMemoryMapping = supportsMemoryMapping;
            SupportsZeroCopy = supportsZeroCopy;
            CacheLineSize = cacheLineSize;
            MemoryBandwidth = memoryBandwidth;
            IsValid = true;
        }

        /// <summary>
        /// Initializes a new instance from serialization data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        private MemoryInfo(SerializationInfo info, StreamingContext context)
        {
            TotalMemory = info.GetInt64(nameof(TotalMemory));
            AvailableMemory = info.GetInt64(nameof(AvailableMemory));
            UsedMemory = info.GetInt64(nameof(UsedMemory));
            MaxAllocationSize = info.GetInt64(nameof(MaxAllocationSize));
            AllocationGranularity = info.GetInt32(nameof(AllocationGranularity));
            SupportsVirtualMemory = info.GetBoolean(nameof(SupportsVirtualMemory));
            SupportsMemoryMapping = info.GetBoolean(nameof(SupportsMemoryMapping));
            SupportsZeroCopy = info.GetBoolean(nameof(SupportsZeroCopy));
            CacheLineSize = info.GetInt32(nameof(CacheLineSize));
            MemoryBandwidth = info.GetInt64(nameof(MemoryBandwidth));
            IsValid = info.GetBoolean(nameof(IsValid));
        }

        #region Properties

        /// <summary>
        /// Gets the total memory available on the device in bytes.
        /// </summary>
        public long TotalMemory { get; }

        /// <summary>
        /// Gets the currently available memory in bytes.
        /// </summary>
        public long AvailableMemory { get; }

        /// <summary>
        /// Gets the currently used memory in bytes.
        /// </summary>
        public long UsedMemory { get; }

        /// <summary>
        /// Gets the maximum single allocation size in bytes.
        /// </summary>
        public long MaxAllocationSize { get; }

        /// <summary>
        /// Gets the memory allocation granularity in bytes.
        /// </summary>
        public int AllocationGranularity { get; }

        /// <summary>
        /// Gets a value indicating whether the device supports virtual memory.
        /// </summary>
        public bool SupportsVirtualMemory { get; }

        /// <summary>
        /// Gets a value indicating whether the device supports memory mapping.
        /// </summary>
        public bool SupportsMemoryMapping { get; }

        /// <summary>
        /// Gets a value indicating whether the device supports zero-copy operations.
        /// </summary>
        public bool SupportsZeroCopy { get; }

        /// <summary>
        /// Gets the cache line size in bytes.
        /// </summary>
        public int CacheLineSize { get; }

        /// <summary>
        /// Gets the memory bandwidth in bytes per second.
        /// </summary>
        public long MemoryBandwidth { get; }

        /// <summary>
        /// Gets a value indicating whether this memory information is valid.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Gets the memory utilization as a percentage (0.0 to 100.0).
        /// </summary>
        public double MemoryUtilization => TotalMemory > 0 ? (double)UsedMemory / TotalMemory * 100.0 : 0.0;

        /// <summary>
        /// Gets the memory utilization as a percentage of available memory (0.0 to 100.0).
        /// </summary>
        public double AvailableMemoryPercentage => TotalMemory > 0 ? (double)AvailableMemory / TotalMemory * 100.0 : 0.0;

        /// <summary>
        /// Gets the fragmentation ratio (higher values indicate more fragmentation).
        /// </summary>
        public double FragmentationRatio => MaxAllocationSize > 0 && AvailableMemory > 0 
            ? Math.Max(0.0, 1.0 - ((double)MaxAllocationSize / AvailableMemory)) : 0.0;

        /// <summary>
        /// Gets a value indicating whether the device has low memory available.
        /// </summary>
        public bool IsLowMemory => AvailableMemoryPercentage < 10.0;

        /// <summary>
        /// Gets a value indicating whether the device memory is highly fragmented.
        /// </summary>
        public bool IsFragmented => FragmentationRatio > 0.5;

        #endregion

        #region Methods

        /// <summary>
        /// Calculates the optimal allocation size for the given requested size.
        /// </summary>
        /// <param name="requestedSize">The requested allocation size in bytes.</param>
        /// <returns>The optimal allocation size aligned to allocation granularity.</returns>
        public long GetOptimalAllocationSize(long requestedSize)
        {
            if (requestedSize <= 0)
                return AllocationGranularity;

            // Align to allocation granularity
            var remainder = requestedSize % AllocationGranularity;
            return remainder == 0 ? requestedSize : requestedSize + (AllocationGranularity - remainder);
        }

        /// <summary>
        /// Determines if an allocation of the specified size can be accommodated.
        /// </summary>
        /// <param name="size">The allocation size in bytes.</param>
        /// <returns>True if the allocation can be accommodated.</returns>
        public bool CanAllocate(long size) => size > 0 && size <= AvailableMemory && size <= MaxAllocationSize;

        /// <summary>
        /// Creates a snapshot of the current memory information with updated values.
        /// </summary>
        /// <param name="newAvailableMemory">The new available memory value.</param>
        /// <param name="newUsedMemory">The new used memory value.</param>
        /// <returns>A new MemoryInfo instance with updated values.</returns>
        public MemoryInfo WithUpdatedUsage(long newAvailableMemory, long newUsedMemory) => new MemoryInfo(
                TotalMemory,
                newAvailableMemory,
                newUsedMemory,
                MaxAllocationSize,
                AllocationGranularity,
                SupportsVirtualMemory,
                SupportsMemoryMapping,
                SupportsZeroCopy,
                CacheLineSize,
                MemoryBandwidth);

        #endregion

        #region Equality and Comparison

        /// <summary>
        /// Determines whether two MemoryInfo instances are equal.
        /// </summary>
        /// <param name="other">The other instance to compare.</param>
        /// <returns>True if the instances are equal.</returns>
        public bool Equals(MemoryInfo? other) => other != null &&
                   TotalMemory == other.TotalMemory &&
                   AvailableMemory == other.AvailableMemory &&
                   UsedMemory == other.UsedMemory &&
                   MaxAllocationSize == other.MaxAllocationSize &&
                   AllocationGranularity == other.AllocationGranularity &&
                   SupportsVirtualMemory == other.SupportsVirtualMemory &&
                   SupportsMemoryMapping == other.SupportsMemoryMapping &&
                   SupportsZeroCopy == other.SupportsZeroCopy &&
                   CacheLineSize == other.CacheLineSize &&
                   MemoryBandwidth == other.MemoryBandwidth;

        /// <summary>
        /// Determines whether this instance is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True if the objects are equal.</returns>
        public override bool Equals(object? obj) => obj is MemoryInfo other && Equals(other);

        /// <summary>
        /// Gets the hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(TotalMemory);
            hash.Add(AvailableMemory);
            hash.Add(UsedMemory);
            hash.Add(MaxAllocationSize);
            hash.Add(AllocationGranularity);
            hash.Add(SupportsVirtualMemory);
            hash.Add(SupportsMemoryMapping);
            hash.Add(SupportsZeroCopy);
            hash.Add(CacheLineSize);
            hash.Add(MemoryBandwidth);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Determines whether two MemoryInfo instances are equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>True if the instances are equal.</returns>
        public static bool operator ==(MemoryInfo? left, MemoryInfo? right) => 
            left?.Equals(right) ?? right is null;

        /// <summary>
        /// Determines whether two MemoryInfo instances are not equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>True if the instances are not equal.</returns>
        public static bool operator !=(MemoryInfo? left, MemoryInfo? right) => !(left == right);

        #endregion

        #region Serialization

        /// <summary>
        /// Gets the serialization data for this instance.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(TotalMemory), TotalMemory);
            info.AddValue(nameof(AvailableMemory), AvailableMemory);
            info.AddValue(nameof(UsedMemory), UsedMemory);
            info.AddValue(nameof(MaxAllocationSize), MaxAllocationSize);
            info.AddValue(nameof(AllocationGranularity), AllocationGranularity);
            info.AddValue(nameof(SupportsVirtualMemory), SupportsVirtualMemory);
            info.AddValue(nameof(SupportsMemoryMapping), SupportsMemoryMapping);
            info.AddValue(nameof(SupportsZeroCopy), SupportsZeroCopy);
            info.AddValue(nameof(CacheLineSize), CacheLineSize);
            info.AddValue(nameof(MemoryBandwidth), MemoryBandwidth);
            info.AddValue(nameof(IsValid), IsValid);
        }

        #endregion

        #region String Representation

        /// <summary>
        /// Returns a string representation of the memory information.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString()
        {
            if (!IsValid)
                return "Memory: Unknown";

            var totalMB = TotalMemory / (1024 * 1024);
            var availableMB = AvailableMemory / (1024 * 1024);
            var usedMB = UsedMemory / (1024 * 1024);

            return $"Memory: {usedMB}MB/{totalMB}MB used ({MemoryUtilization:F1}%), " +
                   $"{availableMB}MB available, Max Allocation: {MaxAllocationSize / (1024 * 1024)}MB";
        }

        #endregion
    }
}