// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: AsyncKernelLoaders.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Provides extension methods for loading and launching kernels with async/await patterns.
    /// </summary>
    /// <remarks>
    /// These methods extend the existing ILGPU kernel loading system to provide
    /// Task-based APIs that integrate with modern .NET async programming patterns,
    /// addressing the limitation where kernel execution was purely synchronous.
    /// </remarks>
    public static class AsyncKernelLoaders
    {
        #region LoadKernelAsync Explicitly Grouped

        /// <summary>
        /// Loads and launches a kernel asynchronously without parameters.
        /// </summary>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadKernelAsync(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            KernelConfig config,
            CancellationToken cancellationToken = default)
        {
            var kernel = accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig>>(method);
            return kernel.LaunchAsync(stream, config, cancellationToken);
        }

        /// <summary>
        /// Loads and launches a kernel asynchronously with one parameter.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadKernelAsync<T1>(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            KernelConfig config,
            T1 arg1,
            CancellationToken cancellationToken = default)
        {
            var kernel = accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1>>(method);
            return kernel.LaunchAsync(stream, config, arg1, cancellationToken);
        }

        /// <summary>
        /// Loads and launches a kernel asynchronously with two parameters.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadKernelAsync<T1, T2>(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            KernelConfig config,
            T1 arg1,
            T2 arg2,
            CancellationToken cancellationToken = default)
        {
            var kernel = accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2>>(method);
            return kernel.LaunchAsync(stream, config, arg1, arg2, cancellationToken);
        }

        /// <summary>
        /// Loads and launches a kernel asynchronously with three parameters.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <typeparam name="T3">The type of the third parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="arg3">The third kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadKernelAsync<T1, T2, T3>(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            KernelConfig config,
            T1 arg1,
            T2 arg2,
            T3 arg3,
            CancellationToken cancellationToken = default)
        {
            var kernel = accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3>>(method);
            return kernel.LaunchAsync(stream, config, arg1, arg2, arg3, cancellationToken);
        }

        #endregion

        #region LoadStreamKernelAsync Explicitly Grouped (Default Stream)

        /// <summary>
        /// Loads and launches a kernel asynchronously using the default stream without parameters.
        /// </summary>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadStreamKernelAsync(
            this Accelerator accelerator,
            MethodInfo method,
            KernelConfig config,
            CancellationToken cancellationToken = default) => accelerator.LoadKernelAsync(method, accelerator.DefaultStream, config, cancellationToken);

        /// <summary>
        /// Loads and launches a kernel asynchronously using the default stream with one parameter.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadStreamKernelAsync<T1>(
            this Accelerator accelerator,
            MethodInfo method,
            KernelConfig config,
            T1 arg1,
            CancellationToken cancellationToken = default) => accelerator.LoadKernelAsync(method, accelerator.DefaultStream, config, arg1, cancellationToken);

        /// <summary>
        /// Loads and launches a kernel asynchronously using the default stream with two parameters.
        /// </summary>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="config">The kernel configuration.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadStreamKernelAsync<T1, T2>(
            this Accelerator accelerator,
            MethodInfo method,
            KernelConfig config,
            T1 arg1,
            T2 arg2,
            CancellationToken cancellationToken = default) => accelerator.LoadKernelAsync(method, accelerator.DefaultStream, config, arg1, arg2, cancellationToken);

        #endregion

        #region LoadAutoGroupedKernelAsync

        /// <summary>
        /// Loads and launches an auto-grouped kernel asynchronously without parameters.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadAutoGroupedKernelAsync<TIndex>(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            TIndex extent,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            var kernel = accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex>>(method);
            return kernel.LaunchAsync(stream, extent, cancellationToken);
        }

        /// <summary>
        /// Loads and launches an auto-grouped kernel asynchronously with one parameter.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadAutoGroupedKernelAsync<TIndex, T1>(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            var kernel = accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1>>(method);
            return kernel.LaunchAsync(stream, extent, arg1, cancellationToken);
        }

        /// <summary>
        /// Loads and launches an auto-grouped kernel asynchronously with two parameters.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <typeparam name="T2">The type of the second parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="stream">The accelerator stream to use.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="arg2">The second kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadAutoGroupedKernelAsync<TIndex, T1, T2>(
            this Accelerator accelerator,
            MethodInfo method,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1,
            T2 arg2,
            CancellationToken cancellationToken = default)
            where TIndex : struct
        {
            var kernel = accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2>>(method);
            return kernel.LaunchAsync(stream, extent, arg1, arg2, cancellationToken);
        }

        #endregion

        #region LoadAutoGroupedStreamKernelAsync (Default Stream)

        /// <summary>
        /// Loads and launches an auto-grouped kernel asynchronously using the default stream without parameters.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadAutoGroupedStreamKernelAsync<TIndex>(
            this Accelerator accelerator,
            MethodInfo method,
            TIndex extent,
            CancellationToken cancellationToken = default)
            where TIndex : struct => accelerator.LoadAutoGroupedKernelAsync(method, accelerator.DefaultStream, extent, cancellationToken);

        /// <summary>
        /// Loads and launches an auto-grouped kernel asynchronously using the default stream with one parameter.
        /// </summary>
        /// <typeparam name="TIndex">The index type (Index1D, Index2D, or Index3D).</typeparam>
        /// <typeparam name="T1">The type of the first parameter.</typeparam>
        /// <param name="accelerator">The accelerator to use.</param>
        /// <param name="method">The method representing the kernel to launch.</param>
        /// <param name="extent">The kernel extent.</param>
        /// <param name="arg1">The first kernel argument.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous kernel execution.</returns>
        public static Task LoadAutoGroupedStreamKernelAsync<TIndex, T1>(
            this Accelerator accelerator,
            MethodInfo method,
            TIndex extent,
            T1 arg1,
            CancellationToken cancellationToken = default)
            where TIndex : struct => accelerator.LoadAutoGroupedKernelAsync(method, accelerator.DefaultStream, extent, arg1, cancellationToken);

        #endregion

        #region High-Level Async Kernel Execution Helpers

        /// <summary>
        /// Executes multiple kernels concurrently and waits for all to complete.
        /// </summary>
        /// <param name="kernelTasks">The kernel execution tasks to wait for.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when all kernels finish.</returns>
        public static Task WhenAllAsync(
            Task[] kernelTasks,
            CancellationToken cancellationToken = default)
        {
            if (kernelTasks is null)
                throw new ArgumentNullException(nameof(kernelTasks));

            return Task.WhenAll(kernelTasks).WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Executes multiple kernels and waits for the first one to complete.
        /// </summary>
        /// <param name="kernelTasks">The kernel execution tasks to monitor.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the first kernel finishes.</returns>
        public static Task<Task> WhenAnyAsync(
            Task[] kernelTasks,
            CancellationToken cancellationToken = default)
        {
            if (kernelTasks is null)
                throw new ArgumentNullException(nameof(kernelTasks));

            return Task.WhenAny(kernelTasks).WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Provides a convenient async pattern for kernel execution with resource cleanup.
        /// </summary>
        /// <typeparam name="T">The type of the kernel execution context.</typeparam>
        /// <param name="setupFunc">Function to set up the kernel execution context.</param>
        /// <param name="kernelFunc">Function that executes the kernel asynchronously.</param>
        /// <param name="cleanupAction">Action to clean up resources after execution.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the async kernel execution with cleanup.</returns>
        public static async Task WithKernelAsync<T>(
            Func<T> setupFunc,
            Func<T, Task> kernelFunc,
            Action<T> cleanupAction,
            CancellationToken cancellationToken = default)
        {
            if (setupFunc is null)
                throw new ArgumentNullException(nameof(setupFunc));
            if (kernelFunc is null)
                throw new ArgumentNullException(nameof(kernelFunc));
            if (cleanupAction is null)
                throw new ArgumentNullException(nameof(cleanupAction));

            var context = setupFunc();
            try
            {
                await kernelFunc(context).ConfigureAwait(false);
            }
            finally
            {
                cleanupAction(context);
            }
        }

        #endregion
    }
}
