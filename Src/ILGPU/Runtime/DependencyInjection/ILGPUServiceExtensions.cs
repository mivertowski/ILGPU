// ---------------------------------------------------------------------------------------
//                                   ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: ILGPUServiceExtensions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

#if NET6_0_OR_GREATER

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace ILGPU.Runtime.DependencyInjection
{
    /// <summary>
    /// Extension methods for configuring ILGPU services in dependency injection.
    /// </summary>
    public static class ILGPUServiceExtensions
    {
        /// <summary>
        /// Adds ILGPU services to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional configuration delegate.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddILGPU(
            this IServiceCollection services,
            Action<ILGPUOptions>? configure = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Configure options
            if (configure != null)
                services.Configure(configure);
            else
                services.Configure<ILGPUOptions>(_ => { });

            // Register core services
            services.TryAddSingleton<IContextFactory, DefaultContextFactory>();
            services.TryAddSingleton<IAcceleratorFactory, DefaultAcceleratorFactory>();
            services.TryAddSingleton<IMemoryManager, DefaultMemoryManager>();
            services.TryAddSingleton<IKernelManager, DefaultKernelManager>();
            
            // Register context as singleton (expensive to create)
            services.TryAddSingleton<Context>(provider =>
            {
                var factory = provider.GetRequiredService<IContextFactory>();
                return factory.CreateContext();
            });

            return services;
        }

        /// <summary>
        /// Adds ILGPU services with a specific accelerator type.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="acceleratorType">The preferred accelerator type.</param>
        /// <param name="configure">Optional configuration delegate.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddILGPU(
            this IServiceCollection services,
            AcceleratorType acceleratorType,
            Action<ILGPUOptions>? configure = null)
        {
            return services.AddILGPU(options =>
            {
                options.PreferredAcceleratorType = acceleratorType;
                configure?.Invoke(options);
            });
        }

        // TODO: Add memory pooling and profiling services once the interfaces are defined
        // These extensions are placeholders for future implementation
    }
}

#endif