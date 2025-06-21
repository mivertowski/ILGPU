// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: VectorOperations.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using ILGPU.Runtime;

namespace ILGPU.SIMD
{
    /// <summary>
    /// Provides unified SIMD operations that work on both CPU and GPU.
    /// </summary>
    public static class VectorOperations
    {
        /// <summary>
        /// Configuration for SIMD operations.
        /// </summary>
        public struct SIMDConfig
        {
            /// <summary>
            /// The preferred vector width in bytes.
            /// </summary>
            public int PreferredVectorWidth { get; set; }

            /// <summary>
            /// Whether to allow GPU vectorization.
            /// </summary>
            public bool AllowGPUVectorization { get; set; }

            /// <summary>
            /// Whether to use CPU fallback when GPU is unavailable.
            /// </summary>
            public bool UseCPUFallback { get; set; }

            /// <summary>
            /// Gets the default configuration.
            /// </summary>
            public static SIMDConfig Default => new SIMDConfig
            {
                PreferredVectorWidth = Vector<float>.Count * sizeof(float),
                AllowGPUVectorization = true,
                UseCPUFallback = true
            };
        }

        /// <summary>
        /// Performs element-wise addition of two vectors.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="left">The first vector.</param>
        /// <param name="right">The second vector.</param>
        /// <param name="result">The result vector.</param>
        /// <param name="config">SIMD configuration.</param>
        public static void Add<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            Span<T> result,
            SIMDConfig? config = null)
            where T : unmanaged, INumber<T>
        {
            if (left.Length != right.Length || left.Length != result.Length)
                throw new ArgumentException("All vectors must have the same length");

            var cfg = config ?? SIMDConfig.Default;

            // Try GPU acceleration first if available
            if (cfg.AllowGPUVectorization && TryGPUVectorAdd(left, right, result))
                return;

            // Fall back to CPU SIMD
            if (cfg.UseCPUFallback)
                CPUVectorAdd(left, right, result);
            else
                throw new NotSupportedException("GPU vectorization failed and CPU fallback is disabled");
        }

        /// <summary>
        /// Performs element-wise multiplication of two vectors.
        /// </summary>
        public static void Multiply<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            Span<T> result,
            SIMDConfig? config = null)
            where T : unmanaged, INumber<T>
        {
            if (left.Length != right.Length || left.Length != result.Length)
                throw new ArgumentException("All vectors must have the same length");

            var cfg = config ?? SIMDConfig.Default;

            if (cfg.AllowGPUVectorization && TryGPUVectorMultiply(left, right, result))
                return;

            if (cfg.UseCPUFallback)
                CPUVectorMultiply(left, right, result);
            else
                throw new NotSupportedException("GPU vectorization failed and CPU fallback is disabled");
        }

        /// <summary>
        /// Computes the dot product of two vectors.
        /// </summary>
        public static T DotProduct<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            SIMDConfig? config = null)
            where T : unmanaged, INumber<T>
        {
            if (left.Length != right.Length)
                throw new ArgumentException("Vectors must have the same length");

            var cfg = config ?? SIMDConfig.Default;

            if (cfg.AllowGPUVectorization && TryGPUDotProduct(left, right, out T gpuResult))
                return gpuResult;

            if (cfg.UseCPUFallback)
                return CPUDotProduct(left, right);

            throw new NotSupportedException("GPU vectorization failed and CPU fallback is disabled");
        }

        /// <summary>
        /// Performs matrix-vector multiplication: y = A * x.
        /// </summary>
        public static void MatrixVectorMultiply<T>(
            ReadOnlySpan<T> matrix,
            int rows,
            int cols,
            ReadOnlySpan<T> vector,
            Span<T> result,
            SIMDConfig? config = null)
            where T : unmanaged, INumber<T>
        {
            if (matrix.Length != rows * cols)
                throw new ArgumentException("Matrix size doesn't match dimensions");
            if (vector.Length != cols)
                throw new ArgumentException("Vector length doesn't match matrix columns");
            if (result.Length != rows)
                throw new ArgumentException("Result length doesn't match matrix rows");

            var cfg = config ?? SIMDConfig.Default;

            if (cfg.AllowGPUVectorization && TryGPUMatrixVector(matrix, rows, cols, vector, result))
                return;

            if (cfg.UseCPUFallback)
                CPUMatrixVectorMultiply(matrix, rows, cols, vector, result);
            else
                throw new NotSupportedException("GPU vectorization failed and CPU fallback is disabled");
        }

        #region GPU Implementation Attempts

        private static bool TryGPUVectorAdd<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            Span<T> result)
            where T : unmanaged, INumber<T>
        {
            // Check if we have a CUDA accelerator available
            try
            {
                using var context = Context.Create(builder => builder.Default());
                var device = context.GetPreferredDevice(preferCPU: false);
                if (device == null) return false;
                
                using var accelerator = device.CreateAccelerator(context);

                // Launch GPU kernel for vector addition
                using var leftBuffer = accelerator.Allocate1D<T>(left.Length);
                using var rightBuffer = accelerator.Allocate1D<T>(right.Length);
                using var resultBuffer = accelerator.Allocate1D<T>(result.Length);

                leftBuffer.CopyFromCPU(left.ToArray());
                rightBuffer.CopyFromCPU(right.ToArray());

                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<T>, ArrayView<T>, ArrayView<T>>(
                    GPUVectorAddKernel);

                kernel(left.Length, leftBuffer.View, rightBuffer.View, resultBuffer.View);
                accelerator.Synchronize();

                var resultArray = resultBuffer.GetAsArray1D();
                resultArray.AsSpan().CopyTo(result);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGPUVectorMultiply<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            Span<T> result)
            where T : unmanaged, INumber<T>
        {
            try
            {
                using var context = Context.Create(builder => builder.Default());
                var device = context.GetPreferredDevice(preferCPU: false);
                if (device == null) return false;
                
                using var accelerator = device.CreateAccelerator(context);

                using var leftBuffer = accelerator.Allocate1D<T>(left.Length);
                using var rightBuffer = accelerator.Allocate1D<T>(right.Length);
                using var resultBuffer = accelerator.Allocate1D<T>(result.Length);

                leftBuffer.CopyFromCPU(left.ToArray());
                rightBuffer.CopyFromCPU(right.ToArray());

                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<T>, ArrayView<T>, ArrayView<T>>(
                    GPUVectorMultiplyKernel);

                kernel(left.Length, leftBuffer.View, rightBuffer.View, resultBuffer.View);
                accelerator.Synchronize();

                var resultArray = resultBuffer.GetAsArray1D();
                resultArray.AsSpan().CopyTo(result);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGPUDotProduct<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            out T result)
            where T : unmanaged, INumber<T>
        {
            result = default;
            try
            {
                using var context = Context.Create(builder => builder.Default());
                var device = context.GetPreferredDevice(preferCPU: false);
                if (device == null) return false;
                
                using var accelerator = device.CreateAccelerator(context);

                using var leftBuffer = accelerator.Allocate1D<T>(left.Length);
                using var rightBuffer = accelerator.Allocate1D<T>(right.Length);
                using var resultBuffer = accelerator.Allocate1D<T>(1);

                leftBuffer.CopyFromCPU(left.ToArray());
                rightBuffer.CopyFromCPU(right.ToArray());

                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<T>, ArrayView<T>, ArrayView<T>>(
                    GPUDotProductKernel);

                kernel(left.Length, leftBuffer.View, rightBuffer.View, resultBuffer.View);
                accelerator.Synchronize();

                var resultArray = resultBuffer.GetAsArray1D();
                result = resultArray[0];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGPUMatrixVector<T>(
            ReadOnlySpan<T> matrix,
            int rows,
            int cols,
            ReadOnlySpan<T> vector,
            Span<T> result)
            where T : unmanaged, INumber<T>
        {
            try
            {
                using var context = Context.Create(builder => builder.Default());
                var device = context.GetPreferredDevice(preferCPU: false);
                if (device == null) return false;
                
                using var accelerator = device.CreateAccelerator(context);

                using var matrixBuffer = accelerator.Allocate1D<T>(matrix.Length);
                using var vectorBuffer = accelerator.Allocate1D<T>(vector.Length);
                using var resultBuffer = accelerator.Allocate1D<T>(result.Length);

                matrixBuffer.CopyFromCPU(matrix.ToArray());
                vectorBuffer.CopyFromCPU(vector.ToArray());

                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<T>, ArrayView<T>, ArrayView<T>, int, int>(
                    GPUMatrixVectorKernel);

                kernel(rows, matrixBuffer.View, vectorBuffer.View, resultBuffer.View, rows, cols);
                accelerator.Synchronize();

                var resultArray = resultBuffer.GetAsArray1D();
                resultArray.AsSpan().CopyTo(result);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region GPU Kernels

        private static void GPUVectorAddKernel<T>(
            Index1D index,
            ArrayView<T> left,
            ArrayView<T> right,
            ArrayView<T> result)
            where T : unmanaged, INumber<T>
        {
            if (index < result.Length)
                result[index] = left[index] + right[index];
        }

        private static void GPUVectorMultiplyKernel<T>(
            Index1D index,
            ArrayView<T> left,
            ArrayView<T> right,
            ArrayView<T> result)
            where T : unmanaged, INumber<T>
        {
            if (index < result.Length)
                result[index] = left[index] * right[index];
        }

        private static void GPUDotProductKernel<T>(
            Index1D index,
            ArrayView<T> left,
            ArrayView<T> right,
            ArrayView<T> result)
            where T : unmanaged, INumber<T>
        {
            // Simple dot product - in practice would use reduction
            var sum = T.Zero;
            for (int i = 0; i < left.Length; i++)
                sum += left[i] * right[i];
            
            if (index == 0)
                result[0] = sum;
        }

        private static void GPUMatrixVectorKernel<T>(
            Index1D index,
            ArrayView<T> matrix,
            ArrayView<T> vector,
            ArrayView<T> result,
            int rows,
            int cols)
            where T : unmanaged, INumber<T>
        {
            if (index < rows)
            {
                var sum = T.Zero;
                for (int j = 0; j < cols; j++)
                    sum += matrix[index * cols + j] * vector[j];
                result[index] = sum;
            }
        }

        #endregion

        #region CPU SIMD Implementations

        private static void CPUVectorAdd<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            Span<T> result)
            where T : unmanaged, INumber<T>
        {
            if (typeof(T) == typeof(float))
            {
                CPUVectorAddFloat(
                    MemoryMarshal.Cast<T, float>(left),
                    MemoryMarshal.Cast<T, float>(right),
                    MemoryMarshal.Cast<T, float>(result));
                return;
            }

            // Generic fallback
            for (int i = 0; i < left.Length; i++)
                result[i] = left[i] + right[i];
        }

        private static void CPUVectorAddFloat(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            // Platform-specific optimized implementations
            if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
            {
                VectorAddAvx(left, right, result);
            }
            else if (Vector128.IsHardwareAccelerated && Sse.IsSupported)
            {
                VectorAddSse(left, right, result);
            }
            else if (AdvSimd.IsSupported)
            {
                VectorAddNeon(left, right, result);
            }
            else
            {
                // Fallback to System.Numerics.Vector
                VectorAddGeneric(left, right, result);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void VectorAddAvx(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            const int vectorSize = 8; // AVX 256-bit = 8 floats
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = Avx.LoadVector256(leftPtr + i);
                    var rightVec = Avx.LoadVector256(rightPtr + i);
                    var resultVec = Avx.Add(leftVec, rightVec);
                    Avx.Store(resultPtr + i, resultVec);
                }
            }

            // Handle remainder
            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] + right[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void VectorAddSse(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            const int vectorSize = 4; // SSE 128-bit = 4 floats
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = Sse.LoadVector128(leftPtr + i);
                    var rightVec = Sse.LoadVector128(rightPtr + i);
                    var resultVec = Sse.Add(leftVec, rightVec);
                    Sse.Store(resultPtr + i, resultVec);
                }
            }

            // Handle remainder
            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] + right[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void VectorAddNeon(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            const int vectorSize = 4; // NEON 128-bit = 4 floats
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = AdvSimd.LoadVector128(leftPtr + i);
                    var rightVec = AdvSimd.LoadVector128(rightPtr + i);
                    var resultVec = AdvSimd.Add(leftVec, rightVec);
                    AdvSimd.Store(resultPtr + i, resultVec);
                }
            }

            // Handle remainder
            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] + right[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void VectorAddGeneric(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            int vectorSize = Vector<float>.Count;
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            // Vectorized part using System.Numerics.Vector
            for (int i = 0; i < vectorizedLength; i += vectorSize)
            {
                var leftVec = new Vector<float>(left.Slice(i));
                var rightVec = new Vector<float>(right.Slice(i));
                var resultVec = leftVec + rightVec;
                resultVec.CopyTo(result.Slice(i));
            }

            // Remainder
            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] + right[i];
        }

        private static void CPUVectorMultiply<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right,
            Span<T> result)
            where T : unmanaged, INumber<T>
        {
            if (typeof(T) == typeof(float))
            {
                CPUVectorMultiplyFloat(
                    MemoryMarshal.Cast<T, float>(left),
                    MemoryMarshal.Cast<T, float>(right),
                    MemoryMarshal.Cast<T, float>(result));
                return;
            }

            // Generic fallback
            for (int i = 0; i < left.Length; i++)
                result[i] = left[i] * right[i];
        }

        private static void CPUVectorMultiplyFloat(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            // Platform-specific optimized implementations
            if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
            {
                VectorMultiplyAvx(left, right, result);
            }
            else if (Vector128.IsHardwareAccelerated && Sse.IsSupported)
            {
                VectorMultiplySse(left, right, result);
            }
            else if (AdvSimd.IsSupported)
            {
                VectorMultiplyNeon(left, right, result);
            }
            else
            {
                VectorMultiplyGeneric(left, right, result);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void VectorMultiplyAvx(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            const int vectorSize = 8;
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = Avx.LoadVector256(leftPtr + i);
                    var rightVec = Avx.LoadVector256(rightPtr + i);
                    var resultVec = Avx.Multiply(leftVec, rightVec);
                    Avx.Store(resultPtr + i, resultVec);
                }
            }

            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] * right[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void VectorMultiplySse(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            const int vectorSize = 4;
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = Sse.LoadVector128(leftPtr + i);
                    var rightVec = Sse.LoadVector128(rightPtr + i);
                    var resultVec = Sse.Multiply(leftVec, rightVec);
                    Sse.Store(resultPtr + i, resultVec);
                }
            }

            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] * right[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void VectorMultiplyNeon(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            const int vectorSize = 4;
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = AdvSimd.LoadVector128(leftPtr + i);
                    var rightVec = AdvSimd.LoadVector128(rightPtr + i);
                    var resultVec = AdvSimd.Multiply(leftVec, rightVec);
                    AdvSimd.Store(resultPtr + i, resultVec);
                }
            }

            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] * right[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void VectorMultiplyGeneric(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right,
            Span<float> result)
        {
            int vectorSize = Vector<float>.Count;
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            for (int i = 0; i < vectorizedLength; i += vectorSize)
            {
                var leftVec = new Vector<float>(left.Slice(i));
                var rightVec = new Vector<float>(right.Slice(i));
                var resultVec = leftVec * rightVec;
                resultVec.CopyTo(result.Slice(i));
            }

            for (int i = vectorizedLength; i < left.Length; i++)
                result[i] = left[i] * right[i];
        }

        private static T CPUDotProduct<T>(
            ReadOnlySpan<T> left,
            ReadOnlySpan<T> right)
            where T : unmanaged, INumber<T>
        {
            if (typeof(T) == typeof(float))
            {
                var result = CPUDotProductFloat(
                    MemoryMarshal.Cast<T, float>(left),
                    MemoryMarshal.Cast<T, float>(right));
                return Unsafe.As<float, T>(ref result);
            }

            // Generic fallback
            var sum = T.Zero;
            for (int i = 0; i < left.Length; i++)
                sum += left[i] * right[i];
            return sum;
        }

        private static float CPUDotProductFloat(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right)
        {
            // Platform-specific optimized implementations
            if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
            {
                return DotProductAvx(left, right);
            }
            else if (Vector128.IsHardwareAccelerated && Sse.IsSupported)
            {
                return DotProductSse(left, right);
            }
            else if (AdvSimd.IsSupported)
            {
                return DotProductNeon(left, right);
            }
            else
            {
                return DotProductGeneric(left, right);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float DotProductAvx(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right)
        {
            const int vectorSize = 8;
            int vectorizedLength = left.Length - (left.Length % vectorSize);
            
            var sumVec = Vector256<float>.Zero;

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = Avx.LoadVector256(leftPtr + i);
                    var rightVec = Avx.LoadVector256(rightPtr + i);
                    var product = Avx.Multiply(leftVec, rightVec);
                    sumVec = Avx.Add(sumVec, product);
                }
            }

            // Horizontal sum of the vector
            var sumHigh = Avx.ExtractVector128(sumVec, 1);
            var sumLow = Avx.ExtractVector128(sumVec, 0);
            var sum128 = Sse.Add(sumHigh, sumLow);
            
            // Final horizontal sum
            var shuf = Sse.Shuffle(sum128, sum128, 0b_11_10_01_00);
            sum128 = Sse.Add(sum128, shuf);
            shuf = Sse.Shuffle(sum128, sum128, 0b_10_11_00_01);
            sum128 = Sse.Add(sum128, shuf);
            
            float sum = sum128.GetElement(0);

            // Handle remainder
            for (int i = vectorizedLength; i < left.Length; i++)
                sum += left[i] * right[i];

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float DotProductSse(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right)
        {
            const int vectorSize = 4;
            int vectorizedLength = left.Length - (left.Length % vectorSize);
            
            var sumVec = Vector128<float>.Zero;

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = Sse.LoadVector128(leftPtr + i);
                    var rightVec = Sse.LoadVector128(rightPtr + i);
                    var product = Sse.Multiply(leftVec, rightVec);
                    sumVec = Sse.Add(sumVec, product);
                }
            }

            // Horizontal sum
            var shuf = Sse.Shuffle(sumVec, sumVec, 0b_11_10_01_00);
            sumVec = Sse.Add(sumVec, shuf);
            shuf = Sse.Shuffle(sumVec, sumVec, 0b_10_11_00_01);
            sumVec = Sse.Add(sumVec, shuf);
            
            float sum = sumVec.GetElement(0);

            for (int i = vectorizedLength; i < left.Length; i++)
                sum += left[i] * right[i];

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float DotProductNeon(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right)
        {
            const int vectorSize = 4;
            int vectorizedLength = left.Length - (left.Length % vectorSize);
            
            var sumVec = Vector128<float>.Zero;

            fixed (float* leftPtr = left)
            fixed (float* rightPtr = right)
            {
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    var leftVec = AdvSimd.LoadVector128(leftPtr + i);
                    var rightVec = AdvSimd.LoadVector128(rightPtr + i);
                    var product = AdvSimd.Multiply(leftVec, rightVec);
                    sumVec = AdvSimd.Add(sumVec, product);
                }
            }

            // Horizontal sum using NEON
            var lower = sumVec.GetLower();
            var upper = sumVec.GetUpper();
            var combined = AdvSimd.Add(lower, upper);
            
            float sum = combined.GetElement(0) + combined.GetElement(1);

            for (int i = vectorizedLength; i < left.Length; i++)
                sum += left[i] * right[i];

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DotProductGeneric(
            ReadOnlySpan<float> left,
            ReadOnlySpan<float> right)
        {
            int vectorSize = Vector<float>.Count;
            int vectorizedLength = left.Length - (left.Length % vectorSize);

            var sumVector = Vector<float>.Zero;

            for (int i = 0; i < vectorizedLength; i += vectorSize)
            {
                var leftVec = new Vector<float>(left.Slice(i));
                var rightVec = new Vector<float>(right.Slice(i));
                sumVector += leftVec * rightVec;
            }

            float sum = Vector.Dot(sumVector, Vector<float>.One);

            for (int i = vectorizedLength; i < left.Length; i++)
                sum += left[i] * right[i];

            return sum;
        }

        private static void CPUMatrixVectorMultiply<T>(
            ReadOnlySpan<T> matrix,
            int rows,
            int cols,
            ReadOnlySpan<T> vector,
            Span<T> result)
            where T : unmanaged, INumber<T>
        {
            if (typeof(T) == typeof(float))
            {
                MatrixVectorMultiplyFloat(
                    MemoryMarshal.Cast<T, float>(matrix),
                    rows,
                    cols,
                    MemoryMarshal.Cast<T, float>(vector),
                    MemoryMarshal.Cast<T, float>(result));
                return;
            }

            // Generic fallback
            for (int i = 0; i < rows; i++)
            {
                var sum = T.Zero;
                var row = matrix.Slice(i * cols, cols);
                
                for (int j = 0; j < cols; j++)
                    sum += row[j] * vector[j];
                
                result[i] = sum;
            }
        }

        private static void MatrixVectorMultiplyFloat(
            ReadOnlySpan<float> matrix,
            int rows,
            int cols,
            ReadOnlySpan<float> vector,
            Span<float> result)
        {
            // Platform-specific optimized implementations
            if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
            {
                MatrixVectorMultiplyAvx(matrix, rows, cols, vector, result);
            }
            else if (Vector128.IsHardwareAccelerated && Sse.IsSupported)
            {
                MatrixVectorMultiplySse(matrix, rows, cols, vector, result);
            }
            else if (AdvSimd.IsSupported)
            {
                MatrixVectorMultiplyNeon(matrix, rows, cols, vector, result);
            }
            else
            {
                MatrixVectorMultiplyGeneric(matrix, rows, cols, vector, result);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void MatrixVectorMultiplyAvx(
            ReadOnlySpan<float> matrix,
            int rows,
            int cols,
            ReadOnlySpan<float> vector,
            Span<float> result)
        {
            const int vectorSize = 8;
            int vectorizedCols = cols - (cols % vectorSize);

            fixed (float* matrixPtr = matrix)
            fixed (float* vectorPtr = vector)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < rows; i++)
                {
                    var sumVec = Vector256<float>.Zero;
                    float* rowPtr = matrixPtr + i * cols;

                    // Vectorized dot product for this row
                    for (int j = 0; j < vectorizedCols; j += vectorSize)
                    {
                        var matrixVec = Avx.LoadVector256(rowPtr + j);
                        var vectorVec = Avx.LoadVector256(vectorPtr + j);
                        var product = Avx.Multiply(matrixVec, vectorVec);
                        sumVec = Avx.Add(sumVec, product);
                    }

                    // Horizontal sum
                    var sumHigh = Avx.ExtractVector128(sumVec, 1);
                    var sumLow = Avx.ExtractVector128(sumVec, 0);
                    var sum128 = Sse.Add(sumHigh, sumLow);
                    
                    var shuf = Sse.Shuffle(sum128, sum128, 0b_11_10_01_00);
                    sum128 = Sse.Add(sum128, shuf);
                    shuf = Sse.Shuffle(sum128, sum128, 0b_10_11_00_01);
                    sum128 = Sse.Add(sum128, shuf);
                    
                    float sum = sum128.ToScalar();

                    // Handle remainder
                    for (int j = vectorizedCols; j < cols; j++)
                        sum += rowPtr[j] * vectorPtr[j];

                    resultPtr[i] = sum;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void MatrixVectorMultiplySse(
            ReadOnlySpan<float> matrix,
            int rows,
            int cols,
            ReadOnlySpan<float> vector,
            Span<float> result)
        {
            const int vectorSize = 4;
            int vectorizedCols = cols - (cols % vectorSize);

            fixed (float* matrixPtr = matrix)
            fixed (float* vectorPtr = vector)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < rows; i++)
                {
                    var sumVec = Vector128<float>.Zero;
                    float* rowPtr = matrixPtr + i * cols;

                    for (int j = 0; j < vectorizedCols; j += vectorSize)
                    {
                        var matrixVec = Sse.LoadVector128(rowPtr + j);
                        var vectorVec = Sse.LoadVector128(vectorPtr + j);
                        var product = Sse.Multiply(matrixVec, vectorVec);
                        sumVec = Sse.Add(sumVec, product);
                    }

                    // Horizontal sum
                    var shuf = Sse.Shuffle(sumVec, sumVec, 0b_11_10_01_00);
                    sumVec = Sse.Add(sumVec, shuf);
                    shuf = Sse.Shuffle(sumVec, sumVec, 0b_10_11_00_01);
                    sumVec = Sse.Add(sumVec, shuf);
                    
                    float sum = sumVec.ToScalar();

                    for (int j = vectorizedCols; j < cols; j++)
                        sum += rowPtr[j] * vectorPtr[j];

                    resultPtr[i] = sum;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void MatrixVectorMultiplyNeon(
            ReadOnlySpan<float> matrix,
            int rows,
            int cols,
            ReadOnlySpan<float> vector,
            Span<float> result)
        {
            const int vectorSize = 4;
            int vectorizedCols = cols - (cols % vectorSize);

            fixed (float* matrixPtr = matrix)
            fixed (float* vectorPtr = vector)
            fixed (float* resultPtr = result)
            {
                for (int i = 0; i < rows; i++)
                {
                    var sumVec = Vector128<float>.Zero;
                    float* rowPtr = matrixPtr + i * cols;

                    for (int j = 0; j < vectorizedCols; j += vectorSize)
                    {
                        var matrixVec = AdvSimd.LoadVector128(rowPtr + j);
                        var vectorVec = AdvSimd.LoadVector128(vectorPtr + j);
                        var product = AdvSimd.Multiply(matrixVec, vectorVec);
                        sumVec = AdvSimd.Add(sumVec, product);
                    }

                    // Horizontal sum using NEON
                    var lower = sumVec.GetLower();
                    var upper = sumVec.GetUpper();
                    var combined = AdvSimd.Add(lower, upper);
                    float sum = combined.GetElement(0) + combined.GetElement(1);

                    for (int j = vectorizedCols; j < cols; j++)
                        sum += rowPtr[j] * vectorPtr[j];

                    resultPtr[i] = sum;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MatrixVectorMultiplyGeneric(
            ReadOnlySpan<float> matrix,
            int rows,
            int cols,
            ReadOnlySpan<float> vector,
            Span<float> result)
        {
            int vectorSize = Vector<float>.Count;
            int vectorizedCols = cols - (cols % vectorSize);

            for (int i = 0; i < rows; i++)
            {
                var sumVec = Vector<float>.Zero;
                var row = matrix.Slice(i * cols, cols);

                for (int j = 0; j < vectorizedCols; j += vectorSize)
                {
                    var matrixVec = new Vector<float>(row.Slice(j));
                    var vectorVec = new Vector<float>(vector.Slice(j));
                    sumVec += matrixVec * vectorVec;
                }

                float sum = Vector.Dot(sumVec, Vector<float>.One);

                for (int j = vectorizedCols; j < cols; j++)
                    sum += row[j] * vector[j];

                result[i] = sum;
            }
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for seamless CPU/GPU vector operations.
    /// </summary>
    public static class VectorExtensions
    {
        /// <summary>
        /// Converts a .NET Vector to GPU-compatible array.
        /// </summary>
        public static T[] ToGPUArray<T>(this Vector<T> vector)
            where T : unmanaged, INumber<T>
        {
            var result = new T[Vector<T>.Count];
            vector.CopyTo(result);
            return result;
        }

        /// <summary>
        /// Creates a .NET Vector from GPU array.
        /// </summary>
        public static Vector<T> ToVector<T>(this ReadOnlySpan<T> span)
            where T : unmanaged, INumber<T>
        {
            if (span.Length < Vector<T>.Count)
                throw new ArgumentException("Span too small for vector");
            
            return new Vector<T>(span);
        }

        /// <summary>
        /// Checks if the current context supports hybrid CPU/GPU vectorization.
        /// </summary>
        public static bool SupportsHybridVectorization(this Accelerator accelerator)
        {
            return accelerator.AcceleratorType == AcceleratorType.Cuda ||
                   accelerator.AcceleratorType == AcceleratorType.OpenCL;
        }
    }
}