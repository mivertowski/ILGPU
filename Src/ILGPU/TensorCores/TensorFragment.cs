// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: TensorFragment.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ILGPU.TensorCores
{
    /// <summary>
    /// Represents the layout of a tensor fragment for tensor core operations.
    /// </summary>
    public enum TensorFragmentLayout
    {
        /// <summary>
        /// Row-major layout.
        /// </summary>
        RowMajor,

        /// <summary>
        /// Column-major layout.
        /// </summary>
        ColMajor
    }

    /// <summary>
    /// Represents the kind of tensor fragment.
    /// </summary>
    public enum TensorFragmentKind
    {
        /// <summary>
        /// Matrix A fragment (multiplicand).
        /// </summary>
        MatrixA,

        /// <summary>
        /// Matrix B fragment (multiplier).
        /// </summary>
        MatrixB,

        /// <summary>
        /// Matrix C fragment (accumulator).
        /// </summary>
        Accumulator
    }

    /// <summary>
    /// Represents the precision mode for tensor operations.
    /// </summary>
    public enum TensorPrecision
    {
        /// <summary>
        /// FP16 precision (half).
        /// </summary>
        FP16,

        /// <summary>
        /// BFloat16 precision.
        /// </summary>
        BF16,

        /// <summary>
        /// TensorFloat32 precision.
        /// </summary>
        TF32,

        /// <summary>
        /// INT8 precision.
        /// </summary>
        INT8,

        /// <summary>
        /// FP8 E4M3 precision.
        /// </summary>
        FP8_E4M3,

        /// <summary>
        /// FP8 E5M2 precision.
        /// </summary>
        FP8_E5M2
    }

    /// <summary>
    /// Base interface for all tensor fragments.
    /// </summary>
    public interface ITensorFragment
    {
        /// <summary>
        /// Gets the fragment kind.
        /// </summary>
        TensorFragmentKind Kind { get; }

        /// <summary>
        /// Gets the precision mode.
        /// </summary>
        TensorPrecision Precision { get; }

        /// <summary>
        /// Gets the number of rows in the fragment.
        /// </summary>
        int Rows { get; }

        /// <summary>
        /// Gets the number of columns in the fragment.
        /// </summary>
        int Columns { get; }

        /// <summary>
        /// Gets the total number of elements in the fragment.
        /// </summary>
        int NumElements { get; }
    }

    /// <summary>
    /// Represents a tensor fragment for matrix multiply-accumulate operations.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TensorFragment<T> : ITensorFragment
        where T : unmanaged
    {
        /// <summary>
        /// The internal storage for the fragment data.
        /// </summary>
        /// <remarks>
        /// This is sized to hold the maximum fragment size (16x16 for Ampere+).
        /// Actual size depends on architecture and operation.
        /// </remarks>
        private readonly FragmentStorage storage;

        /// <summary>
        /// Initializes a new tensor fragment.
        /// </summary>
        internal TensorFragment(
            TensorFragmentKind kind,
            TensorPrecision precision,
            int rows,
            int columns)
        {
            Kind = kind;
            Precision = precision;
            Rows = rows;
            Columns = columns;
            NumElements = rows * columns;
            storage = default;
        }

        /// <inheritdoc/>
        public TensorFragmentKind Kind { get; }

        /// <inheritdoc/>
        public TensorPrecision Precision { get; }

        /// <inheritdoc/>
        public int Rows { get; }

        /// <inheritdoc/>
        public int Columns { get; }

        /// <inheritdoc/>
        public int NumElements { get; }

        /// <summary>
        /// Storage structure for fragment data.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 512)]
        private struct FragmentStorage
        {
            // This will be filled by PTX intrinsics
            // Size is 512 bytes to accommodate largest fragments
        }
    }

    /// <summary>
    /// Factory methods for creating tensor fragments.
    /// </summary>
    public static class TensorFragment
    {
        /// <summary>
        /// Creates a matrix A fragment for tensor core operations.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="rows">Number of rows.</param>
        /// <param name="columns">Number of columns.</param>
        /// <param name="precision">The precision mode.</param>
        /// <returns>A new tensor fragment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TensorFragment<T> CreateMatrixA<T>(
            int rows,
            int columns,
            TensorPrecision precision)
            where T : unmanaged
        {
            ValidateFragmentDimensions(rows, columns, TensorFragmentKind.MatrixA);
            return new TensorFragment<T>(TensorFragmentKind.MatrixA, precision, rows, columns);
        }

        /// <summary>
        /// Creates a matrix B fragment for tensor core operations.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="rows">Number of rows.</param>
        /// <param name="columns">Number of columns.</param>
        /// <param name="precision">The precision mode.</param>
        /// <returns>A new tensor fragment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TensorFragment<T> CreateMatrixB<T>(
            int rows,
            int columns,
            TensorPrecision precision)
            where T : unmanaged
        {
            ValidateFragmentDimensions(rows, columns, TensorFragmentKind.MatrixB);
            return new TensorFragment<T>(TensorFragmentKind.MatrixB, precision, rows, columns);
        }

        /// <summary>
        /// Creates an accumulator fragment for tensor core operations.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="rows">Number of rows.</param>
        /// <param name="columns">Number of columns.</param>
        /// <param name="precision">The precision mode.</param>
        /// <returns>A new tensor fragment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TensorFragment<T> CreateAccumulator<T>(
            int rows,
            int columns,
            TensorPrecision precision)
            where T : unmanaged
        {
            ValidateFragmentDimensions(rows, columns, TensorFragmentKind.Accumulator);
            return new TensorFragment<T>(TensorFragmentKind.Accumulator, precision, rows, columns);
        }

        /// <summary>
        /// Validates fragment dimensions based on architecture requirements.
        /// </summary>
        private static void ValidateFragmentDimensions(int rows, int columns, TensorFragmentKind kind)
        {
            // Tensor core fragments must be specific sizes
            // For now, we support the common 16x16x16 configuration
            bool isValid = (rows, columns, kind) switch
            {
                (16, 16, TensorFragmentKind.MatrixA) => true,
                (16, 16, TensorFragmentKind.MatrixB) => true,
                (16, 16, TensorFragmentKind.Accumulator) => true,
                (8, 16, TensorFragmentKind.MatrixA) => true,
                (16, 8, TensorFragmentKind.MatrixB) => true,
                (8, 8, TensorFragmentKind.Accumulator) => true,
                _ => false
            };

            if (!isValid)
            {
                throw new ArgumentException(
                    $"Invalid fragment dimensions {rows}x{columns} for {kind}. " +
                    "Tensor cores require specific matrix dimensions.");
            }
        }
    }
}
