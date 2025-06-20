// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: IMemoryPoolFactory.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU.Runtime.MemoryPooling
{
    /// <summary>
    /// Factory interface for creating memory pools.
    /// </summary>
    public interface IMemoryPoolFactory
    {
        /// <summary>
        /// Creates a memory pool for the specified accelerator and element type.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The accelerator to create the pool for.</param>
        /// <param name="configuration">The pool configuration.</param>
        /// <returns>A new memory pool instance.</returns>
        IMemoryPool<T> CreatePool<T>(Accelerator accelerator, MemoryPoolConfiguration? configuration = null) 
            where T : unmanaged;
    }

    /// <summary>
    /// Default implementation of the memory pool factory.
    /// </summary>
    public sealed class DefaultMemoryPoolFactory : IMemoryPoolFactory
    {
        /// <inheritdoc/>
        public IMemoryPool<T> CreatePool<T>(Accelerator accelerator, MemoryPoolConfiguration? configuration = null) 
            where T : unmanaged
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));

            return new AdaptiveMemoryPool<T>(accelerator, configuration);
        }
    }

    /// <summary>
    /// Predefined memory pool configuration presets.
    /// </summary>
    public enum MemoryPoolPreset
    {
        /// <summary>
        /// Default configuration suitable for most applications.
        /// </summary>
        Default,

        /// <summary>
        /// High-performance configuration with larger pools and more retention.
        /// </summary>
        HighPerformance,

        /// <summary>
        /// Memory-efficient configuration with smaller pools and aggressive trimming.
        /// </summary>
        MemoryEfficient,

        /// <summary>
        /// Development configuration with detailed statistics and frequent trimming.
        /// </summary>
        Development
    }
}