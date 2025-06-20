// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: AOTDelegateResolver.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Provides AOT-compatible delegate resolution for kernel launching and intrinsic mapping.
    /// </summary>
    /// <remarks>
    /// This resolver automatically selects between compile-time generated delegates (AOT)
    /// and runtime-generated delegates (JIT) based on compilation mode.
    /// </remarks>
    public static class AOTDelegateResolver
    {
        private static readonly ConcurrentDictionary<string, Delegate> DelegateCache = new();

        /// <summary>
        /// Creates or retrieves a delegate for the specified method and type.
        /// </summary>
        /// <typeparam name="TDelegate">The delegate type to create.</typeparam>
        /// <param name="method">The method to create a delegate for.</param>
        /// <param name="target">The target instance for instance methods (null for static).</param>
        /// <returns>A delegate of type TDelegate.</returns>
        public static TDelegate CreateDelegate<TDelegate>(MethodInfo method, object? target = null)
            where TDelegate : Delegate =>
#if NATIVE_AOT || AOT_COMPATIBLE
            return CreateAOTDelegate<TDelegate>(method, target);
#else
            CreateRuntimeDelegate<TDelegate>(method, target);
#endif



        /// <summary>
        /// Creates or retrieves a delegate for the specified method and delegate type.
        /// </summary>
        /// <param name="delegateType">The delegate type to create.</param>
        /// <param name="method">The method to create a delegate for.</param>
        /// <param name="target">The target instance for instance methods (null for static).</param>
        /// <returns>A delegate of the specified type.</returns>
        public static Delegate CreateDelegate(Type delegateType, MethodInfo method, object? target = null) =>
#if NATIVE_AOT || AOT_COMPATIBLE
            return CreateAOTDelegate(delegateType, method, target);
#else
            CreateRuntimeDelegate(delegateType, method, target);
#endif



#if NATIVE_AOT || AOT_COMPATIBLE
        /// <summary>
        /// Creates a delegate using AOT-compatible method resolution.
        /// </summary>
        private static TDelegate CreateAOTDelegate<TDelegate>(MethodInfo method, object? target)
            where TDelegate : Delegate
        {
            var cacheKey = GetCacheKey(typeof(TDelegate), method, target);
            
            return (TDelegate)DelegateCache.GetOrAdd(cacheKey, _ =>
            {
                // In AOT mode, we rely on source generators to provide pre-compiled delegates
                // This will be populated by the AOT source generators
                var generatedDelegate = AOTKernelRegistry.GetDelegate<TDelegate>(method, target);
                if (generatedDelegate != null)
                    return generatedDelegate;

                // Fallback for unsupported scenarios - will throw at runtime
                throw new NotSupportedException(
                    $"Delegate creation for method '{method.Name}' of type '{typeof(TDelegate).Name}' " +
                    "is not supported in AOT mode. Ensure the method is registered with AOT source generators.");
            });
        }

        private static Delegate CreateAOTDelegate(Type delegateType, MethodInfo method, object? target)
        {
            var cacheKey = GetCacheKey(delegateType, method, target);
            
            return DelegateCache.GetOrAdd(cacheKey, _ =>
            {
                var generatedDelegate = AOTKernelRegistry.GetDelegate(delegateType, method, target);
                if (generatedDelegate != null)
                    return generatedDelegate;

                throw new NotSupportedException(
                    $"Delegate creation for method '{method.Name}' of type '{delegateType.Name}' " +
                    "is not supported in AOT mode. Ensure the method is registered with AOT source generators.");
            });
        }
#else
        /// <summary>
        /// Creates a delegate using runtime reflection.
        /// </summary>
        private static TDelegate CreateRuntimeDelegate<TDelegate>(MethodInfo method, object? target)
            where TDelegate : Delegate
        {
            var cacheKey = GetCacheKey(typeof(TDelegate), method, target);
            
            return (TDelegate)DelegateCache.GetOrAdd(cacheKey, _ =>
            {
                return method.CreateDelegate<TDelegate>(target);
            });
        }

        private static Delegate CreateRuntimeDelegate(Type delegateType, MethodInfo method, object? target)
        {
            var cacheKey = GetCacheKey(delegateType, method, target);
            
            return DelegateCache.GetOrAdd(cacheKey, _ =>
            {
                return method.CreateDelegate(delegateType, target);
            });
        }
#endif

        /// <summary>
        /// Generates a cache key for the delegate.
        /// </summary>
        private static string GetCacheKey(Type delegateType, MethodInfo method, object? target)
        {
            var targetKey = target?.GetHashCode().ToString() ?? "static";
            return $"{delegateType.FullName}:{method.DeclaringType?.FullName}.{method.Name}:{targetKey}";
        }

        /// <summary>
        /// Clears the delegate cache.
        /// </summary>
        public static void ClearCache() => DelegateCache.Clear();
    }

    /// <summary>
    /// Registry for AOT-generated kernel delegates.
    /// This class will be populated by source generators during AOT compilation.
    /// </summary>
    public static class AOTKernelRegistry
    {
#if NATIVE_AOT || AOT_COMPATIBLE
        private static readonly ConcurrentDictionary<string, Delegate> GeneratedDelegates = new();

        /// <summary>
        /// Registers a pre-compiled delegate for AOT scenarios.
        /// This method is called by source generators.
        /// </summary>
        public static void RegisterDelegate<TDelegate>(string methodKey, TDelegate del)
            where TDelegate : Delegate
        {
            GeneratedDelegates.TryAdd(methodKey, del);
        }

        /// <summary>
        /// Retrieves a pre-compiled delegate for the specified method.
        /// </summary>
        internal static TDelegate? GetDelegate<TDelegate>(MethodInfo method, object? target)
            where TDelegate : Delegate
        {
            var key = GetMethodKey(method, target);
            return GeneratedDelegates.TryGetValue(key, out var del) ? (TDelegate)del : null;
        }

        internal static Delegate? GetDelegate(Type delegateType, MethodInfo method, object? target)
        {
            var key = GetMethodKey(method, target);
            return GeneratedDelegates.TryGetValue(key, out var del) ? del : null;
        }

        private static string GetMethodKey(MethodInfo method, object? target)
        {
            var targetKey = target?.GetType().FullName ?? "static";
            return $"{method.DeclaringType?.FullName}.{method.Name}:{targetKey}";
        }
#else
        internal static TDelegate? GetDelegate<TDelegate>(MethodInfo method, object? target)
            where TDelegate : Delegate => null;

        internal static Delegate? GetDelegate(Type delegateType, MethodInfo method, object? target) => null;
#endif
    }
}