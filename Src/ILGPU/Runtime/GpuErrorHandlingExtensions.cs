// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: GpuErrorHandlingExtensions.cs
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
    /// Provides extension methods for enhanced GPU error handling on common ILGPU operations.
    /// </summary>
    /// <remarks>
    /// These extensions wrap common ILGPU operations with enhanced error handling,
    /// providing automatic recovery, detailed error information, and consistent logging.
    /// </remarks>
    public static class GpuErrorHandlingExtensions
    {
        /// <summary>
        /// Allocates a 1D memory buffer with enhanced error handling.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The accelerator to allocate memory on.</param>
        /// <param name="length">The length of the buffer.</param>
        /// <returns>The allocated memory buffer.</returns>
        /// <exception cref="GpuMemoryException">Thrown when memory allocation fails.</exception>
        public static MemoryBuffer1D<T, Stride1D.Dense> SafeAllocate1D<T>(
            this Accelerator accelerator, 
            long length)
            where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));

            return GpuErrorHandler.HandleOperation(
                () => accelerator.Allocate1D<T>(length),
                accelerator,
                $"Allocate1D<{typeof(T).Name}>[{length}]");
        }

        /// <summary>
        /// Allocates a 2D memory buffer with enhanced error handling.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The accelerator to allocate memory on.</param>
        /// <param name="width">The width of the buffer.</param>
        /// <param name="height">The height of the buffer.</param>
        /// <returns>The allocated memory buffer.</returns>
        /// <exception cref="GpuMemoryException">Thrown when memory allocation fails.</exception>
        public static MemoryBuffer2D<T, Stride2D.DenseX> SafeAllocate2D<T>(
            this Accelerator accelerator,
            int width,
            int height)
            where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));

            return GpuErrorHandler.HandleOperation(
                () => accelerator.Allocate2DDenseX<T>(new LongIndex2D(width, height)),
                accelerator,
                $"Allocate2D<{typeof(T).Name}>[{width}x{height}]");
        }

        /// <summary>
        /// Loads a 1D kernel with enhanced error handling.
        /// </summary>
        /// <typeparam name="T">The kernel parameter type.</typeparam>
        /// <param name="accelerator">The accelerator to load the kernel on.</param>
        /// <param name="kernelMethod">The 1D kernel method.</param>
        /// <returns>The loaded kernel.</returns>
        /// <exception cref="GpuKernelException">Thrown when kernel loading fails.</exception>
        public static Action<Index1D, T> SafeLoadKernel<T>(
            this Accelerator accelerator,
            Action<Index1D, T> kernelMethod)
            where T : struct
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (kernelMethod == null)
                throw new ArgumentNullException(nameof(kernelMethod));

            return GpuErrorHandler.HandleOperation(
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, T>(kernelMethod),
                accelerator,
                $"LoadKernel<Action<Index1D, {typeof(T).Name}>>");
        }

        /// <summary>
        /// Loads an auto-grouped kernel with enhanced error handling.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T">The kernel parameter type.</typeparam>
        /// <param name="accelerator">The accelerator to load the kernel on.</param>
        /// <param name="kernelMethod">The kernel method.</param>
        /// <returns>The loaded kernel.</returns>
        /// <exception cref="GpuKernelException">Thrown when kernel loading fails.</exception>
        public static Action<TIndex, T> SafeLoadAutoGroupedKernel<TIndex, T>(
            this Accelerator accelerator,
            Action<TIndex, T> kernelMethod)
            where TIndex : struct, IIndex
            where T : struct
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (kernelMethod == null)
                throw new ArgumentNullException(nameof(kernelMethod));

            return GpuErrorHandler.HandleOperation(
                () => accelerator.LoadAutoGroupedStreamKernel<TIndex, T>(kernelMethod),
                accelerator,
                $"LoadAutoGroupedKernel<{typeof(TIndex).Name}, {typeof(T).Name}>");
        }

        /// <summary>
        /// Synchronizes an accelerator stream with enhanced error handling.
        /// </summary>
        /// <param name="stream">The stream to synchronize.</param>
        /// <exception cref="GpuException">Thrown when synchronization fails.</exception>
        public static void SafeSynchronize(this AcceleratorStream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            GpuErrorHandler.HandleOperation(
                () => { stream.Synchronize(); return true; },
                stream.Accelerator,
                "StreamSynchronize");
        }

        /// <summary>
        /// Asynchronously synchronizes an accelerator stream with enhanced error handling.
        /// </summary>
        /// <param name="stream">The stream to synchronize.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the synchronization operation.</returns>
        /// <exception cref="GpuException">Thrown when synchronization fails.</exception>
        public static Task SafeSynchronizeAsync(
            this AcceleratorStream stream,
            CancellationToken cancellationToken = default)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            return GpuErrorHandler.HandleOperationAsync(
                async () => { await stream.SynchronizeAsync().ConfigureAwait(false); return true; },
                stream.Accelerator,
                "AsyncStreamSynchronize",
                cancellationToken);
        }

        /// <summary>
        /// Copies memory from CPU to GPU with enhanced error handling.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="buffer">The GPU buffer.</param>
        /// <param name="data">The CPU data to copy.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <exception cref="GpuException">Thrown when the copy operation fails.</exception>
        public static void SafeCopyFromCPU<T>(
            this MemoryBuffer1D<T, Stride1D.Dense> buffer,
            ReadOnlySpan<T> data,
            AcceleratorStream? stream = null)
            where T : unmanaged
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            var actualStream = stream ?? buffer.Accelerator.DefaultStream;
            
            var dataArray = data.ToArray();
            GpuErrorHandler.HandleOperation(
                () => { buffer.CopyFromCPU(actualStream, dataArray); return true; },
                buffer.Accelerator,
                $"CopyFromCPU<{typeof(T).Name}>[{dataArray.Length}]");
        }

        /// <summary>
        /// Copies memory from GPU to CPU with enhanced error handling.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="buffer">The GPU buffer.</param>
        /// <param name="data">The CPU data to copy to.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <exception cref="GpuException">Thrown when the copy operation fails.</exception>
        public static void SafeCopyToCPU<T>(
            this MemoryBuffer1D<T, Stride1D.Dense> buffer,
            Span<T> data,
            AcceleratorStream? stream = null)
            where T : unmanaged
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            var actualStream = stream ?? buffer.Accelerator.DefaultStream;
            
            var dataArray = new T[data.Length];
            GpuErrorHandler.HandleOperation(
                () => { buffer.CopyToCPU(actualStream, dataArray); return true; },
                buffer.Accelerator,
                $"CopyToCPU<{typeof(T).Name}>[{dataArray.Length}]");
            dataArray.CopyTo(data);
        }

        /// <summary>
        /// Creates an accelerator with enhanced error handling and automatic device selection.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="acceleratorType">The preferred accelerator type.</param>
        /// <param name="deviceSelector">Optional device selector function.</param>
        /// <returns>The created accelerator.</returns>
        /// <exception cref="GpuDeviceException">Thrown when accelerator creation fails.</exception>
        public static Accelerator SafeCreateAccelerator(
            this Context context,
            AcceleratorType acceleratorType,
            Func<Device[], Device>? deviceSelector = null)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return GpuErrorHandler.HandleOperation(() =>
            {
                Device[] devices;
                switch (acceleratorType)
                {
                    case AcceleratorType.CPU:
                        {
                            var cpuDevices = context.GetDevices<Runtime.CPU.CPUDevice>();
                            devices = new Device[cpuDevices.Count];
                            for (int i = 0; i < cpuDevices.Count; i++)
                                devices[i] = cpuDevices[i];
                            break;
                        }
                    case AcceleratorType.Cuda:
                        {
                            var cudaDevices = context.GetDevices<Runtime.Cuda.CudaDevice>();
                            devices = new Device[cudaDevices.Count];
                            for (int i = 0; i < cudaDevices.Count; i++)
                                devices[i] = cudaDevices[i];
                            break;
                        }
                    case AcceleratorType.OpenCL:
                        {
                            var clDevices = context.GetDevices<Runtime.OpenCL.CLDevice>();
                            devices = new Device[clDevices.Count];
                            for (int i = 0; i < clDevices.Count; i++)
                                devices[i] = clDevices[i];
                            break;
                        }
                    case AcceleratorType.Velocity:
                        {
                            var velocityDevices = context.GetDevices<Runtime.Velocity.VelocityDevice>();
                            devices = new Device[velocityDevices.Count];
                            for (int i = 0; i < velocityDevices.Count; i++)
                                devices[i] = velocityDevices[i];
                            break;
                        }
                    default:
                        throw new ArgumentException($"Unsupported accelerator type: {acceleratorType}");
                }

                if (devices.Length == 0)
                {
                    throw GpuErrorHandler.CreateException(
                        $"No devices found for accelerator type: {acceleratorType}",
                        GpuErrorCode.DeviceNotFound);
                }

                var selectedDevice = deviceSelector?.Invoke(devices) ?? devices[0];
                return selectedDevice.CreateAccelerator(context);
            },
            null,
            $"CreateAccelerator[{acceleratorType}]");
        }
    }
}
