// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: DeviceStatus.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents the current status of a device.
    /// </summary>
    /// <remarks>
    /// This enumeration provides standardized device status tracking across different
    /// accelerator types, enabling consistent device state monitoring and management.
    /// </remarks>
    public enum DeviceStatus
    {
        /// <summary>
        /// Device status is unknown or could not be determined.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Device is available and ready for use.
        /// </summary>
        Available = 1,

        /// <summary>
        /// Device is currently busy executing operations.
        /// </summary>
        Busy = 2,

        /// <summary>
        /// Device is in an error state and requires recovery.
        /// </summary>
        Error = 3,

        /// <summary>
        /// Device is not available or has been disabled.
        /// </summary>
        Unavailable = 4,

        /// <summary>
        /// Device is being used exclusively by another process.
        /// </summary>
        ExclusiveProcess = 5,

        /// <summary>
        /// Device is in a low-power or suspended state.
        /// </summary>
        Suspended = 6,

        /// <summary>
        /// Device is initializing or starting up.
        /// </summary>
        Initializing = 7,

        /// <summary>
        /// Device is shutting down or being disposed.
        /// </summary>
        ShuttingDown = 8,

        /// <summary>
        /// Device requires driver update or reconfiguration.
        /// </summary>
        RequiresUpdate = 9,

        /// <summary>
        /// Device is overheating and throttling performance.
        /// </summary>
        Throttling = 10,

        /// <summary>
        /// Device has experienced a fatal error and requires restart.
        /// </summary>
        FatalError = 11
    }

    /// <summary>
    /// Provides utility methods for working with device status.
    /// </summary>
    public static class DeviceStatusExtensions
    {
        /// <summary>
        /// Determines if the device status indicates the device is usable.
        /// </summary>
        /// <param name="status">The device status to check.</param>
        /// <returns>True if the device can be used for operations.</returns>
        public static bool IsUsable(this DeviceStatus status) => status switch
        {
            DeviceStatus.Available => true,
            DeviceStatus.Busy => true,
            DeviceStatus.Throttling => true,
            _ => false
        };

        /// <summary>
        /// Determines if the device status indicates an error condition.
        /// </summary>
        /// <param name="status">The device status to check.</param>
        /// <returns>True if the device is in an error state.</returns>
        public static bool IsError(this DeviceStatus status) => status switch
        {
            DeviceStatus.Error => true,
            DeviceStatus.FatalError => true,
            _ => false
        };

        /// <summary>
        /// Determines if the device status indicates the device is temporarily unavailable.
        /// </summary>
        /// <param name="status">The device status to check.</param>
        /// <returns>True if the device is temporarily unavailable but may recover.</returns>
        public static bool IsTemporarilyUnavailable(this DeviceStatus status) => status switch
        {
            DeviceStatus.Busy => true,
            DeviceStatus.ExclusiveProcess => true,
            DeviceStatus.Suspended => true,
            DeviceStatus.Initializing => true,
            DeviceStatus.ShuttingDown => true,
            DeviceStatus.Throttling => true,
            _ => false
        };

        /// <summary>
        /// Determines if the device status indicates the device needs attention.
        /// </summary>
        /// <param name="status">The device status to check.</param>
        /// <returns>True if the device requires user intervention.</returns>
        public static bool RequiresAttention(this DeviceStatus status) => status switch
        {
            DeviceStatus.Error => true,
            DeviceStatus.RequiresUpdate => true,
            DeviceStatus.FatalError => true,
            _ => false
        };

        /// <summary>
        /// Gets a human-readable description of the device status.
        /// </summary>
        /// <param name="status">The device status.</param>
        /// <returns>A descriptive string explaining the status.</returns>
        public static string GetDescription(this DeviceStatus status) => status switch
        {
            DeviceStatus.Unknown => "Status cannot be determined",
            DeviceStatus.Available => "Ready for use",
            DeviceStatus.Busy => "Currently executing operations",
            DeviceStatus.Error => "Device error - recovery needed",
            DeviceStatus.Unavailable => "Device not available",
            DeviceStatus.ExclusiveProcess => "In use by another process",
            DeviceStatus.Suspended => "In low-power state",
            DeviceStatus.Initializing => "Starting up",
            DeviceStatus.ShuttingDown => "Shutting down",
            DeviceStatus.RequiresUpdate => "Driver update needed",
            DeviceStatus.Throttling => "Performance throttled due to temperature",
            DeviceStatus.FatalError => "Fatal error - restart required",
            _ => "Unknown status"
        };
    }
}
