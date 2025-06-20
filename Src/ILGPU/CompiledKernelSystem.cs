// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2020-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: CompiledKernelSystem.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.IL;
using ILGPU.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ILGPU
{
    /// <summary>
    /// Represents the compile-time ILGPU kernel system that replaces dynamic
    /// assembly generation with source generators for native AOT compatibility.
    /// </summary>
    public sealed class CompiledKernelSystem : DisposeBase, IKernelSystem
    {
        #region Constants

        /// <summary>
        /// The prefix for generated kernel methods.
        /// </summary>
        internal const string GeneratedKernelPrefix = "CompiledKernel_";

        /// <summary>
        /// The namespace for generated kernel types.
        /// </summary>
        internal const string GeneratedKernelNamespace = "ILGPU.Runtime.Generated";

        #endregion

        #region Nested Types

        /// <summary>
        /// A scoped lock that can be used in combination with a
        /// <see cref="CompiledKernelSystem"/> instance.
        /// </summary>
        public readonly struct ScopedLock : IDisposable
        {
            private readonly WriteScopedLock writeScope;

            internal ScopedLock(CompiledKernelSystem parent)
            {
                writeScope = parent.systemLock.EnterWriteScope();
                Parent = parent;
                Version = parent.version;
            }

            /// <summary>
            /// Returns the parent compiled kernel system instance.
            /// </summary>
            public CompiledKernelSystem Parent { get; }

            /// <summary>
            /// Returns the original system version this lock has been created from.
            /// </summary>
            public int Version { get; }

            /// <summary>
            /// Releases the lock.
            /// </summary>
            public readonly void Dispose()
            {
                // Verify the system version
                Debug.Assert(
                    Version == Parent.version,
                    "Invalid concurrent modification detected");
                writeScope.Dispose();
            }
        }

        /// <summary>
        /// Represents a compile-time method builder that generates source code
        /// instead of dynamic IL.
        /// </summary>
        public readonly struct MethodBuilder
        {
            private readonly StringBuilder sourceBuilder;
            private readonly CompileTimeILEmitter emitter;

            /// <summary>
            /// Constructs a new compile-time method builder.
            /// </summary>
            /// <param name="methodName">The method name.</param>
            /// <param name="returnType">The return type.</param>
            /// <param name="parameterTypes">The parameter types.</param>
            public MethodBuilder(
                string methodName,
                Type returnType,
                Type[] parameterTypes)
            {
                sourceBuilder = new StringBuilder();
                emitter = new CompileTimeILEmitter(sourceBuilder);
                
                MethodName = methodName;
                ReturnType = returnType;
                ParameterTypes = parameterTypes;

                // Generate method signature
                GenerateMethodSignature();
            }

            /// <summary>
            /// Returns the method name.
            /// </summary>
            public string MethodName { get; }

            /// <summary>
            /// Returns the return type.
            /// </summary>
            public Type ReturnType { get; }

            /// <summary>
            /// Returns the parameter types.
            /// </summary>
            public Type[] ParameterTypes { get; }

            /// <summary>
            /// Returns the compile-time IL emitter.
            /// </summary>
            public CompileTimeILEmitter ILEmitter => emitter;

            /// <summary>
            /// Generates the method signature.
            /// </summary>
            private void GenerateMethodSignature()
            {
                sourceBuilder.AppendLine($"        /// <summary>");
                sourceBuilder.AppendLine($"        /// Generated AOT-compatible kernel method: {MethodName}");
                sourceBuilder.AppendLine($"        /// </summary>");
                sourceBuilder.Append($"        public static {GetTypeString(ReturnType)} {MethodName}(");
                
                for (int i = 0; i < ParameterTypes.Length; i++)
                {
                    if (i > 0) sourceBuilder.Append(", ");
                    sourceBuilder.Append($"{GetTypeString(ParameterTypes[i])} arg{i}");
                }
                
                sourceBuilder.AppendLine(")");
                sourceBuilder.AppendLine("        {");
            }

            /// <summary>
            /// Finishes the method generation and returns the generated source code.
            /// </summary>
            /// <returns>The generated method source code.</returns>
            public string Finish()
            {
                emitter.Finish();
                sourceBuilder.AppendLine("        }");
                return sourceBuilder.ToString();
            }

            /// <summary>
            /// Gets the C# string representation of a type.
            /// </summary>
            private static string GetTypeString(Type type)
            {
                if (type == null) return "object";
                
                return type switch
                {
                    _ when type == typeof(int) => "int",
                    _ when type == typeof(long) => "long",
                    _ when type == typeof(float) => "float",
                    _ when type == typeof(double) => "double",
                    _ when type == typeof(bool) => "bool",
                    _ when type == typeof(byte) => "byte",
                    _ when type == typeof(sbyte) => "sbyte",
                    _ when type == typeof(short) => "short",
                    _ when type == typeof(ushort) => "ushort",
                    _ when type == typeof(uint) => "uint",
                    _ when type == typeof(ulong) => "ulong",
                    _ when type == typeof(char) => "char",
                    _ when type == typeof(string) => "string",
                    _ when type == typeof(object) => "object",
                    _ when type == typeof(void) => "void",
                    _ => type.Name
                };
            }
        }

        #endregion

        #region Static

        /// <summary>
        /// The globally unique system version.
        /// </summary>
        private static volatile int globalVersion;

        /// <summary>
        /// Determines the next global system version.
        /// </summary>
        private static int GetNextVersion() =>
            Interlocked.Add(ref globalVersion, 1);

        #endregion

        #region Instance

        private readonly ReaderWriterLockSlim systemLock = new ReaderWriterLockSlim(
            LockRecursionPolicy.SupportsRecursion);

        private readonly ConcurrentDictionary<string, CompiledKernelEntry> compiledKernels = new();
        private readonly StringBuilder generatedSource = new();

        private volatile int version;
        private volatile int methodCounter;

        /// <summary>
        /// Constructs a new compiled kernel system.
        /// </summary>
        public CompiledKernelSystem()
        {
            ReloadSystem();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the current system version.
        /// </summary>
        public int Version => version;

        /// <summary>
        /// Returns the number of compiled kernels.
        /// </summary>
        public int CompiledKernelCount => compiledKernels.Count;

        /// <summary>
        /// Gets the assembly name used by this kernel system.
        /// </summary>
        public string AssemblyName => GeneratedKernelNamespace;

        /// <summary>
        /// Gets a value indicating whether this system supports dynamic code generation.
        /// </summary>
        public bool SupportsDynamicGeneration => false;

        /// <summary>
        /// Gets a value indicating whether this system is optimized for AOT compilation.
        /// </summary>
        public bool IsAOTCompatible => true;

        #endregion

        #region Methods

        /// <summary>
        /// Reloads the compiled kernel system.
        /// </summary>
        private void ReloadSystem()
        {
            using var writerLock = systemLock.EnterWriteScope();

            version = GetNextVersion();
            compiledKernels.Clear();
            generatedSource.Clear();
            
            // Initialize the generated source structure
            generatedSource.AppendLine("// <auto-generated />");
            generatedSource.AppendLine("// Generated AOT-compatible kernel system");
            generatedSource.AppendLine();
            generatedSource.AppendLine("using System;");
            generatedSource.AppendLine("using ILGPU;");
            generatedSource.AppendLine("using ILGPU.Runtime;");
            generatedSource.AppendLine();
            generatedSource.AppendLine($"namespace {GeneratedKernelNamespace}");
            generatedSource.AppendLine("{");
            generatedSource.AppendLine("    /// <summary>");
            generatedSource.AppendLine("    /// AOT-compatible compiled kernels");
            generatedSource.AppendLine("    /// </summary>");
            generatedSource.AppendLine("    public static class CompiledKernels");
            generatedSource.AppendLine("    {");
        }

        /// <summary>
        /// Defines a new compile-time method.
        /// </summary>
        /// <param name="returnType">The return type.</param>
        /// <param name="parameterTypes">All parameter types.</param>
        /// <param name="methodBuilder">The method builder.</param>
        /// <returns>The acquired scoped lock.</returns>
        public ScopedLock DefineCompiledMethod(
            Type returnType,
            Type[] parameterTypes,
            out MethodBuilder methodBuilder)
        {
            var scopedLock = new ScopedLock(this);

            var methodName = $"{GeneratedKernelPrefix}{Interlocked.Increment(ref methodCounter)}";
            methodBuilder = new MethodBuilder(methodName, returnType, parameterTypes);

            return scopedLock;
        }

        /// <summary>
        /// Registers a compiled kernel method.
        /// </summary>
        /// <param name="kernelName">The kernel name.</param>
        /// <param name="method">The compiled method.</param>
        /// <param name="sourceCode">The generated source code.</param>
        public void RegisterCompiledKernel(string kernelName, MethodInfo method, string sourceCode)
        {
            var entry = new CompiledKernelEntry(kernelName, method, sourceCode);
            compiledKernels.TryAdd(kernelName, entry);
            
            // Add to the generated source
            using var writerLock = systemLock.EnterWriteScope();
            generatedSource.AppendLine(sourceCode);
        }

        /// <summary>
        /// Gets a compiled kernel method by name.
        /// </summary>
        /// <param name="kernelName">The kernel name.</param>
        /// <returns>The compiled kernel entry, or null if not found.</returns>
        public CompiledKernelEntry? GetCompiledKernel(string kernelName)
        {
            return compiledKernels.TryGetValue(kernelName, out var entry) ? entry : null;
        }

        /// <summary>
        /// Gets all compiled kernels.
        /// </summary>
        /// <returns>A collection of all compiled kernel entries.</returns>
        public IEnumerable<CompiledKernelEntry> GetAllCompiledKernels()
        {
            return compiledKernels.Values;
        }

        /// <summary>
        /// Gets the complete generated source code.
        /// </summary>
        /// <returns>The generated source code for all kernels.</returns>
        public string GetGeneratedSource()
        {
            using var readerLock = systemLock.EnterReadScope();
            
            var completeSource = new StringBuilder(generatedSource.ToString());
            completeSource.AppendLine("    }");
            completeSource.AppendLine("}");
            
            return completeSource.ToString();
        }

        #endregion

        #region ICache

        /// <summary>
        /// Clears all internal caches.
        /// </summary>
        /// <param name="mode">
        /// Passing <see cref="ClearCacheMode.Everything"/>, causes a reload of the
        /// compiled kernel system.
        /// </param>
        public void ClearCache(ClearCacheMode mode)
        {
            if (mode == ClearCacheMode.Everything)
                ReloadSystem();
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes the internal system lock.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                systemLock.Dispose();
            base.Dispose(disposing);
        }

        #endregion
    }

    /// <summary>
    /// Represents a compiled kernel entry.
    /// </summary>
    public sealed class CompiledKernelEntry
    {
        /// <summary>
        /// Constructs a new compiled kernel entry.
        /// </summary>
        /// <param name="kernelName">The kernel name.</param>
        /// <param name="method">The compiled method.</param>
        /// <param name="sourceCode">The generated source code.</param>
        public CompiledKernelEntry(string kernelName, MethodInfo method, string sourceCode)
        {
            KernelName = kernelName;
            Method = method;
            SourceCode = sourceCode;
        }

        /// <summary>
        /// Returns the kernel name.
        /// </summary>
        public string KernelName { get; }

        /// <summary>
        /// Returns the compiled method.
        /// </summary>
        public MethodInfo Method { get; }

        /// <summary>
        /// Returns the generated source code.
        /// </summary>
        public string SourceCode { get; }
    }
}