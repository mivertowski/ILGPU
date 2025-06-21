// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: TensorOperations.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.TensorCores;

namespace ILGPU.ML
{
    /// <summary>
    /// Represents the shape of a tensor.
    /// </summary>
    public readonly struct TensorShape : IEquatable<TensorShape>
    {
        private readonly int[] dimensions;

        /// <summary>
        /// Initializes a new tensor shape.
        /// </summary>
        public TensorShape(params int[] dimensions)
        {
            if (dimensions == null || dimensions.Length == 0)
                throw new ArgumentException("Tensor must have at least one dimension");

            this.dimensions = new int[dimensions.Length];
            Array.Copy(dimensions, this.dimensions, dimensions.Length);

            // Calculate total size
            Size = 1;
            for (int i = 0; i < dimensions.Length; i++)
            {
                if (dimensions[i] <= 0)
                    throw new ArgumentException($"Dimension {i} must be positive");
                Size *= dimensions[i];
            }
        }

        /// <summary>
        /// Gets the number of dimensions.
        /// </summary>
        public int Rank => dimensions?.Length ?? 0;

        /// <summary>
        /// Gets the total number of elements.
        /// </summary>
        public long Size { get; }

        /// <summary>
        /// Gets the dimension at the specified index.
        /// </summary>
        public int this[int index] => dimensions[index];

        /// <summary>
        /// Gets all dimensions as a span.
        /// </summary>
        public ReadOnlySpan<int> Dimensions => dimensions;

        /// <summary>
        /// Checks if this shape is compatible for matrix multiplication with another shape.
        /// </summary>
        public bool IsMatMulCompatible(TensorShape other)
        {
            if (Rank != 2 || other.Rank != 2)
                return false;
            return this[1] == other[0]; // Inner dimensions must match
        }

        /// <summary>
        /// Gets the resulting shape from matrix multiplication.
        /// </summary>
        public TensorShape MatMulResultShape(TensorShape other)
        {
            if (!IsMatMulCompatible(other))
                throw new ArgumentException("Shapes are not compatible for matrix multiplication");
            return new TensorShape(this[0], other[1]);
        }

        /// <inheritdoc/>
        public bool Equals(TensorShape other)
        {
            if (Rank != other.Rank) return false;
            for (int i = 0; i < Rank; i++)
                if (this[i] != other[i]) return false;
            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TensorShape other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            for (int i = 0; i < Rank; i++)
                hash.Add(this[i]);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public override string ToString() => $"[{string.Join(", ", dimensions)}]";

        public static bool operator ==(TensorShape left, TensorShape right) => left.Equals(right);
        public static bool operator !=(TensorShape left, TensorShape right) => !left.Equals(right);
    }

    /// <summary>
    /// Represents a GPU tensor for machine learning operations.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public class Tensor<T> : IDisposable where T : unmanaged, INumber<T>
    {
        private readonly MemoryBuffer1D<T, Stride1D.Dense> buffer;
        private bool disposed;

        /// <summary>
        /// Initializes a new tensor.
        /// </summary>
        public Tensor(Accelerator accelerator, TensorShape shape)
        {
            if (accelerator == null)
                throw new ArgumentNullException(nameof(accelerator));

            Shape = shape;
            Accelerator = accelerator;
            buffer = accelerator.Allocate1D<T>(shape.Size);
        }

        /// <summary>
        /// Initializes a tensor from existing data.
        /// </summary>
        public Tensor(Accelerator accelerator, TensorShape shape, ReadOnlySpan<T> data)
            : this(accelerator, shape)
        {
            if (data.Length != shape.Size)
                throw new ArgumentException("Data length doesn't match tensor shape");

            buffer.View.CopyFromCPU(data.ToArray());
        }

        /// <summary>
        /// Gets the tensor shape.
        /// </summary>
        public TensorShape Shape { get; }

        /// <summary>
        /// Gets the accelerator this tensor is allocated on.
        /// </summary>
        public Accelerator Accelerator { get; }

        /// <summary>
        /// Gets the underlying memory buffer.
        /// </summary>
        public ArrayView<T> View => buffer.View;

        /// <summary>
        /// Gets the device memory buffer.
        /// </summary>
        internal MemoryBuffer1D<T, Stride1D.Dense> Buffer => buffer;

        /// <summary>
        /// Copies data from CPU to this tensor.
        /// </summary>
        public void CopyFromCPU(ReadOnlySpan<T> data)
        {
            if (data.Length != Shape.Size)
                throw new ArgumentException("Data length doesn't match tensor size");
            buffer.View.CopyFromCPU(data.ToArray());
        }

        /// <summary>
        /// Copies data from this tensor to CPU.
        /// </summary>
        public void CopyToCPU(Span<T> data)
        {
            if (data.Length != Shape.Size)
                throw new ArgumentException("Data length doesn't match tensor size");
            var tempArray = new T[data.Length];
            buffer.View.CopyToCPU(tempArray);
            tempArray.AsSpan().CopyTo(data);
        }

        /// <summary>
        /// Gets the tensor data as a CPU array.
        /// </summary>
        public T[] ToArray()
        {
            var result = new T[Shape.Size];
            buffer.View.CopyToCPU(result);
            return result;
        }

        /// <summary>
        /// Reshapes the tensor to a new shape (must have same total size).
        /// </summary>
        public Tensor<T> Reshape(TensorShape newShape)
        {
            if (newShape.Size != Shape.Size)
                throw new ArgumentException("New shape must have the same total size");

            // Create a new tensor that shares the same buffer
            var reshaped = new Tensor<T>(Accelerator, newShape);
            View.CopyTo(reshaped.View);
            return reshaped;
        }

        /// <summary>
        /// Creates a transposed view of this tensor (for 2D tensors).
        /// </summary>
        public Tensor<T> Transpose()
        {
            if (Shape.Rank != 2)
                throw new InvalidOperationException("Transpose only supported for 2D tensors");

            var transposed = new Tensor<T>(Accelerator, new TensorShape(Shape[1], Shape[0]));
            
            // Launch transpose kernel
            var kernel = Accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<T>, ArrayView<T>, int, int>(TransposeKernel<T>);
            
            kernel(new Index2D(Shape[1], Shape[0]), 
                View, transposed.View, Shape[0], Shape[1]);
            
            return transposed;
        }

        /// <summary>
        /// Performs matrix multiplication with another tensor using tensor cores if available.
        /// </summary>
        public Tensor<T> MatMul(Tensor<T> other, bool useTensorCores = true)
        {
            if (!Shape.IsMatMulCompatible(other.Shape))
                throw new ArgumentException("Tensor shapes are not compatible for matrix multiplication");

            var resultShape = Shape.MatMulResultShape(other.Shape);
            var result = new Tensor<T>(Accelerator, resultShape);

            if (useTensorCores && Accelerator.SupportsTensorCores() && 
                typeof(T) == typeof(Half) || typeof(T) == typeof(float))
            {
                // Use tensor cores for supported types
                TensorCoreMatMul(this, other, result);
            }
            else
            {
                // Use regular GEMM
                RegularMatMul(this, other, result);
            }

            return result;
        }

        /// <summary>
        /// Element-wise addition.
        /// </summary>
        public Tensor<T> Add(Tensor<T> other)
        {
            if (Shape != other.Shape)
                throw new ArgumentException("Tensors must have the same shape for addition");

            var result = new Tensor<T>(Accelerator, Shape);
            
            var kernel = Accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<T>, ArrayView<T>, ArrayView<T>>(AddKernel<T>);
            
            kernel(View.IntExtent, View, other.View, result.View);
            
            return result;
        }

        /// <summary>
        /// Element-wise multiplication.
        /// </summary>
        public Tensor<T> Multiply(Tensor<T> other)
        {
            if (Shape != other.Shape)
                throw new ArgumentException("Tensors must have the same shape for multiplication");

            var result = new Tensor<T>(Accelerator, Shape);
            
            var kernel = Accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<T>, ArrayView<T>, ArrayView<T>>(MultiplyKernel<T>);
            
            kernel(View.IntExtent, View, other.View, result.View);
            
            return result;
        }

        /// <summary>
        /// Applies ReLU activation function.
        /// </summary>
        public Tensor<T> ReLU()
        {
            var result = new Tensor<T>(Accelerator, Shape);
            
            var kernel = Accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<T>, ArrayView<T>>(ReLUKernel<T>);
            
            kernel(View.IntExtent, View, result.View);
            
            return result;
        }

        /// <summary>
        /// Mathematical extension methods for tensor operations.
        /// </summary>
        private static class MathExtensions
        {
            public static TElement Exp<TElement>(TElement value) where TElement : unmanaged, INumber<TElement>
            {
                // This would be implemented with proper math intrinsics
                if (typeof(TElement) == typeof(float))
                {
                    var floatVal = Unsafe.As<TElement, float>(ref value);
                    var result = MathF.Exp(floatVal);
                    return Unsafe.As<float, TElement>(ref result);
                }
                if (typeof(TElement) == typeof(double))
                {
                    var doubleVal = Unsafe.As<TElement, double>(ref value);
                    var result = Math.Exp(doubleVal);
                    return Unsafe.As<double, TElement>(ref result);
                }
                return value; // Placeholder
            }
        }

        /// <summary>
        /// Computes softmax along the last dimension.
        /// </summary>
        public Tensor<T> Softmax()
        {
            var result = new Tensor<T>(Accelerator, Shape);
            
            if (Shape.Rank == 2)
            {
                // 2D softmax (batch processing)
                var kernel = Accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<T>, ArrayView<T>, int, int>(Softmax2DKernel<T>);
                
                kernel(Shape[0], View, result.View, Shape[0], Shape[1]);
            }
            else
            {
                throw new NotImplementedException("Softmax only implemented for 2D tensors");
            }
            
            return result;
        }

        #region Kernels

        private static void TransposeKernel<T>(
            Index2D index,
            ArrayView<T> input,
            ArrayView<T> output,
            int rows,
            int cols)
            where T : unmanaged
        {
            if (index.X < cols && index.Y < rows)
            {
                var inputIdx = index.Y * cols + index.X;
                var outputIdx = index.X * rows + index.Y;
                output[outputIdx] = input[inputIdx];
            }
        }

        private static void AddKernel<T>(
            Index1D index,
            ArrayView<T> left,
            ArrayView<T> right,
            ArrayView<T> result)
            where T : unmanaged, INumber<T>
        {
            if (index < result.Length)
                result[index] = left[index] + right[index];
        }

        private static void MultiplyKernel<T>(
            Index1D index,
            ArrayView<T> left,
            ArrayView<T> right,
            ArrayView<T> result)
            where T : unmanaged, INumber<T>
        {
            if (index < result.Length)
                result[index] = left[index] * right[index];
        }

        private static void ReLUKernel<T>(
            Index1D index,
            ArrayView<T> input,
            ArrayView<T> output)
            where T : unmanaged, INumber<T>
        {
            if (index < input.Length)
                output[index] = T.Max(T.Zero, input[index]);
        }

        private static void Softmax2DKernel<T>(
            Index1D batchIndex,
            ArrayView<T> input,
            ArrayView<T> output,
            int batchSize,
            int featureSize)
            where T : unmanaged, INumber<T>
        {
            if (batchIndex >= batchSize) return;

            var offset = batchIndex * featureSize;
            
            // Find max for numerical stability
            var maxVal = input[offset];
            for (int i = 1; i < featureSize; i++)
                maxVal = T.Max(maxVal, input[offset + i]);

            // Compute exp and sum
            var sum = T.Zero;
            for (int i = 0; i < featureSize; i++)
            {
                var exp = MathExtensions.Exp(input[offset + i] - maxVal);
                output[offset + i] = exp;
                sum += exp;
            }

            // Normalize
            for (int i = 0; i < featureSize; i++)
                output[offset + i] /= sum;
        }

        #endregion

        #region Matrix Multiplication Implementations

        private static void TensorCoreMatMul(Tensor<T> a, Tensor<T> b, Tensor<T> result)
        {
            // Tensor core operations require specialized implementations for each precision
            throw new NotImplementedException("Tensor core matrix multiplication requires specialized precision-specific implementation");
        }

        private static void RegularMatMul(Tensor<T> a, Tensor<T> b, Tensor<T> result)
        {
            // Regular matrix multiplication kernel
            var kernel = a.Accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView<T>, ArrayView<T>, ArrayView<T>, int, int, int>(MatMulKernel<T>);
            
            kernel(new Index2D(result.Shape[0], result.Shape[1]),
                a.View, b.View, result.View,
                a.Shape[0], a.Shape[1], b.Shape[1]);
        }

        private static void MatMulKernel<T>(
            Index2D index,
            ArrayView<T> a,
            ArrayView<T> b,
            ArrayView<T> result,
            int M, int K, int N)
            where T : unmanaged, INumber<T>
        {
            if (index.X >= M || index.Y >= N) return;

            var sum = T.Zero;
            for (int k = 0; k < K; k++)
            {
                var aVal = a[index.X * K + k];
                var bVal = b[k * N + index.Y];
                sum += aVal * bVal;
            }
            
            result[index.X * N + index.Y] = sum;
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!disposed)
            {
                buffer?.Dispose();
                disposed = true;
            }
        }

        #endregion

        #region Operators

        public static Tensor<T> operator +(Tensor<T> left, Tensor<T> right) => left.Add(right);
        public static Tensor<T> operator *(Tensor<T> left, Tensor<T> right) => left.Multiply(right);

        #endregion
    }

    /// <summary>
    /// Factory methods for creating tensors.
    /// </summary>
    public static class Tensor
    {
        /// <summary>
        /// Creates a tensor filled with zeros.
        /// </summary>
        public static Tensor<T> Zeros<T>(Accelerator accelerator, TensorShape shape)
            where T : unmanaged, INumber<T>
        {
            var tensor = new Tensor<T>(accelerator, shape);
            tensor.View.MemSet(accelerator.DefaultStream, 0);
            return tensor;
        }

        /// <summary>
        /// Creates a tensor filled with ones.
        /// </summary>
        public static Tensor<T> Ones<T>(Accelerator accelerator, TensorShape shape)
            where T : unmanaged, INumber<T>
        {
            var tensor = new Tensor<T>(accelerator, shape);
            
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<T>>(FillOnesKernel<T>);
            
            kernel(tensor.View.IntExtent, tensor.View);
            
            return tensor;
        }

        /// <summary>
        /// Creates a tensor from CPU data.
        /// </summary>
        public static Tensor<T> FromArray<T>(Accelerator accelerator, TensorShape shape, T[] data)
            where T : unmanaged, INumber<T>
        {
            return new Tensor<T>(accelerator, shape, data);
        }

        private static void FillOnesKernel<T>(Index1D index, ArrayView<T> output)
            where T : unmanaged, INumber<T>
        {
            if (index < output.Length)
                output[index] = T.One;
        }
    }
}