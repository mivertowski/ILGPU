// ---------------------------------------------------------------------------------------
//                                   ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: ILGPUServices.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

#if NET6_0_OR_GREATER

using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using ILGPU.Runtime.Velocity;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ILGPU.Runtime.DependencyInjection
{
    /// <summary>
    /// Interface for ILGPU context factory.
    /// </summary>
    public interface IContextFactory
    {
        /// <summary>
        /// Creates a new ILGPU context.
        /// </summary>
        /// <returns>The created context.</returns>
        Context CreateContext();
    }

    /// <summary>
    /// Interface for accelerator factory.
    /// </summary>
    public interface IAcceleratorFactory
    {
        /// <summary>
        /// Creates an accelerator of the specified type.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="acceleratorType">The accelerator type.</param>
        /// <returns>The created accelerator.</returns>
        Accelerator CreateAccelerator(Context context, AcceleratorType acceleratorType);

        /// <summary>
        /// Gets all available devices of the specified type.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="acceleratorType">The accelerator type.</param>
        /// <returns>Available devices.</returns>
        IReadOnlyList<Device> GetDevices(Context context, AcceleratorType acceleratorType);
    }

    /// <summary>
    /// Interface for memory manager.
    /// </summary>
    public interface IMemoryManager
    {
        /// <summary>
        /// Allocates a memory buffer of the specified size.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The target accelerator.</param>
        /// <param name="length">The buffer length.</param>
        /// <returns>The allocated buffer.</returns>
        MemoryBuffer1D<T, Stride1D.Dense> Allocate<T>(Accelerator accelerator, long length)
            where T : unmanaged;
    }

    /// <summary>
    /// Interface for kernel manager.
    /// </summary>
    public interface IKernelManager
    {
        /// <summary>
        /// Loads a kernel from the specified method.
        /// </summary>
        /// <param name="accelerator">The target accelerator.</param>
        /// <param name="method">The kernel method.</param>
        /// <returns>The loaded kernel.</returns>
        Kernel LoadKernel(Accelerator accelerator, System.Reflection.MethodInfo method);

        /// <summary>
        /// Loads a generic kernel.
        /// </summary>
        /// <typeparam name="TDelegate">The kernel delegate type.</typeparam>
        /// <param name="accelerator">The target accelerator.</param>
        /// <param name="kernelMethod">The kernel method delegate.</param>
        /// <returns>The loaded kernel.</returns>
        TDelegate LoadKernel<TDelegate>(Accelerator accelerator, MethodInfo kernelMethod)
            where TDelegate : Delegate;
    }

    /// <summary>
    /// Default implementation of context factory.
    /// </summary>
    internal sealed class DefaultContextFactory : IContextFactory
    {
        private readonly IOptions<ILGPUOptions> _options;

        public DefaultContextFactory(IOptions<ILGPUOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Context CreateContext()
        {
            var builder = Context.Create();
            
            var options = _options.Value;
            if (options.EnableDebugAssertions)
                builder.DebugConfig(enableAssertions: true);

            // Apply custom context configuration if provided
            options.ContextConfigurator?.Invoke(builder);

            return builder.ToContext();
        }
    }

    /// <summary>
    /// Default implementation of accelerator factory.
    /// </summary>
    internal sealed class DefaultAcceleratorFactory : IAcceleratorFactory
    {
        private readonly IOptions<ILGPUOptions> _options;

        public DefaultAcceleratorFactory(IOptions<ILGPUOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Accelerator CreateAccelerator(Context context, AcceleratorType acceleratorType)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var devices = GetDevices(context, acceleratorType);
            if (devices.Count == 0)
                throw new InvalidOperationException($"No devices found for accelerator type: {acceleratorType}");

            // Use device selector if provided, otherwise use first device
            var selectedDevice = _options.Value.DeviceSelector?.Invoke(devices) ?? devices[0];
            
            return selectedDevice.CreateAccelerator(context);
        }

        public IReadOnlyList<Device> GetDevices(Context context, AcceleratorType acceleratorType)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return acceleratorType switch
            {
                AcceleratorType.CPU => EnumerateDevices(context.GetCPUDevices()),
                AcceleratorType.Cuda => EnumerateDevices(context.GetCudaDevices()),
                AcceleratorType.OpenCL => EnumerateDevices(context.GetCLDevices()),
                AcceleratorType.Velocity => EnumerateDevices(context.GetVelocityDevices()),
                _ => throw new ArgumentException($"Unsupported accelerator type: {acceleratorType}", nameof(acceleratorType))
            };
        }

        private static List<Device> EnumerateDevices<TDevice>(Context.DeviceCollection<TDevice> deviceCollection)
            where TDevice : Device
        {
            var devices = new List<Device>();
            foreach (var device in deviceCollection)
                devices.Add(device);
            return devices;
        }
    }

    /// <summary>
    /// Default implementation of memory manager.
    /// </summary>
    internal sealed class DefaultMemoryManager : IMemoryManager
    {
        public MemoryBuffer1D<T, Stride1D.Dense> Allocate<T>(Accelerator accelerator, long length)
            where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive");

            return accelerator.Allocate1D<T>(length);
        }
    }

    /// <summary>
    /// Default implementation of kernel manager.
    /// </summary>
    internal sealed class DefaultKernelManager : IKernelManager
    {
        public Kernel LoadKernel(Accelerator accelerator, System.Reflection.MethodInfo method)
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            return accelerator.LoadKernel(method);
        }

        public TDelegate LoadKernel<TDelegate>(Accelerator accelerator, System.Reflection.MethodInfo kernelMethod)
            where TDelegate : Delegate
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));
            if (kernelMethod == null)
                throw new ArgumentNullException(nameof(kernelMethod));

            return accelerator.LoadKernel<TDelegate>(kernelMethod);
        }
    }
}

#endif