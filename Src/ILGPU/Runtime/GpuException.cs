// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: GpuException.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ILGPU.Runtime
{
    /// <summary>
    /// The base exception type for all GPU-related errors in ILGPU.
    /// </summary>
    /// <remarks>
    /// This exception provides enhanced error information including device context,
    /// error codes, and recovery suggestions. It addresses the critical issue where
    /// ILGPU error handling was fragmented across different exception types without
    /// consistent error information.
    /// </remarks>
    [Serializable]
    public class GpuException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the GpuException class.
        /// </summary>
        public GpuException()
            : this("An unknown GPU error occurred.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the GpuException class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public GpuException(string message)
            : base(message)
        {
            ErrorCode = GpuErrorCode.Unknown;
            DeviceInfo = DeviceErrorInfo.Unknown;
        }

        /// <summary>
        /// Initializes a new instance of the GpuException class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public GpuException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = GpuErrorCode.Unknown;
            DeviceInfo = DeviceErrorInfo.Unknown;
        }

        /// <summary>
        /// Initializes a new instance of the GpuException class with detailed error information.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="errorCode">The specific GPU error code.</param>
        /// <param name="deviceInfo">Information about the device where the error occurred.</param>
        public GpuException(string message, GpuErrorCode errorCode, DeviceErrorInfo deviceInfo)
            : base(message)
        {
            ErrorCode = errorCode;
            DeviceInfo = deviceInfo;
        }

        /// <summary>
        /// Initializes a new instance of the GpuException class with detailed error information and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="errorCode">The specific GPU error code.</param>
        /// <param name="deviceInfo">Information about the device where the error occurred.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public GpuException(string message, GpuErrorCode errorCode, DeviceErrorInfo deviceInfo, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            DeviceInfo = deviceInfo;
        }

        /// <summary>
        /// Initializes a new instance of the GpuException class with serialized data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        protected GpuException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ErrorCode = (GpuErrorCode)info.GetInt32(nameof(ErrorCode));
            DeviceInfo = (DeviceErrorInfo)info.GetValue(nameof(DeviceInfo), typeof(DeviceErrorInfo))!;
        }

        /// <summary>
        /// Gets the specific GPU error code.
        /// </summary>
        public GpuErrorCode ErrorCode { get; }

        /// <summary>
        /// Gets information about the device where the error occurred.
        /// </summary>
        public DeviceErrorInfo DeviceInfo { get; }

        /// <summary>
        /// Gets additional context information about the error.
        /// </summary>
        public Dictionary<string, object> Context { get; } = new();

        /// <summary>
        /// Gets recovery suggestions for this error.
        /// </summary>
        public virtual IEnumerable<string> RecoverySuggestions => GetRecoverySuggestions();

        /// <summary>
        /// Sets serialization data for this exception.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ErrorCode), (int)ErrorCode);
            info.AddValue(nameof(DeviceInfo), DeviceInfo);
        }

        /// <summary>
        /// Gets recovery suggestions based on the error code.
        /// </summary>
        /// <returns>An enumerable of recovery suggestions.</returns>
        protected virtual IEnumerable<string> GetRecoverySuggestions() => ErrorCode switch
        {
            GpuErrorCode.OutOfMemory => new[]
            {
                    "Reduce memory buffer sizes",
                    "Dispose unused memory buffers",
                    "Enable memory pooling to reuse buffers",
                    "Consider using paged memory for large allocations"
                },
            GpuErrorCode.DeviceNotFound => new[]
            {
                    "Verify GPU drivers are installed and up to date",
                    "Check that the device is properly connected",
                    "Ensure the accelerator type matches available hardware"
                },
            GpuErrorCode.KernelLaunchFailed => new[]
            {
                    "Check kernel parameters and memory bounds",
                    "Verify kernel configuration is valid",
                    "Ensure all memory buffers are properly allocated"
                },
            GpuErrorCode.InvalidOperation => new[]
            {
                    "Check that the operation is supported on this device",
                    "Verify the current device state",
                    "Ensure proper initialization before operation"
                },
            _ => new[] { "Consult ILGPU documentation for troubleshooting guidance" }
        };

        /// <summary>
        /// Creates a string representation of the exception with enhanced error information.
        /// </summary>
        /// <returns>A string representation of the exception.</returns>
        public override string ToString()
        {
            var result = $"{GetType().Name}: {Message}";
            
            if (ErrorCode != GpuErrorCode.Unknown)
                result += $" (Error Code: {ErrorCode})";
            
            if (DeviceInfo.IsValid)
                result += $" (Device: {DeviceInfo})";
            
            if (Context.Count > 0)
            {
                result += "\nContext:";
                foreach (var kvp in Context)
                    result += $"\n  {kvp.Key}: {kvp.Value}";
            }

            if (InnerException != null)
                result += $"\n ---> {InnerException}";

            result += $"\n{StackTrace}";
            return result;
        }
    }

    /// <summary>
    /// Exception thrown when GPU memory operations fail.
    /// </summary>
    [Serializable]
    public sealed class GpuMemoryException : GpuException
    {
        /// <summary>
        /// Initializes a new instance of the GpuMemoryException class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="requestedSize">The size that was requested.</param>
        /// <param name="availableSize">The available memory size.</param>
        public GpuMemoryException(string message, long requestedSize, long availableSize)
            : base(message, GpuErrorCode.OutOfMemory, DeviceErrorInfo.Unknown)
        {
            RequestedSize = requestedSize;
            AvailableSize = availableSize;
            Context["RequestedSize"] = requestedSize;
            Context["AvailableSize"] = availableSize;
        }

        /// <summary>
        /// Initializes a new instance of the GpuMemoryException class with serialized data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        private GpuMemoryException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            RequestedSize = info.GetInt64(nameof(RequestedSize));
            AvailableSize = info.GetInt64(nameof(AvailableSize));
        }

        /// <summary>
        /// Gets the size that was requested when the error occurred.
        /// </summary>
        public long RequestedSize { get; }

        /// <summary>
        /// Gets the available memory size when the error occurred.
        /// </summary>
        public long AvailableSize { get; }

        /// <summary>
        /// Sets serialization data for this exception.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(RequestedSize), RequestedSize);
            info.AddValue(nameof(AvailableSize), AvailableSize);
        }
    }

    /// <summary>
    /// Exception thrown when kernel execution fails.
    /// </summary>
    [Serializable]
    public sealed class GpuKernelException : GpuException
    {
        /// <summary>
        /// Initializes a new instance of the GpuKernelException class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="kernelName">The name of the kernel that failed.</param>
        /// <param name="launchConfig">The kernel launch configuration.</param>
        public GpuKernelException(string message, string kernelName, KernelConfig launchConfig)
            : base(message, GpuErrorCode.KernelLaunchFailed, DeviceErrorInfo.Unknown)
        {
            KernelName = kernelName ?? "Unknown";
            LaunchConfig = launchConfig;
            Context["KernelName"] = KernelName;
            Context["LaunchConfig"] = launchConfig.ToString();
        }

        /// <summary>
        /// Initializes a new instance of the GpuKernelException class with serialized data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        private GpuKernelException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            KernelName = info.GetString(nameof(KernelName)) ?? "Unknown";
            LaunchConfig = (KernelConfig)info.GetValue(nameof(LaunchConfig), typeof(KernelConfig))!;
        }

        /// <summary>
        /// Gets the name of the kernel that failed.
        /// </summary>
        public string KernelName { get; }

        /// <summary>
        /// Gets the kernel launch configuration that was used.
        /// </summary>
        public KernelConfig LaunchConfig { get; }

        /// <summary>
        /// Sets serialization data for this exception.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(KernelName), KernelName);
            info.AddValue(nameof(LaunchConfig), LaunchConfig);
        }
    }

    /// <summary>
    /// Exception thrown when device operations fail.
    /// </summary>
    [Serializable]
    public sealed class GpuDeviceException : GpuException
    {
        /// <summary>
        /// Initializes a new instance of the GpuDeviceException class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="deviceInfo">Information about the device.</param>
        /// <param name="nativeErrorCode">The native error code from the device driver.</param>
        public GpuDeviceException(string message, DeviceErrorInfo deviceInfo, int nativeErrorCode)
            : base(message, GpuErrorCode.DeviceError, deviceInfo)
        {
            NativeErrorCode = nativeErrorCode;
            Context["NativeErrorCode"] = nativeErrorCode;
        }

        /// <summary>
        /// Initializes a new instance of the GpuDeviceException class with serialized data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        private GpuDeviceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            NativeErrorCode = info.GetInt32(nameof(NativeErrorCode));
        }

        /// <summary>
        /// Gets the native error code from the device driver.
        /// </summary>
        public int NativeErrorCode { get; }

        /// <summary>
        /// Sets serialization data for this exception.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(NativeErrorCode), NativeErrorCode);
        }
    }
}
