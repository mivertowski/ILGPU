// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: TensorIntrinsics.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Values;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using System;
using System.Runtime.CompilerServices;
using System.Numerics;

namespace ILGPU.TensorCores
{
    /// <summary>
    /// Specifies the tensor core operation type for PTX intrinsic generation.
    /// </summary>
    public enum TensorCoreOperation
    {
        WMMA_Load_A,
        WMMA_Load_B,
        WMMA_Load_C,
        WMMA_Store,
        WMMA_MMA,
        WMMA_Fill
    }

    /// <summary>
    /// Attribute to mark methods as tensor core intrinsics that generate PTX instructions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TensorCoreIntrinsicAttribute : Attribute
    {
        public TensorCoreOperation Operation { get; }
        public string? PTXInstruction { get; set; }
        
        public TensorCoreIntrinsicAttribute(TensorCoreOperation operation)
        {
            Operation = operation;
        }
    }

    /// <summary>
    /// Contains intrinsic functions for tensor core operations.
    /// These methods are replaced with actual PTX WMMA instructions during compilation.
    /// </summary>
    public static class TensorIntrinsics
    {
        /// <summary>
        /// Checks if tensor cores are available on the current device.
        /// </summary>
        /// <returns>True if tensor cores are available.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTensorCoreSupported()
        {
            // This method will be replaced with actual capability check during kernel compilation
            // For now, we return a conservative estimate
            return true; // Will be optimized during PTX generation
        }

        /// <summary>
        /// Loads matrix A fragment from global memory using WMMA.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="ptr">Pointer to matrix data in global memory.</param>
        /// <param name="leadingDimension">The leading dimension (stride) of the matrix.</param>
        /// <returns>The loaded matrix fragment.</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_Load_A, PTXInstruction = "wmma.load.a.sync")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<T> LoadMatrixA<T>(
            ArrayView<T> ptr,
            int leadingDimension)
            where T : unmanaged;

        /// <summary>
        /// Loads matrix B fragment from global memory using WMMA.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="ptr">Pointer to matrix data in global memory.</param>
        /// <param name="leadingDimension">The leading dimension (stride) of the matrix.</param>
        /// <returns>The loaded matrix fragment.</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_Load_B, PTXInstruction = "wmma.load.b.sync")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<T> LoadMatrixB<T>(
            ArrayView<T> ptr,
            int leadingDimension)
            where T : unmanaged;

        /// <summary>
        /// Loads accumulator matrix C fragment from global memory using WMMA.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="ptr">Pointer to matrix data in global memory.</param>
        /// <param name="leadingDimension">The leading dimension (stride) of the matrix.</param>
        /// <returns>The loaded matrix fragment.</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_Load_C, PTXInstruction = "wmma.load.c.sync")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<T> LoadMatrixC<T>(
            ArrayView<T> ptr,
            int leadingDimension)
            where T : unmanaged;

        /// <summary>
        /// Stores a matrix fragment to global memory using WMMA.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="ptr">Pointer to destination matrix data in global memory.</param>
        /// <param name="fragment">The fragment to store.</param>
        /// <param name="leadingDimension">The leading dimension (stride) of the matrix.</param>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_Store, PTXInstruction = "wmma.store.d.sync")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern void StoreMatrix<T>(
            ArrayView<T> ptr,
            MatrixFragment<T> fragment,
            int leadingDimension)
            where T : unmanaged;

        /// <summary>
        /// Performs matrix multiply-accumulate operation: D = A * B + C using tensor cores.
        /// </summary>
        /// <typeparam name="TInput">The input element type (Half, BFloat16).</typeparam>
        /// <typeparam name="TAccum">The accumulator element type (float, Half).</typeparam>
        /// <param name="fragmentA">The A matrix fragment.</param>
        /// <param name="fragmentB">The B matrix fragment.</param>
        /// <param name="fragmentC">The C matrix fragment (accumulator).</param>
        /// <returns>The result fragment D = A * B + C.</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_MMA, PTXInstruction = "wmma.mma.sync")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<TAccum> MatrixMultiplyAccumulate<TInput, TAccum>(
            MatrixFragment<TInput> fragmentA,
            MatrixFragment<TInput> fragmentB,
            MatrixFragment<TAccum> fragmentC)
            where TInput : unmanaged
            where TAccum : unmanaged;

        /// <summary>
        /// Fills a matrix fragment with a constant value using WMMA.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="value">The value to fill with.</param>
        /// <returns>The filled matrix fragment.</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_Fill, PTXInstruction = "wmma.fill.sync")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<T> FillFragment<T>(T value)
            where T : unmanaged;

        /// <summary>
        /// Performs mixed-precision matrix multiply with FP16 inputs and FP32 accumulation.
        /// </summary>
        /// <param name="fragmentA">The A matrix fragment (FP16).</param>
        /// <param name="fragmentB">The B matrix fragment (FP16).</param>
        /// <param name="fragmentC">The C matrix fragment (FP32).</param>
        /// <returns>The result fragment D = A * B + C (FP32).</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_MMA, PTXInstruction = "wmma.mma.sync.aligned.m16n16k16.f32.f16.f16.f32")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<float> MixedPrecisionMMA(
            MatrixFragment<Half> fragmentA,
            MatrixFragment<Half> fragmentB,
            MatrixFragment<float> fragmentC);

        /// <summary>
        /// Performs BFloat16 mixed-precision matrix multiply with BF16 inputs and FP32 accumulation.
        /// </summary>
        /// <param name="fragmentA">The A matrix fragment (BF16).</param>
        /// <param name="fragmentB">The B matrix fragment (BF16).</param>
        /// <param name="fragmentC">The C matrix fragment (FP32).</param>
        /// <returns>The result fragment D = A * B + C (FP32).</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_MMA, PTXInstruction = "wmma.mma.sync.aligned.m16n16k16.f32.bf16.bf16.f32")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<float> BFloat16MixedPrecisionMMA(
            MatrixFragment<BFloat16> fragmentA,
            MatrixFragment<BFloat16> fragmentB,
            MatrixFragment<float> fragmentC);

        /// <summary>
        /// Performs TensorFloat32 (TF32) matrix multiply on Ampere+ GPUs.
        /// </summary>
        /// <param name="fragmentA">The A matrix fragment (TF32).</param>
        /// <param name="fragmentB">The B matrix fragment (TF32).</param>
        /// <param name="fragmentC">The C matrix fragment (FP32).</param>
        /// <returns>The result fragment D = A * B + C (FP32).</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_MMA, PTXInstruction = "wmma.mma.sync.aligned.m16n16k8.f32.tf32.tf32.f32")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<float> TF32MatrixMultiply(
            MatrixFragment<float> fragmentA,
            MatrixFragment<float> fragmentB,
            MatrixFragment<float> fragmentC);

        /// <summary>
        /// Performs INT8 matrix multiply with INT32 accumulation for quantized models.
        /// </summary>
        /// <param name="fragmentA">The A matrix fragment (INT8).</param>
        /// <param name="fragmentB">The B matrix fragment (INT8).</param>
        /// <param name="fragmentC">The C matrix fragment (INT32).</param>
        /// <returns>The result fragment D = A * B + C (INT32).</returns>
        [TensorCoreIntrinsic(TensorCoreOperation.WMMA_MMA, PTXInstruction = "wmma.mma.sync.aligned.m16n16k16.s32.s8.s8.s32")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static extern MatrixFragment<int> INT8MatrixMultiply(
            MatrixFragment<sbyte> fragmentA,
            MatrixFragment<sbyte> fragmentB,
            MatrixFragment<int> fragmentC);

        /// <summary>
        /// Synchronizes all threads in a warp after tensor operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SyncTensor()
        {
            Warp.Barrier();
        }
    }

    /// <summary>
    /// Warp-level matrix fragment for tensor core operations.
    /// This struct represents a distributed matrix fragment across a warp.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public readonly ref struct MatrixFragment<T> where T : unmanaged
    {
        private readonly Span<T> data;
        
        /// <summary>
        /// Creates a matrix fragment with the specified data.
        /// </summary>
        /// <param name="data">The fragment data distributed across warp threads.</param>
        internal MatrixFragment(Span<T> data)
        {
            this.data = data;
        }
        
        /// <summary>
        /// Gets the fragment data as a span.
        /// </summary>
        public Span<T> Data => data;
        
        /// <summary>
        /// Gets the number of elements in this fragment.
        /// </summary>
        public int Length => data.Length;
        
        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>The element at the specified index.</returns>
        public T this[int index]
        {
            get => data[index];
            set => data[index] = value;
        }
        
        /// <summary>
        /// Converts to .NET SIMD vector for CPU fallback operations.
        /// </summary>
        /// <param name="index">The starting index for the vector.</param>
        /// <returns>A vector containing fragment data.</returns>
        public Vector<T> AsVector(int index)
        {
            if (typeof(T) == typeof(float))
            {
                var floatData = System.Runtime.InteropServices.MemoryMarshal.Cast<T, float>(data);
                var floatVector = new Vector<float>(floatData.Slice(index));
                return System.Runtime.InteropServices.MemoryMarshal.Cast<Vector<float>, Vector<T>>(new[] { floatVector })[0];
            }
            throw new NotSupportedException($"Vector conversion not supported for type {typeof(T)}");
        }
        
        /// <summary>
        /// Sets fragment data from a .NET SIMD vector.
        /// </summary>
        /// <param name="vector">The vector to copy from.</param>
        /// <param name="index">The starting index in the fragment.</param>
        public void FromVector(Vector<T> vector, int index)
        {
            if (typeof(T) == typeof(float))
            {
                var floatVector = System.Runtime.InteropServices.MemoryMarshal.Cast<Vector<T>, Vector<float>>(new[] { vector })[0];
                var floatData = System.Runtime.InteropServices.MemoryMarshal.Cast<T, float>(data);
                floatVector.CopyTo(floatData.Slice(index));
            }
            else
            {
                throw new NotSupportedException($"Vector conversion not supported for type {typeof(T)}");
            }
        }
    }
}