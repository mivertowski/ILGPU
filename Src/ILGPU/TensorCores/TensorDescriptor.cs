// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: TensorDescriptor.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU.TensorCores
{
    /// <summary>
    /// Interface for compile-time constants used in tensor descriptors.
    /// </summary>
    /// <typeparam name="T">The constant type.</typeparam>
    public interface IConstant<T> where T : struct
    {
        /// <summary>
        /// Gets the compile-time constant value.
        /// </summary>
        static abstract T Value { get; }
    }

    /// <summary>
    /// Compile-time integer constant for tensor dimensions.
    /// </summary>
    /// <typeparam name="TValue">The integer value.</typeparam>
    public readonly struct IntConstant<TValue> : IConstant<int> where TValue : IConstant<int>
    {
        /// <inheritdoc/>
        public static int Value => TValue.Value;
    }

    /// <summary>
    /// Common tensor dimension constants.
    /// </summary>
    public static class TensorDimensions
    {
        public readonly struct D8 : IConstant<int> { public static int Value => 8; }
        public readonly struct D16 : IConstant<int> { public static int Value => 16; }
        public readonly struct D32 : IConstant<int> { public static int Value => 32; }
        public readonly struct D64 : IConstant<int> { public static int Value => 64; }
    }

    /// <summary>
    /// Tensor fragment layout types.
    /// </summary>
    public enum TensorLayout
    {
        RowMajor,
        ColMajor
    }

    /// <summary>
    /// Compile-time tensor descriptor with type-level dimension safety.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TLayout">The matrix layout.</typeparam>
    /// <typeparam name="TM">The M dimension constant.</typeparam>
    /// <typeparam name="TN">The N dimension constant.</typeparam>
    /// <typeparam name="TK">The K dimension constant.</typeparam>
    public readonly struct TensorDescriptor<T, TLayout, TM, TN, TK> : ITensorDescriptor
        where T : unmanaged
        where TLayout : struct, Enum
        where TM : IConstant<int>
        where TN : IConstant<int>
        where TK : IConstant<int>
    {
        /// <summary>
        /// Gets the M dimension (number of rows).
        /// </summary>
        public static int M => TM.Value;

        /// <summary>
        /// Gets the N dimension (number of columns).
        /// </summary>
        public static int N => TN.Value;

        /// <summary>
        /// Gets the K dimension (inner dimension for matrix multiplication).
        /// </summary>
        public static int K => TK.Value;

        /// <summary>
        /// Gets the matrix layout.
        /// </summary>
        public static TensorLayout Layout
        {
            get
            {
                var defaultLayout = default(TLayout);
                return Unsafe.As<TLayout, TensorLayout>(ref defaultLayout);
            }
        }

        /// <summary>
        /// Gets the element type.
        /// </summary>
        public static Type ElementType => typeof(T);

        /// <summary>
        /// Gets the total number of elements in a matrix fragment.
        /// </summary>
        public static int NumElements => GetFragmentElements();

        /// <summary>
        /// Validates that the tensor dimensions are supported by tensor cores.
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                // Tensor cores support specific dimension combinations
                return (M, N, K) switch
                {
                    (16, 16, 16) => true,  // Most common
                    (32, 8, 16) => true,   // Tall matrices
                    (8, 32, 16) => true,   // Wide matrices
                    (16, 16, 8) when typeof(T) == typeof(float) => true,  // TF32
                    _ => false
                };
            }
        }

        /// <summary>
        /// Gets the precision mode based on the element type.
        /// </summary>
        public static TensorPrecision Precision
        {
            get
            {
                return typeof(T) switch
                {
                    Type t when t == typeof(Half) => TensorPrecision.FP16,
                    Type t when t == typeof(BFloat16) => TensorPrecision.BF16,
                    Type t when t == typeof(float) => TensorPrecision.TF32,
                    _ => throw new NotSupportedException($"Unsupported tensor type: {typeof(T)}")
                };
            }
        }

        /// <summary>
        /// Creates a matrix A fragment descriptor.
        /// </summary>
        /// <returns>A matrix fragment descriptor for matrix A.</returns>
        public static MatrixFragmentDescriptor<T> CreateMatrixA()
        {
            ValidateSupported();
            return new MatrixFragmentDescriptor<T>(TensorFragmentKind.MatrixA, M, K, Layout);
        }

        /// <summary>
        /// Creates a matrix B fragment descriptor.
        /// </summary>
        /// <returns>A matrix fragment descriptor for matrix B.</returns>
        public static MatrixFragmentDescriptor<T> CreateMatrixB()
        {
            ValidateSupported();
            return new MatrixFragmentDescriptor<T>(TensorFragmentKind.MatrixB, K, N, Layout);
        }

        /// <summary>
        /// Creates an accumulator matrix C/D fragment descriptor.
        /// </summary>
        /// <returns>A matrix fragment descriptor for accumulator matrix.</returns>
        public static MatrixFragmentDescriptor<T> CreateAccumulator()
        {
            ValidateSupported();
            return new MatrixFragmentDescriptor<T>(TensorFragmentKind.Accumulator, M, N, Layout);
        }

        /// <summary>
        /// Performs compile-time validation of tensor core compatibility.
        /// </summary>
        /// <exception cref="NotSupportedException">Thrown when the configuration is not supported.</exception>
        private static void ValidateSupported()
        {
            if (!IsSupported)
            {
                throw new NotSupportedException(
                    $"Tensor configuration {typeof(T).Name} {M}x{N}x{K} is not supported by tensor cores. " +
                    $"Supported configurations: 16x16x16 (FP16/BF16), 16x16x8 (TF32), 32x8x16, 8x32x16");
            }
        }

        /// <summary>
        /// Gets the number of elements in a matrix fragment based on dimensions and type.
        /// </summary>
        /// <returns>The number of elements per thread in the fragment.</returns>
        private static int GetFragmentElements()
        {
            // Fragment elements are distributed across a 32-thread warp
            // The exact distribution depends on the tensor core architecture
            return (M, N, K) switch
            {
                (16, 16, 16) when typeof(T) == typeof(Half) => 8,      // 16x16 FP16 matrix = 8 elements per thread
                (16, 16, 16) when typeof(T) == typeof(BFloat16) => 8,  // 16x16 BF16 matrix = 8 elements per thread
                (16, 16, 8) when typeof(T) == typeof(float) => 8,      // 16x16 TF32 accumulator = 8 elements per thread
                (32, 8, 16) => 8,   // Alternative configurations
                (8, 32, 16) => 8,
                _ => throw new NotSupportedException($"Unsupported tensor configuration: {M}x{N}x{K}")
            };
        }
    }

    /// <summary>
    /// Base interface for tensor descriptors.
    /// </summary>
    public interface ITensorDescriptor
    {
        // Marker interface for type safety
    }

    /// <summary>
    /// Descriptor for a specific matrix fragment.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public readonly struct MatrixFragmentDescriptor<T> where T : unmanaged
    {
        /// <summary>
        /// Gets the fragment kind (A, B, or Accumulator).
        /// </summary>
        public TensorFragmentKind Kind { get; }

        /// <summary>
        /// Gets the number of rows in the fragment.
        /// </summary>
        public int Rows { get; }

        /// <summary>
        /// Gets the number of columns in the fragment.
        /// </summary>
        public int Columns { get; }

        /// <summary>
        /// Gets the matrix layout.
        /// </summary>
        public TensorLayout Layout { get; }

        /// <summary>
        /// Gets the number of elements stored per thread.
        /// </summary>
        public int ElementsPerThread { get; }

        /// <summary>
        /// Initializes a matrix fragment descriptor.
        /// </summary>
        /// <param name="kind">The fragment kind.</param>
        /// <param name="rows">The number of rows.</param>
        /// <param name="columns">The number of columns.</param>
        /// <param name="layout">The matrix layout.</param>
        internal MatrixFragmentDescriptor(TensorFragmentKind kind, int rows, int columns, TensorLayout layout)
        {
            Kind = kind;
            Rows = rows;
            Columns = columns;
            Layout = layout;
            ElementsPerThread = CalculateElementsPerThread(kind, rows, columns);
        }

        /// <summary>
        /// Calculates the number of elements stored per thread for this fragment.
        /// </summary>
        /// <param name="kind">The fragment kind.</param>
        /// <param name="rows">The number of rows.</param>
        /// <param name="columns">The number of columns.</param>
        /// <returns>The number of elements per thread.</returns>
        private static int CalculateElementsPerThread(TensorFragmentKind kind, int rows, int columns)
        {
            // Standard WMMA fragments distribute elements across 32 threads
            int totalElements = rows * columns;
            const int warpSize = 32;

            return kind switch
            {
                TensorFragmentKind.MatrixA when (rows, columns) == (16, 16) => 8,
                TensorFragmentKind.MatrixB when (rows, columns) == (16, 16) => 8,
                TensorFragmentKind.Accumulator when (rows, columns) == (16, 16) => 8,
                _ => Math.Max(1, totalElements / warpSize)
            };
        }

        /// <summary>
        /// Creates a matrix fragment with this descriptor.
        /// </summary>
        /// <returns>A new matrix fragment.</returns>
        public MatrixFragment<T> CreateFragment()
        {
            var data = new T[ElementsPerThread];
            return new MatrixFragment<T>(data);
        }

        /// <summary>
        /// Validates compatibility with another fragment for matrix operations.
        /// </summary>
        /// <param name="other">The other fragment descriptor.</param>
        /// <returns>True if compatible for matrix multiplication.</returns>
        public bool IsCompatibleWith<TOther>(MatrixFragmentDescriptor<TOther> other) 
            where TOther : unmanaged
        {
            return (Kind, other.Kind) switch
            {
                (TensorFragmentKind.MatrixA, TensorFragmentKind.MatrixB) => Columns == other.Rows,
                (TensorFragmentKind.MatrixA, TensorFragmentKind.Accumulator) => Rows == other.Rows,
                (TensorFragmentKind.MatrixB, TensorFragmentKind.Accumulator) => Columns == other.Columns,
                _ => false
            };
        }
    }

    /// <summary>
    /// Helper class for creating tensor descriptors with common configurations.
    /// </summary>
    public static class TensorDescriptors
    {
        /// <summary>
        /// Standard 16x16x16 FP16 tensor core configuration.
        /// </summary>
        public static class FP16_16x16x16
        {
            public static TensorDescriptor<Half, TensorLayout, TensorDimensions.D16, TensorDimensions.D16, TensorDimensions.D16> RowMajor =>
                default;
        }

        /// <summary>
        /// Standard 16x16x16 BF16 tensor core configuration.
        /// </summary>
        public static class BF16_16x16x16
        {
            public static TensorDescriptor<BFloat16, TensorLayout, TensorDimensions.D16, TensorDimensions.D16, TensorDimensions.D16> RowMajor =>
                default;
        }

        /// <summary>
        /// TensorFloat32 16x16x8 configuration for Ampere GPUs.
        /// </summary>
        public static class TF32_16x16x8
        {
            public static TensorDescriptor<float, TensorLayout, TensorDimensions.D16, TensorDimensions.D16, TensorDimensions.D8> RowMajor =>
                default;
        }
    }
}
