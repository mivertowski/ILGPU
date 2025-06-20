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
using Microsoft.Extensions.Options;
using System;
using ILGPU.Runtime.MemoryPooling;
using ILGPU.Runtime.Profiling;

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

            // Add memory pooling by default if enabled in options
            services.TryAddSingleton<MemoryPoolConfiguration>(provider =>
            {
                var options = provider.GetService<IOptions<ILGPUOptions>>();
                if (options?.Value?.EnableMemoryPooling == true)
                {
                    return options.Value.MemoryPoolConfiguration;
                }
                return new MemoryPoolConfiguration();
            });

            services.TryAddSingleton<IMemoryPoolFactory, DefaultMemoryPoolFactory>();
            services.TryAddScoped(typeof(IMemoryPool<>), typeof(AdaptiveMemoryPool<>));

            // Add performance profiling by default if enabled in options
            services.TryAddScoped<IPerformanceProfiler>(provider =>
            {
                var options = provider.GetService<IOptions<ILGPUOptions>>();
                var accelerator = provider.GetRequiredService<Accelerator>();
                var enableProfiling = options?.Value?.EnableProfiling ?? false;
                return new PerformanceProfiler(accelerator, enableProfiling);
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
            Action<ILGPUOptions>? configure = null) => services.AddILGPU(options =>
                                                                {
                                                                    options.PreferredAcceleratorType = acceleratorType;
                                                                    configure?.Invoke(options);
                                                                });

        /// <summary>
        /// Adds memory pooling services to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional memory pool configuration delegate.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddMemoryPooling(
            this IServiceCollection services,
            Action<MemoryPoolConfiguration>? configure = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Register memory pool configuration
            services.TryAddSingleton<MemoryPoolConfiguration>(provider =>
            {
                var config = new MemoryPoolConfiguration();
                configure?.Invoke(config);
                config.Validate();
                return config;
            });

            // Register memory pool factory
            services.TryAddSingleton<IMemoryPoolFactory, DefaultMemoryPoolFactory>();

            // Register generic memory pool as scoped (per accelerator)
            services.TryAddScoped(typeof(IMemoryPool<>), typeof(AdaptiveMemoryPool<>));

            return services;
        }

        /// <summary>
        /// Adds memory pooling services with a specific configuration preset.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="preset">The configuration preset to use.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddMemoryPooling(
            this IServiceCollection services,
            MemoryPoolPreset preset) => preset switch
            {
                MemoryPoolPreset.HighPerformance => services.AddMemoryPooling(_ => MemoryPoolConfiguration.CreateHighPerformance()),
                MemoryPoolPreset.MemoryEfficient => services.AddMemoryPooling(_ => MemoryPoolConfiguration.CreateMemoryEfficient()),
                MemoryPoolPreset.Development => services.AddMemoryPooling(_ => MemoryPoolConfiguration.CreateDevelopment()),
                _ => services.AddMemoryPooling()
            };

        /// <summary>
        /// Adds performance profiling services to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="enableByDefault">Whether profiling is enabled by default.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPerformanceProfiling(
            this IServiceCollection services,
            bool enableByDefault = false)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            services.TryAddScoped<IPerformanceProfiler>(provider =>
            {
                var accelerator = provider.GetRequiredService<Accelerator>();
                return new PerformanceProfiler(accelerator, enableByDefault);
            });

            return services;
        }

        /// <summary>
        /// Adds performance profiling services with detailed configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Profiling configuration delegate.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPerformanceProfiling(
            this IServiceCollection services,
            Action<ProfilingOptions> configure)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (configure != null)
                services.Configure(configure);

            services.TryAddScoped<IPerformanceProfiler>(provider =>
            {
                var accelerator = provider.GetRequiredService<Accelerator>();
                var options = provider.GetService<IOptions<ProfilingOptions>>();
                var enableProfiling = options?.Value?.EnableKernelProfiling ?? false;
                return new PerformanceProfiler(accelerator, enableProfiling);
            });

            return services;
        }
    }
}

#endif