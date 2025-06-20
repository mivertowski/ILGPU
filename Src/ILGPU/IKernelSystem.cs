// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: IKernelSystem.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;

namespace ILGPU
{
    /// <summary>
    /// Represents an abstract kernel system interface that provides common functionality
    /// for both runtime and compile-time kernel generation systems.
    /// </summary>
    /// <remarks>
    /// This interface enables ILGPU to work with both RuntimeSystem (for JIT scenarios)
    /// and CompiledKernelSystem (for AOT scenarios) through a common abstraction.
    /// </remarks>
    public interface IKernelSystem : IDisposable, ICache
    {
        /// <summary>
        /// Gets the assembly name used by this kernel system.
        /// </summary>
        string AssemblyName { get; }

        /// <summary>
        /// Gets a value indicating whether this system supports dynamic code generation.
        /// </summary>
        bool SupportsDynamicGeneration { get; }

        /// <summary>
        /// Gets a value indicating whether this system is optimized for AOT compilation.
        /// </summary>
        bool IsAOTCompatible { get; }

        /// <summary>
        /// Clears internal caches based on the given cache mode.
        /// </summary>
        /// <param name="mode">The cache clearing mode.</param>
        new void ClearCache(ClearCacheMode mode);
    }

    /// <summary>
    /// Provides factory methods for creating appropriate kernel system instances
    /// based on compilation configuration.
    /// </summary>
    public static class KernelSystemFactory
    {
        /// <summary>
        /// Creates the appropriate kernel system based on current compilation mode.
        /// </summary>
        /// <returns>An IKernelSystem instance optimized for the current runtime.</returns>
        public static IKernelSystem Create()
        {
#if NATIVE_AOT || AOT_COMPATIBLE
            return new CompiledKernelSystem();
#else
            // Runtime detection of AOT mode - if System.Reflection.Emit is not available,
            // fall back to AOT-compatible system
            try
            {
                // Test if dynamic code generation is supported
                System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    new System.Reflection.AssemblyName("TestAOTDetection"),
                    System.Reflection.Emit.AssemblyBuilderAccess.Run);
                return new RuntimeSystemAdapter();
            }
            catch (System.PlatformNotSupportedException)
            {
                // Running in AOT mode, use compiled system
                return new CompiledKernelSystem();
            }
#endif
        }

        /// <summary>
        /// Creates a kernel system optimized for the specified runtime mode.
        /// </summary>
        /// <param name="useAOT">True to force AOT-compatible system, false for runtime system.</param>
        /// <returns>An IKernelSystem instance for the specified mode.</returns>
        public static IKernelSystem Create(bool useAOT) => 
            useAOT ? new CompiledKernelSystem() : 
#if NATIVE_AOT || AOT_COMPATIBLE
            new CompiledKernelSystem();
#else
            new RuntimeSystemAdapter();
#endif
    }

    /// <summary>
    /// Adapter that wraps RuntimeSystem to implement IKernelSystem interface.
    /// </summary>
    /// <remarks>
    /// This adapter is only available in JIT compilation mode.
    /// </remarks>
#if !NATIVE_AOT && !AOT_COMPATIBLE
    internal sealed class RuntimeSystemAdapter : IKernelSystem
    {
        /// <summary>
        /// Initializes a new RuntimeSystemAdapter.
        /// </summary>
        public RuntimeSystemAdapter()
        {
            RuntimeSystem = new RuntimeSystem();
        }

        /// <inheritdoc/>
        public string AssemblyName => RuntimeSystem.AssemblyName;

        /// <inheritdoc/>
        public bool SupportsDynamicGeneration => true;

        /// <inheritdoc/>
        public bool IsAOTCompatible => false;

        /// <summary>
        /// Gets the underlying RuntimeSystem instance.
        /// </summary>
        public RuntimeSystem RuntimeSystem { get; }

        /// <inheritdoc/>
        public void ClearCache(ClearCacheMode mode) => RuntimeSystem.ClearCache(mode);

        /// <inheritdoc/>
        public void Dispose() => RuntimeSystem.Dispose();
    }
#endif
}