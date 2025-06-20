// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: GpuErrorCode.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents specific GPU error codes for enhanced error classification.
    /// </summary>
    /// <remarks>
    /// This enumeration provides standardized error codes across different accelerator
    /// types, enabling consistent error handling and recovery strategies.
    /// </remarks>
    public enum GpuErrorCode
    {
        /// <summary>
        /// Unknown or unspecified error.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Success - no error occurred.
        /// </summary>
        Success = 1,

        /// <summary>
        /// Device initialization failed.
        /// </summary>
        InitializationFailed = 100,

        /// <summary>
        /// The requested device was not found.
        /// </summary>
        DeviceNotFound = 101,

        /// <summary>
        /// Device is not available or is being used by another process.
        /// </summary>
        DeviceUnavailable = 102,

        /// <summary>
        /// Device capabilities are insufficient for the requested operation.
        /// </summary>
        InsufficientCapabilities = 103,

        /// <summary>
        /// Device driver error or incompatible driver version.
        /// </summary>
        DriverError = 104,

        /// <summary>
        /// General device error.
        /// </summary>
        DeviceError = 105,

        /// <summary>
        /// Out of GPU memory.
        /// </summary>
        OutOfMemory = 200,

        /// <summary>
        /// Memory allocation failed.
        /// </summary>
        MemoryAllocationFailed = 201,

        /// <summary>
        /// Memory deallocation failed.
        /// </summary>
        MemoryDeallocationFailed = 202,

        /// <summary>
        /// Memory copy operation failed.
        /// </summary>
        MemoryCopyFailed = 203,

        /// <summary>
        /// Invalid memory access or bounds violation.
        /// </summary>
        InvalidMemoryAccess = 204,

        /// <summary>
        /// Memory is not properly aligned.
        /// </summary>
        MemoryAlignment = 205,

        /// <summary>
        /// Kernel compilation failed.
        /// </summary>
        KernelCompilationFailed = 300,

        /// <summary>
        /// Kernel launch failed.
        /// </summary>
        KernelLaunchFailed = 301,

        /// <summary>
        /// Kernel execution timed out.
        /// </summary>
        KernelTimeout = 302,

        /// <summary>
        /// Invalid kernel configuration.
        /// </summary>
        InvalidKernelConfig = 303,

        /// <summary>
        /// Kernel execution was aborted.
        /// </summary>
        KernelAborted = 304,

        /// <summary>
        /// Kernel execution resulted in an illegal instruction.
        /// </summary>
        IllegalInstruction = 305,

        /// <summary>
        /// Stream operation failed.
        /// </summary>
        StreamError = 400,

        /// <summary>
        /// Stream synchronization failed.
        /// </summary>
        SynchronizationFailed = 401,

        /// <summary>
        /// Stream is not ready for the requested operation.
        /// </summary>
        StreamNotReady = 402,

        /// <summary>
        /// Context creation failed.
        /// </summary>
        ContextCreationFailed = 500,

        /// <summary>
        /// Context is invalid or has been destroyed.
        /// </summary>
        InvalidContext = 501,

        /// <summary>
        /// Context switch failed.
        /// </summary>
        ContextSwitchFailed = 502,

        /// <summary>
        /// Invalid parameter passed to API function.
        /// </summary>
        InvalidParameter = 600,

        /// <summary>
        /// Invalid buffer size.
        /// </summary>
        InvalidBufferSize = 601,

        /// <summary>
        /// Invalid device pointer.
        /// </summary>
        InvalidDevicePointer = 602,

        /// <summary>
        /// Invalid host pointer.
        /// </summary>
        InvalidHostPointer = 603,

        /// <summary>
        /// Operation is not supported by the device or runtime.
        /// </summary>
        NotSupported = 700,

        /// <summary>
        /// Feature is not implemented.
        /// </summary>
        NotImplemented = 701,

        /// <summary>
        /// Invalid operation for the current state.
        /// </summary>
        InvalidOperation = 702,

        /// <summary>
        /// Resource is busy and cannot be accessed.
        /// </summary>
        ResourceBusy = 703,

        /// <summary>
        /// Operation was cancelled.
        /// </summary>
        OperationCancelled = 800,

        /// <summary>
        /// Operation timed out.
        /// </summary>
        Timeout = 801,

        /// <summary>
        /// Connection to the device was lost.
        /// </summary>
        ConnectionLost = 802,

        /// <summary>
        /// Peer access error in multi-GPU scenarios.
        /// </summary>
        PeerAccessError = 900,

        /// <summary>
        /// Profile execution error.
        /// </summary>
        ProfilingError = 1000,

        /// <summary>
        /// Debug information is not available.
        /// </summary>
        DebugInfoUnavailable = 1001,

        /// <summary>
        /// Native AOT compilation error.
        /// </summary>
        AOTCompilationError = 1100,

        /// <summary>
        /// Source generation error.
        /// </summary>
        SourceGenerationError = 1101,

        /// <summary>
        /// Reflection operation not supported in AOT context.
        /// </summary>
        ReflectionNotSupported = 1102
    }

    /// <summary>
    /// Provides utility methods for working with GPU error codes.
    /// </summary>
    public static class GpuErrorCodeExtensions
    {
        /// <summary>
        /// Determines if the error code represents a success state.
        /// </summary>
        /// <param name="errorCode">The error code to check.</param>
        /// <returns>True if the error code represents success.</returns>
        public static bool IsSuccess(this GpuErrorCode errorCode) => errorCode == GpuErrorCode.Success;

        /// <summary>
        /// Determines if the error code represents a memory-related error.
        /// </summary>
        /// <param name="errorCode">The error code to check.</param>
        /// <returns>True if the error code is memory-related.</returns>
        public static bool IsMemoryError(this GpuErrorCode errorCode) =>
            errorCode >= GpuErrorCode.OutOfMemory && errorCode < GpuErrorCode.KernelCompilationFailed;

        /// <summary>
        /// Determines if the error code represents a kernel-related error.
        /// </summary>
        /// <param name="errorCode">The error code to check.</param>
        /// <returns>True if the error code is kernel-related.</returns>
        public static bool IsKernelError(this GpuErrorCode errorCode) =>
            errorCode >= GpuErrorCode.KernelCompilationFailed && errorCode < GpuErrorCode.StreamError;

        /// <summary>
        /// Determines if the error code represents a device-related error.
        /// </summary>
        /// <param name="errorCode">The error code to check.</param>
        /// <returns>True if the error code is device-related.</returns>
        public static bool IsDeviceError(this GpuErrorCode errorCode) =>
            errorCode >= GpuErrorCode.InitializationFailed && errorCode < GpuErrorCode.OutOfMemory;

        /// <summary>
        /// Determines if the error is recoverable.
        /// </summary>
        /// <param name="errorCode">The error code to check.</param>
        /// <returns>True if the error might be recoverable.</returns>
        public static bool IsRecoverable(this GpuErrorCode errorCode) => errorCode switch
        {
            GpuErrorCode.OutOfMemory => true,
            GpuErrorCode.DeviceUnavailable => true,
            GpuErrorCode.StreamNotReady => true,
            GpuErrorCode.ResourceBusy => true,
            GpuErrorCode.Timeout => true,
            _ => false
        };

        /// <summary>
        /// Gets the severity level of the error.
        /// </summary>
        /// <param name="errorCode">The error code to check.</param>
        /// <returns>The severity level of the error.</returns>
        public static ErrorSeverity GetSeverity(this GpuErrorCode errorCode) => errorCode switch
        {
            GpuErrorCode.Success => ErrorSeverity.Info,
            GpuErrorCode.StreamNotReady => ErrorSeverity.Warning,
            GpuErrorCode.ResourceBusy => ErrorSeverity.Warning,
            GpuErrorCode.Timeout => ErrorSeverity.Warning,
            GpuErrorCode.OperationCancelled => ErrorSeverity.Warning,
            GpuErrorCode.OutOfMemory => ErrorSeverity.Error,
            GpuErrorCode.DeviceNotFound => ErrorSeverity.Error,
            GpuErrorCode.KernelLaunchFailed => ErrorSeverity.Error,
            GpuErrorCode.DriverError => ErrorSeverity.Critical,
            GpuErrorCode.InitializationFailed => ErrorSeverity.Critical,
            GpuErrorCode.ContextCreationFailed => ErrorSeverity.Critical,
            _ => ErrorSeverity.Error
        };
    }

    /// <summary>
    /// Represents the severity level of an error.
    /// </summary>
    public enum ErrorSeverity
    {
        /// <summary>
        /// Informational message.
        /// </summary>
        Info,

        /// <summary>
        /// Warning that doesn't prevent operation.
        /// </summary>
        Warning,

        /// <summary>
        /// Error that prevents operation but may be recoverable.
        /// </summary>
        Error,

        /// <summary>
        /// Critical error that requires immediate attention.
        /// </summary>
        Critical
    }
}