// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: AsyncKernelExecution.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents the result of an asynchronous kernel execution.
    /// </summary>
    /// <remarks>
    /// This class provides a Task-based API for kernel execution that integrates
    /// with .NET's async/await patterns, addressing the issue where ILGPU's kernel
    /// execution was purely synchronous and could cause thread blocking in async contexts.
    /// </remarks>
    public sealed class KernelExecutionResult
    {
        private readonly AcceleratorStream stream;
        private readonly TaskCompletionSource<object?> completionSource;
        private readonly CancellationTokenRegistration cancellationRegistration;

        /// <summary>
        /// Initializes a new kernel execution result.
        /// </summary>
        /// <param name="stream">The accelerator stream used for kernel execution.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        internal KernelExecutionResult(AcceleratorStream stream, CancellationToken cancellationToken)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            completionSource = new TaskCompletionSource<object?>();
            
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() => 
                    completionSource.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
            }

            // Start the completion monitoring on a background thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await stream.SynchronizeAsync().ConfigureAwait(false);
                    completionSource.TrySetResult(null);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception ex)
                {
                    // We need to catch all exceptions to properly forward them to the TaskCompletionSource
                    completionSource.TrySetException(ex);
                }
#pragma warning restore CA1031 // Do not catch general exception types
            }, CancellationToken.None);
        }

        /// <summary>
        /// Gets the task representing the completion of the kernel execution.
        /// </summary>
        public Task Task => completionSource.Task;

        /// <summary>
        /// Gets the accelerator stream used for this kernel execution.
        /// </summary>
        public AcceleratorStream Stream => stream;

        /// <summary>
        /// Waits for the kernel execution to complete.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the kernel execution finishes.</returns>
        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.CanBeCanceled)
            {
                return Task.WhenAny(Task, Task.Delay(-1, cancellationToken))
                    .ContinueWith(t => Task, TaskScheduler.Default)
                    .Unwrap();
            }
            return Task;
        }

        /// <summary>
        /// Synchronously waits for the kernel execution to complete.
        /// </summary>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <returns>True if the execution completed within the timeout.</returns>
        public bool Wait(TimeSpan timeout) => Task.Wait(timeout);

        /// <summary>
        /// Synchronously waits for the kernel execution to complete.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public void Wait(CancellationToken cancellationToken = default) => Task.Wait(cancellationToken);

        /// <summary>
        /// Releases all resources used by this kernel execution result.
        /// </summary>
        public void Dispose() => cancellationRegistration.Dispose();
    }

    /// <summary>
    /// Provides extension methods for asynchronous kernel execution.
    /// </summary>
    /// <remarks>
    /// These extensions enable modern async/await patterns for ILGPU kernel execution,
    /// providing a non-blocking alternative to the traditional synchronous API.
    /// </remarks>
    public static class AsyncKernelExtensions
    {
        /// <summary>
        /// Launches a kernel asynchronously and returns a task representing the completion.
        /// </summary>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync(
            this Action<AcceleratorStream, KernelConfig> kernel,
            AcceleratorStream stream,
            KernelConfig config,
            CancellationToken cancellationToken = default)
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            // Launch the kernel synchronously (this is fast - just queues work on GPU)
            kernel(stream, config);
            
            // Return a task that completes when the stream synchronizes
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches a kernel with one parameter asynchronously.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<T1>(
            this Action<AcceleratorStream, KernelConfig, T1> kernel,
            AcceleratorStream stream,
            KernelConfig config,
            T1 arg1,
            CancellationToken cancellationToken = default)
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, config, arg1);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches a kernel with two parameters asynchronously.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<T1, T2>(
            this Action<AcceleratorStream, KernelConfig, T1, T2> kernel,
            AcceleratorStream stream,
            KernelConfig config,
            T1 arg1,
            T2 arg2,
            CancellationToken cancellationToken = default)
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, config, arg1, arg2);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches a kernel with three parameters asynchronously.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <typeparam name="T3">The type of the third parameter.</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="arg3">The third kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<T1, T2, T3>(
            this Action<AcceleratorStream, KernelConfig, T1, T2, T3> kernel,
            AcceleratorStream stream,
            KernelConfig config,
            T1 arg1,
            T2 arg2,
            T3 arg3,
            CancellationToken cancellationToken = default)
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, config, arg1, arg2, arg3);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches an implicitly grouped kernel asynchronously using index dimensions.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<TIndex>(
            this Action<AcceleratorStream, TIndex> kernel,
            AcceleratorStream stream,
            TIndex extent,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, extent);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches an implicitly grouped kernel with one parameter asynchronously.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<TIndex, T1>(
            this Action<AcceleratorStream, TIndex, T1> kernel,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, extent, arg1);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches an implicitly grouped kernel with two parameters asynchronously.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<TIndex, T1, T2>(
            this Action<AcceleratorStream, TIndex, T1, T2> kernel,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1,
            T2 arg2,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, extent, arg1, arg2);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }

        /// <summary>
        /// Launches an implicitly grouped kernel with three parameters asynchronously.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <typeparam name="T3">The type of the third parameter.</typeparam>
        /// <param name="kernel">The kernel delegate to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="arg3">The third kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LaunchAsync<TIndex, T1, T2, T3>(
            this Action<AcceleratorStream, TIndex, T1, T2, T3> kernel,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1,
            T2 arg2,
            T3 arg3,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            if (kernel is null)
                throw new ArgumentNullException(nameof(kernel));
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            kernel(stream, extent, arg1, arg2, arg3);
            return new KernelExecutionResult(stream, cancellationToken).Task;
        }
    }
}
