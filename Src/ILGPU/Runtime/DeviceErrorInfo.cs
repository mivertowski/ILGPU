// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: DeviceErrorInfo.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Contains detailed information about the device where an error occurred.
    /// </summary>
    /// <remarks>
    /// This structure provides comprehensive device context for error analysis and debugging,
    /// enabling better error diagnostics across different accelerator types.
    /// </remarks>
    [Serializable]
    public readonly struct DeviceErrorInfo : IEquatable<DeviceErrorInfo>, ISerializable
    {
        /// <summary>
        /// Represents unknown or unavailable device information.
        /// </summary>
        public static readonly DeviceErrorInfo Unknown = new();

        /// <summary>
        /// Initializes a new instance of the DeviceErrorInfo struct.
        /// </summary>
        /// <param name="acceleratorType">The type of accelerator.</param>
        /// <param name="deviceId">The device identifier.</param>
        /// <param name="deviceName">The name of the device.</param>
        /// <param name="driverVersion">The driver version.</param>
        /// <param name="memoryInfo">Memory information.</param>
        public DeviceErrorInfo(
            AcceleratorType acceleratorType,
            DeviceId deviceId,
            string deviceName,
            string driverVersion,
            DeviceMemoryInfo memoryInfo)
        {
            AcceleratorType = acceleratorType;
            DeviceId = deviceId;
            DeviceName = deviceName ?? "Unknown";
            DriverVersion = driverVersion ?? "Unknown";
            MemoryInfo = memoryInfo;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Initializes a new instance of the DeviceErrorInfo struct from serialization data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        private DeviceErrorInfo(SerializationInfo info, StreamingContext context)
        {
            AcceleratorType = (AcceleratorType)info.GetInt32(nameof(AcceleratorType));
            DeviceId = (DeviceId)info.GetValue(nameof(DeviceId), typeof(DeviceId))!;
            DeviceName = info.GetString(nameof(DeviceName)) ?? "Unknown";
            DriverVersion = info.GetString(nameof(DriverVersion)) ?? "Unknown";
            MemoryInfo = (DeviceMemoryInfo)info.GetValue(nameof(MemoryInfo), typeof(DeviceMemoryInfo))!;
            Timestamp = info.GetDateTime(nameof(Timestamp));
        }

        /// <summary>
        /// Gets the type of accelerator.
        /// </summary>
        public AcceleratorType AcceleratorType { get; }

        /// <summary>
        /// Gets the device identifier.
        /// </summary>
        public DeviceId DeviceId { get; }

        /// <summary>
        /// Gets the name of the device.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// Gets the driver version.
        /// </summary>
        public string DriverVersion { get; }

        /// <summary>
        /// Gets the memory information for the device.
        /// </summary>
        public DeviceMemoryInfo MemoryInfo { get; }

        /// <summary>
        /// Gets the timestamp when this error information was created.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Gets a value indicating whether this device information is valid.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(DeviceName) && DeviceName != "Unknown";

        /// <summary>
        /// Creates device error information from an accelerator.
        /// </summary>
        /// <param name="accelerator">The accelerator to get information from.</param>
        /// <returns>Device error information.</returns>
        public static DeviceErrorInfo FromAccelerator(Accelerator accelerator)
        {
            if (accelerator == null)
                return Unknown;

            try
            {
                var memoryInfo = new DeviceMemoryInfo(
                    accelerator.MemorySize,
                    accelerator.MemorySize, // Available memory (would need actual implementation)
                    accelerator.MaxGridSize.Size,
                    accelerator.MaxGroupSize.Size
                );

                // Create a synthetic DeviceId until Accelerator class implements IDeviceIdentifiable
                var deviceId = accelerator.AcceleratorType switch
                {
                    AcceleratorType.CPU => DeviceId.FromCPU(accelerator.GetHashCode()),
                    AcceleratorType.Cuda => DeviceId.FromCuda(0), // Would need actual CUDA device ID
                    AcceleratorType.OpenCL => DeviceId.FromOpenCL(IntPtr.Zero, IntPtr.Zero), // Would need actual OpenCL IDs
                    AcceleratorType.Velocity => DeviceId.FromVelocity(accelerator.GetHashCode()),
                    _ => new DeviceId(0, accelerator.AcceleratorType)
                };

                return new DeviceErrorInfo(
                    accelerator.AcceleratorType,
                    deviceId,
                    accelerator.Name,
                    "Unknown", // Driver version would need platform-specific implementation
                    memoryInfo
                );
            }
            catch
            {
                // If we can't get device info, return unknown
                return Unknown;
            }
        }

        /// <summary>
        /// Creates device error information from a device.
        /// </summary>
        /// <param name="device">The device to get information from.</param>
        /// <returns>Device error information.</returns>
        public static DeviceErrorInfo FromDevice(Device device)
        {
            if (device == null)
                return Unknown;

            try
            {
                var memoryInfo = new DeviceMemoryInfo(
                    device.MemorySize,
                    device.MemorySize, // Available memory
                    device.MaxGridSize.Size,
                    device.MaxGroupSize.Size
                );

                // Create a synthetic DeviceId until Device class implements IDeviceIdentifiable
                var deviceId = device.AcceleratorType switch
                {
                    AcceleratorType.CPU => DeviceId.FromCPU(device.GetHashCode()),
                    AcceleratorType.Cuda => DeviceId.FromCuda(0), // Would need actual CUDA device ID
                    AcceleratorType.OpenCL => DeviceId.FromOpenCL(IntPtr.Zero, IntPtr.Zero), // Would need actual OpenCL IDs
                    AcceleratorType.Velocity => DeviceId.FromVelocity(device.GetHashCode()),
                    _ => new DeviceId(0, device.AcceleratorType)
                };

                return new DeviceErrorInfo(
                    device.AcceleratorType,
                    deviceId,
                    device.Name,
                    "Unknown", // Driver version
                    memoryInfo
                );
            }
            catch
            {
                return Unknown;
            }
        }

        /// <summary>
        /// Determines whether two DeviceErrorInfo instances are equal.
        /// </summary>
        /// <param name="other">The other instance to compare.</param>
        /// <returns>True if the instances are equal.</returns>
        public bool Equals(DeviceErrorInfo other) =>
            AcceleratorType == other.AcceleratorType &&
            DeviceId.Equals(other.DeviceId) &&
            DeviceName == other.DeviceName &&
            DriverVersion == other.DriverVersion;

        /// <summary>
        /// Determines whether this instance is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True if the objects are equal.</returns>
        public override bool Equals(object? obj) => obj is DeviceErrorInfo other && Equals(other);

        /// <summary>
        /// Gets the hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => HashCode.Combine(AcceleratorType, DeviceId, DeviceName, DriverVersion);

        /// <summary>
        /// Returns a string representation of the device error information.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString()
        {
            if (!IsValid)
                return "Unknown Device";

            return $"{AcceleratorType} Device '{DeviceName}' (ID: {DeviceId}, Driver: {DriverVersion})";
        }

        /// <summary>
        /// Gets the serialization data for this instance.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(AcceleratorType), (int)AcceleratorType);
            info.AddValue(nameof(DeviceId), DeviceId);
            info.AddValue(nameof(DeviceName), DeviceName);
            info.AddValue(nameof(DriverVersion), DriverVersion);
            info.AddValue(nameof(MemoryInfo), MemoryInfo);
            info.AddValue(nameof(Timestamp), Timestamp);
        }

        /// <summary>
        /// Determines whether two DeviceErrorInfo instances are equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>True if the instances are equal.</returns>
        public static bool operator ==(DeviceErrorInfo left, DeviceErrorInfo right) => left.Equals(right);

        /// <summary>
        /// Determines whether two DeviceErrorInfo instances are not equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>True if the instances are not equal.</returns>
        public static bool operator !=(DeviceErrorInfo left, DeviceErrorInfo right) => !left.Equals(right);
    }

    /// <summary>
    /// Contains memory information for a device.
    /// </summary>
    [Serializable]
    public readonly struct DeviceMemoryInfo : IEquatable<DeviceMemoryInfo>, ISerializable
    {
        /// <summary>
        /// Initializes a new instance of the DeviceMemoryInfo struct.
        /// </summary>
        /// <param name="totalMemory">The total memory available on the device.</param>
        /// <param name="availableMemory">The currently available memory.</param>
        /// <param name="maxAllocationSize">The maximum single allocation size.</param>
        /// <param name="maxWorkGroupSize">The maximum work group size.</param>
        public DeviceMemoryInfo(long totalMemory, long availableMemory, long maxAllocationSize, int maxWorkGroupSize)
        {
            TotalMemory = totalMemory;
            AvailableMemory = availableMemory;
            MaxAllocationSize = maxAllocationSize;
            MaxWorkGroupSize = maxWorkGroupSize;
        }

        /// <summary>
        /// Initializes a new instance of the DeviceMemoryInfo struct from serialization data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        private DeviceMemoryInfo(SerializationInfo info, StreamingContext context)
        {
            TotalMemory = info.GetInt64(nameof(TotalMemory));
            AvailableMemory = info.GetInt64(nameof(AvailableMemory));
            MaxAllocationSize = info.GetInt64(nameof(MaxAllocationSize));
            MaxWorkGroupSize = info.GetInt32(nameof(MaxWorkGroupSize));
        }

        /// <summary>
        /// Gets the total memory available on the device in bytes.
        /// </summary>
        public long TotalMemory { get; }

        /// <summary>
        /// Gets the currently available memory in bytes.
        /// </summary>
        public long AvailableMemory { get; }

        /// <summary>
        /// Gets the maximum single allocation size in bytes.
        /// </summary>
        public long MaxAllocationSize { get; }

        /// <summary>
        /// Gets the maximum work group size.
        /// </summary>
        public int MaxWorkGroupSize { get; }

        /// <summary>
        /// Gets the memory utilization as a percentage.
        /// </summary>
        public double MemoryUtilization => TotalMemory > 0 ? (double)(TotalMemory - AvailableMemory) / TotalMemory * 100.0 : 0.0;

        /// <summary>
        /// Determines whether two DeviceMemoryInfo instances are equal.
        /// </summary>
        /// <param name="other">The other instance to compare.</param>
        /// <returns>True if the instances are equal.</returns>
        public bool Equals(DeviceMemoryInfo other) =>
            TotalMemory == other.TotalMemory &&
            AvailableMemory == other.AvailableMemory &&
            MaxAllocationSize == other.MaxAllocationSize &&
            MaxWorkGroupSize == other.MaxWorkGroupSize;

        /// <summary>
        /// Determines whether this instance is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True if the objects are equal.</returns>
        public override bool Equals(object? obj) => obj is DeviceMemoryInfo other && Equals(other);

        /// <summary>
        /// Gets the hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => HashCode.Combine(TotalMemory, AvailableMemory, MaxAllocationSize, MaxWorkGroupSize);

        /// <summary>
        /// Returns a string representation of the memory information.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString() => $"Memory: {AvailableMemory / (1024 * 1024)}MB/{TotalMemory / (1024 * 1024)}MB " +
                   $"({MemoryUtilization:F1}% used), Max Allocation: {MaxAllocationSize / (1024 * 1024)}MB";

        /// <summary>
        /// Gets the serialization data for this instance.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(TotalMemory), TotalMemory);
            info.AddValue(nameof(AvailableMemory), AvailableMemory);
            info.AddValue(nameof(MaxAllocationSize), MaxAllocationSize);
            info.AddValue(nameof(MaxWorkGroupSize), MaxWorkGroupSize);
        }

        /// <summary>
        /// Determines whether two DeviceMemoryInfo instances are equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>True if the instances are equal.</returns>
        public static bool operator ==(DeviceMemoryInfo left, DeviceMemoryInfo right) => left.Equals(right);

        /// <summary>
        /// Determines whether two DeviceMemoryInfo instances are not equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>True if the instances are not equal.</returns>
        public static bool operator !=(DeviceMemoryInfo left, DeviceMemoryInfo right) => !left.Equals(right);
    }
}