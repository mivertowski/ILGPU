// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: ILGPUQueryable.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace ILGPU.Runtime.LINQ
{
    /// <summary>
    /// Provides LINQ-style operations for GPU arrays with lazy evaluation.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public interface IGPUQueryable<T> : IQueryable<T>, IDisposable
        where T : unmanaged
    {
        /// <summary>
        /// Gets the accelerator associated with this queryable.
        /// </summary>
        Accelerator Accelerator { get; }

        /// <summary>
        /// Gets the underlying memory buffer.
        /// </summary>
        MemoryBuffer1D<T, Stride1D.Dense> Buffer { get; }

        /// <summary>
        /// Executes the query and returns the results as an enumerable.
        /// </summary>
        /// <returns>The query results.</returns>
        IEnumerable<T> Execute();

        /// <summary>
        /// Executes the query and returns the results as an array.
        /// </summary>
        /// <returns>The query results as an array.</returns>
        T[] ToArray();

        /// <summary>
        /// Executes the query and stores the results in the specified buffer.
        /// </summary>
        /// <param name="outputBuffer">The output buffer.</param>
        void ExecuteTo(MemoryBuffer1D<T, Stride1D.Dense> outputBuffer);
    }

    /// <summary>
    /// Represents a GPU queryable with lazy evaluation of operations.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class GPUQueryable<T> : IGPUQueryable<T>
        where T : unmanaged
    {
        #region Instance

        private readonly Accelerator accelerator;
        private readonly MemoryBuffer1D<T, Stride1D.Dense> buffer;
        private readonly Expression expression;
        private readonly IQueryProvider provider;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="GPUQueryable{T}"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="buffer">The memory buffer.</param>
        internal GPUQueryable(
            Accelerator accelerator,
            MemoryBuffer1D<T, Stride1D.Dense> buffer)
        {
            this.accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            provider = new GPUQueryProvider(accelerator);
            expression = Expression.Constant(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GPUQueryable{T}"/> class.
        /// </summary>
        /// <param name="provider">The query provider.</param>
        /// <param name="expression">The expression tree.</param>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="buffer">The memory buffer.</param>
        internal GPUQueryable(
            IQueryProvider provider,
            Expression expression,
            Accelerator accelerator,
            MemoryBuffer1D<T, Stride1D.Dense> buffer)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.expression = expression ?? throw new ArgumentNullException(nameof(expression));
            this.accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the accelerator associated with this queryable.
        /// </summary>
        public Accelerator Accelerator => accelerator;

        /// <summary>
        /// Gets the underlying memory buffer.
        /// </summary>
        public MemoryBuffer1D<T, Stride1D.Dense> Buffer => buffer;

        /// <summary>
        /// Gets the element type.
        /// </summary>
        public Type ElementType => typeof(T);

        /// <summary>
        /// Gets the expression tree.
        /// </summary>
        public Expression Expression => expression;

        /// <summary>
        /// Gets the query provider.
        /// </summary>
        public IQueryProvider Provider => provider;

        #endregion

        #region Methods

        /// <summary>
        /// Returns an enumerator that iterates through the query results.
        /// </summary>
        /// <returns>An enumerator for the query results.</returns>
        public IEnumerator<T> GetEnumerator() => Execute().GetEnumerator();

        /// <summary>
        /// Returns an enumerator that iterates through the query results.
        /// </summary>
        /// <returns>An enumerator for the query results.</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Executes the query and returns the results as an enumerable.
        /// </summary>
        /// <returns>The query results.</returns>
        public IEnumerable<T> Execute()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GPUQueryable<T>));

            // Execute the expression tree and return results
            var executor = new GPUQueryExecutor(accelerator);
            return executor.Execute<T>(expression);
        }

        /// <summary>
        /// Executes the query and returns the results as an array.
        /// </summary>
        /// <returns>The query results as an array.</returns>
        public T[] ToArray() => Execute().ToArray();

        /// <summary>
        /// Executes the query and stores the results in the specified buffer.
        /// </summary>
        /// <param name="outputBuffer">The output buffer.</param>
        public void ExecuteTo(MemoryBuffer1D<T, Stride1D.Dense> outputBuffer)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GPUQueryable<T>));
            if (outputBuffer == null)
                throw new ArgumentNullException(nameof(outputBuffer));

            var executor = new GPUQueryExecutor(accelerator);
            executor.ExecuteTo(expression, outputBuffer);
        }

        /// <summary>
        /// Releases all resources used by the <see cref="GPUQueryable{T}"/>.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                buffer?.Dispose();
                disposed = true;
            }
        }

        #endregion
    }
}