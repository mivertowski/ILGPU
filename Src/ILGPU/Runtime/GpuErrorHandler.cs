// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: GpuErrorHandler.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Provides centralized error handling and recovery mechanisms for GPU operations.
    /// </summary>
    /// <remarks>
    /// This class implements modern error handling patterns including structured logging,
    /// error aggregation, and automatic recovery strategies. It addresses the critical
    /// need for consistent error handling across all ILGPU operations.
    /// </remarks>
    public static class GpuErrorHandler
    {
        private static readonly object ErrorCountLock = new();
        private static readonly Dictionary<GpuErrorCode, int> ErrorCounts = new();
        private static readonly List<IGpuErrorLogger> ErrorLoggers = new();

        /// <summary>
        /// Gets or sets whether automatic error recovery is enabled.
        /// </summary>
        public static bool AutoRecoveryEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of recovery attempts for recoverable errors.
        /// </summary>
        public static int MaxRecoveryAttempts { get; set; } = 3;

        /// <summary>
        /// Event raised when a GPU error occurs.
        /// </summary>
        public static event EventHandler<GpuErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Registers an error logger.
        /// </summary>
        /// <param name="logger">The logger to register.</param>
        public static void RegisterLogger(IGpuErrorLogger logger)
        {
            if (logger != null)
            {
                lock (ErrorLoggers)
                {
                    ErrorLoggers.Add(logger);
                }
            }
        }

        /// <summary>
        /// Unregisters an error logger.
        /// </summary>
        /// <param name="logger">The logger to unregister.</param>
        public static void UnregisterLogger(IGpuErrorLogger logger)
        {
            if (logger != null)
            {
                lock (ErrorLoggers)
                {
                    ErrorLoggers.Remove(logger);
                }
            }
        }

        /// <summary>
        /// Handles a GPU exception with automatic recovery if enabled.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="accelerator">The accelerator context.</param>
        /// <param name="operationName">The name of the operation for logging.</param>
        /// <returns>The result of the operation.</returns>
        public static T HandleOperation<T>(
            Func<T> operation,
            Accelerator? accelerator = null,
            string operationName = "Unknown")
        {
            return HandleOperation(operation, accelerator, operationName, CancellationToken.None);
        }

        /// <summary>
        /// Handles a GPU exception with automatic recovery if enabled.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="accelerator">The accelerator context.</param>
        /// <param name="operationName">The name of the operation for logging.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the operation.</returns>
        public static T HandleOperation<T>(
            Func<T> operation,
            Accelerator? accelerator,
            string operationName,
            CancellationToken cancellationToken)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            int attempts = 0;
            GpuException? lastException = null;

            while (attempts <= MaxRecoveryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = operation();
                    
                    // If we succeeded after retries, log the recovery
                    if (attempts > 0)
                    {
                        LogRecovery(operationName, attempts, lastException, accelerator);
                    }
                    
                    return result;
                }
                catch (GpuException ex)
                {
                    lastException = ex;
                    LogError(ex, operationName, accelerator);
                    IncrementErrorCount(ex.ErrorCode);
                    
                    // Raise error event
                    ErrorOccurred?.Invoke(null, new GpuErrorEventArgs(ex, operationName, attempts));

                    // Check if we should retry
                    if (!AutoRecoveryEnabled || !ex.ErrorCode.IsRecoverable() || attempts >= MaxRecoveryAttempts)
                    {
                        throw;
                    }

                    attempts++;
                    
                    // Apply recovery strategy
                    ApplyRecoveryStrategy(ex, accelerator, attempts);
                }
                catch (Exception ex)
                {
                    // Wrap non-GPU exceptions
                    var gpuEx = new GpuException($"Unexpected error in {operationName}: {ex.Message}", ex);
                    LogError(gpuEx, operationName, accelerator);
                    throw gpuEx;
                }
            }

            // This should never be reached, but included for completeness
            throw lastException ?? new GpuException($"Operation {operationName} failed after {MaxRecoveryAttempts} attempts");
        }

        /// <summary>
        /// Handles an async GPU operation with automatic recovery if enabled.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="operation">The async operation to execute.</param>
        /// <param name="accelerator">The accelerator context.</param>
        /// <param name="operationName">The name of the operation for logging.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the async operation.</returns>
        public static async Task<T> HandleOperationAsync<T>(
            Func<Task<T>> operation,
            Accelerator? accelerator = null,
            string operationName = "Unknown",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            int attempts = 0;
            GpuException? lastException = null;

            while (attempts <= MaxRecoveryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await operation().ConfigureAwait(false);
                    
                    if (attempts > 0)
                    {
                        LogRecovery(operationName, attempts, lastException, accelerator);
                    }
                    
                    return result;
                }
                catch (GpuException ex)
                {
                    lastException = ex;
                    LogError(ex, operationName, accelerator);
                    IncrementErrorCount(ex.ErrorCode);
                    
                    ErrorOccurred?.Invoke(null, new GpuErrorEventArgs(ex, operationName, attempts));

                    if (!AutoRecoveryEnabled || !ex.ErrorCode.IsRecoverable() || attempts >= MaxRecoveryAttempts)
                    {
                        throw;
                    }

                    attempts++;
                    ApplyRecoveryStrategy(ex, accelerator, attempts);
                }
                catch (Exception ex)
                {
                    var gpuEx = new GpuException($"Unexpected error in {operationName}: {ex.Message}", ex);
                    LogError(gpuEx, operationName, accelerator);
                    throw gpuEx;
                }
            }

            throw lastException ?? new GpuException($"Async operation {operationName} failed after {MaxRecoveryAttempts} attempts");
        }

        /// <summary>
        /// Creates a GPU exception with enhanced context information.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="errorCode">The GPU error code.</param>
        /// <param name="accelerator">The accelerator context.</param>
        /// <param name="innerException">The inner exception, if any.</param>
        /// <returns>A new GPU exception with context information.</returns>
        public static GpuException CreateException(
            string message,
            GpuErrorCode errorCode,
            Accelerator? accelerator = null,
            Exception? innerException = null)
        {
            var deviceInfo = accelerator != null ? DeviceErrorInfo.FromAccelerator(accelerator) : DeviceErrorInfo.Unknown;
            return new GpuException(message, errorCode, deviceInfo, innerException);
        }

        /// <summary>
        /// Creates a GPU memory exception with memory context.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="requestedSize">The requested memory size.</param>
        /// <param name="accelerator">The accelerator context.</param>
        /// <returns>A new GPU memory exception.</returns>
        public static GpuMemoryException CreateMemoryException(
            string message,
            long requestedSize,
            Accelerator? accelerator = null)
        {
            long availableSize = 0;
            try
            {
                availableSize = accelerator?.MemorySize ?? 0;
            }
            catch
            {
                // Ignore errors when getting available memory
            }

            return new GpuMemoryException(message, requestedSize, availableSize);
        }

        /// <summary>
        /// Gets error statistics.
        /// </summary>
        /// <returns>A dictionary of error codes and their occurrence counts.</returns>
        public static IReadOnlyDictionary<GpuErrorCode, int> GetErrorStatistics()
        {
            lock (ErrorCountLock)
            {
                return new Dictionary<GpuErrorCode, int>(ErrorCounts);
            }
        }

        /// <summary>
        /// Resets error statistics.
        /// </summary>
        public static void ResetErrorStatistics()
        {
            lock (ErrorCountLock)
            {
                ErrorCounts.Clear();
            }
        }

        private static void LogError(GpuException exception, string operationName, Accelerator? accelerator)
        {
            var severity = exception.ErrorCode.GetSeverity();
            var deviceInfo = accelerator != null ? DeviceErrorInfo.FromAccelerator(accelerator) : DeviceErrorInfo.Unknown;
            
            lock (ErrorLoggers)
            {
                foreach (var logger in ErrorLoggers)
                {
                    try
                    {
                        logger.LogError(exception, operationName, severity, deviceInfo);
                    }
                    catch
                    {
                        // Don't let logger errors break the application
                    }
                }
            }

            // Also write to debug output
            Debug.WriteLine($"[ILGPU Error] {severity}: {exception.Message} in {operationName} (Code: {exception.ErrorCode})");
        }

        private static void LogRecovery(string operationName, int attempts, GpuException? lastException, Accelerator? accelerator)
        {
            var deviceInfo = accelerator != null ? DeviceErrorInfo.FromAccelerator(accelerator) : DeviceErrorInfo.Unknown;
            
            lock (ErrorLoggers)
            {
                foreach (var logger in ErrorLoggers)
                {
                    try
                    {
                        logger.LogRecovery(operationName, attempts, lastException, deviceInfo);
                    }
                    catch
                    {
                        // Don't let logger errors break the application
                    }
                }
            }

            Debug.WriteLine($"[ILGPU Recovery] Operation {operationName} recovered after {attempts} attempts");
        }

        private static void IncrementErrorCount(GpuErrorCode errorCode)
        {
            lock (ErrorCountLock)
            {
                ErrorCounts[errorCode] = ErrorCounts.GetValueOrDefault(errorCode) + 1;
            }
        }

        private static void ApplyRecoveryStrategy(GpuException exception, Accelerator? accelerator, int attempt)
        {
            var delay = TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, attempt - 1), 5000)); // Exponential backoff

            switch (exception.ErrorCode)
            {
                case GpuErrorCode.OutOfMemory:
                    // For memory errors, force garbage collection and wait
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(delay);
                    break;

                case GpuErrorCode.DeviceUnavailable:
                case GpuErrorCode.ResourceBusy:
                    // For busy resources, just wait
                    Thread.Sleep(delay);
                    break;

                case GpuErrorCode.StreamNotReady:
                    // For stream issues, try to synchronize
                    try
                    {
                        accelerator?.DefaultStream.Synchronize();
                    }
                    catch
                    {
                        // If sync fails, just wait
                        Thread.Sleep(delay);
                    }
                    break;

                default:
                    // For other recoverable errors, just wait
                    Thread.Sleep(delay);
                    break;
            }
        }
    }

    /// <summary>
    /// Interface for GPU error loggers.
    /// </summary>
    public interface IGpuErrorLogger
    {
        /// <summary>
        /// Logs a GPU error.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="severity">The severity of the error.</param>
        /// <param name="deviceInfo">Information about the device.</param>
        void LogError(GpuException exception, string operationName, ErrorSeverity severity, DeviceErrorInfo deviceInfo);

        /// <summary>
        /// Logs a successful recovery.
        /// </summary>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="attempts">The number of attempts it took to recover.</param>
        /// <param name="lastException">The last exception before recovery.</param>
        /// <param name="deviceInfo">Information about the device.</param>
        void LogRecovery(string operationName, int attempts, GpuException? lastException, DeviceErrorInfo deviceInfo);
    }

    /// <summary>
    /// Event arguments for GPU error events.
    /// </summary>
    public sealed class GpuErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the GpuErrorEventArgs class.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="attempt">The current attempt number.</param>
        public GpuErrorEventArgs(GpuException exception, string operationName, int attempt)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            OperationName = operationName ?? "Unknown";
            Attempt = attempt;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the exception that occurred.
        /// </summary>
        public GpuException Exception { get; }

        /// <summary>
        /// Gets the name of the operation.
        /// </summary>
        public string OperationName { get; }

        /// <summary>
        /// Gets the current attempt number.
        /// </summary>
        public int Attempt { get; }

        /// <summary>
        /// Gets the timestamp when the error occurred.
        /// </summary>
        public DateTime Timestamp { get; }
    }
}