// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: ConsoleGpuErrorLogger.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU.Runtime
{
    /// <summary>
    /// A simple console-based GPU error logger for development and debugging.
    /// </summary>
    /// <remarks>
    /// This logger provides basic console output for GPU errors and recovery events.
    /// It's suitable for development scenarios and simple applications.
    /// </remarks>
    public sealed class ConsoleGpuErrorLogger : IGpuErrorLogger
    {
        /// <summary>
        /// Gets or sets whether to include timestamp in log messages.
        /// </summary>
        public bool IncludeTimestamp { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include device information in log messages.
        /// </summary>
        public bool IncludeDeviceInfo { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include stack trace for critical errors.
        /// </summary>
        public bool IncludeStackTrace { get; set; } = false;

        /// <summary>
        /// Gets or sets the minimum severity level to log.
        /// </summary>
        public ErrorSeverity MinimumSeverity { get; set; } = ErrorSeverity.Warning;

        /// <summary>
        /// Logs a GPU error to the console.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="severity">The severity of the error.</param>
        /// <param name="deviceInfo">Information about the device.</param>
        public void LogError(GpuException exception, string operationName, ErrorSeverity severity, DeviceErrorInfo deviceInfo)
        {
            if (exception == null || severity < MinimumSeverity)
                return;

            var color = GetConsoleColor(severity);
            var timestamp = IncludeTimestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " : "";
            var severityText = GetSeverityText(severity);
            
            Console.ForegroundColor = color;
            Console.Write($"{timestamp}[ILGPU {severityText}]");
            Console.ResetColor();
            
            Console.WriteLine($" {exception.ErrorCode} in {operationName}: {exception.Message}");

            if (IncludeDeviceInfo && deviceInfo.IsValid)
            {
                Console.WriteLine($"  Device: {deviceInfo}");
            }

            if (exception.Context.Count > 0)
            {
                Console.WriteLine("  Context:");
                foreach (var kvp in exception.Context)
                {
                    Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
                }
            }

            var suggestions = exception.RecoverySuggestions;
            if (suggestions != null)
            {
                bool hasSuggestions = false;
                Console.WriteLine("  Recovery Suggestions:");
                foreach (var suggestion in suggestions)
                {
                    Console.WriteLine($"    • {suggestion}");
                    hasSuggestions = true;
                }
                
                if (!hasSuggestions)
                {
                    Console.WriteLine("    • No specific suggestions available");
                }
            }

            if (IncludeStackTrace && severity == ErrorSeverity.Critical && exception.StackTrace != null)
            {
                Console.WriteLine("  Stack Trace:");
                Console.WriteLine($"    {exception.StackTrace.Replace("\n", "\n    ")}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Logs a successful recovery to the console.
        /// </summary>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="attempts">The number of attempts it took to recover.</param>
        /// <param name="lastException">The last exception before recovery.</param>
        /// <param name="deviceInfo">Information about the device.</param>
        public void LogRecovery(string operationName, int attempts, GpuException? lastException, DeviceErrorInfo deviceInfo)
        {
            var timestamp = IncludeTimestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " : "";
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{timestamp}[ILGPU RECOVERY]");
            Console.ResetColor();
            
            Console.WriteLine($" Operation {operationName} recovered after {attempts} attempt(s)");

            if (lastException != null)
            {
                Console.WriteLine($"  Last Error: {lastException.ErrorCode} - {lastException.Message}");
            }

            if (IncludeDeviceInfo && deviceInfo.IsValid)
            {
                Console.WriteLine($"  Device: {deviceInfo}");
            }

            Console.WriteLine();
        }

        private static ConsoleColor GetConsoleColor(ErrorSeverity severity) => severity switch
        {
            ErrorSeverity.Info => ConsoleColor.White,
            ErrorSeverity.Warning => ConsoleColor.Yellow,
            ErrorSeverity.Error => ConsoleColor.Red,
            ErrorSeverity.Critical => ConsoleColor.Magenta,
            _ => ConsoleColor.Gray
        };

        private static string GetSeverityText(ErrorSeverity severity) => severity switch
        {
            ErrorSeverity.Info => "INFO",
            ErrorSeverity.Warning => "WARN",
            ErrorSeverity.Error => "ERROR",
            ErrorSeverity.Critical => "CRITICAL",
            _ => "UNKNOWN"
        };
    }
}