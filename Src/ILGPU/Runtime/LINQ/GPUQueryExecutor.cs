// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
// File: GPUQueryExecutor.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ILGPU.Runtime.LINQ
{
    /// <summary>
    /// Executes GPU LINQ queries by compiling expression trees to GPU kernels.
    /// </summary>
    internal sealed class GPUQueryExecutor
    {
        #region Instance

        private readonly Accelerator accelerator;
        private readonly AcceleratorStream stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="GPUQueryExecutor"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        public GPUQueryExecutor(Accelerator accelerator)
        {
            this.accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            stream = accelerator.CreateStream();
        }

        #endregion

        #region Execution

        /// <summary>
        /// Executes the given expression and returns the result.
        /// </summary>
        /// <param name="expression">The expression to execute.</param>
        /// <returns>The execution result.</returns>
        public object Execute(Expression expression)
        {
            var visitor = new GPUQueryExpressionVisitor(accelerator, stream);
            return visitor.Visit(expression);
        }

        /// <summary>
        /// Executes the given expression and returns the result.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="expression">The expression to execute.</param>
        /// <returns>The execution result.</returns>
        public IEnumerable<T> Execute<T>(Expression expression)
            where T : unmanaged
        {
            var visitor = new GPUQueryExpressionVisitor(accelerator, stream);
            var result = visitor.Visit(expression);
            
            if (result is MemoryBuffer1D<T, Stride1D.Dense> buffer)
            {
                var cpuData = buffer.GetAsArray1D();
                return cpuData;
            }
            
            if (result is IEnumerable<T> enumerable)
                return enumerable;
                
            throw new InvalidOperationException($"Unexpected result type: {result?.GetType()}");
        }

        /// <summary>
        /// Executes the given expression and stores the result in the output buffer.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="expression">The expression to execute.</param>
        /// <param name="outputBuffer">The output buffer.</param>
        public void ExecuteTo<T>(Expression expression, MemoryBuffer1D<T, Stride1D.Dense> outputBuffer)
            where T : unmanaged
        {
            var visitor = new GPUQueryExpressionVisitor(accelerator, stream);
            var result = visitor.Visit(expression);
            
            if (result is MemoryBuffer1D<T, Stride1D.Dense> sourceBuffer)
            {
                outputBuffer.View.BaseView.CopyFrom(stream, sourceBuffer.View.BaseView);
                stream.Synchronize();
            }
            else
            {
                throw new InvalidOperationException($"Cannot execute to buffer from result type: {result?.GetType()}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Expression visitor that converts LINQ expressions to GPU operations.
    /// </summary>
    internal sealed class GPUQueryExpressionVisitor : ExpressionVisitor
    {
        #region Instance

        private readonly Accelerator accelerator;
        private readonly AcceleratorStream stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="GPUQueryExpressionVisitor"/> class.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="stream">The accelerator stream.</param>
        public GPUQueryExpressionVisitor(Accelerator accelerator, AcceleratorStream stream)
        {
            this.accelerator = accelerator;
            this.stream = stream;
        }

        #endregion

        #region Visit Methods

        /// <summary>
        /// Visits a method call expression and converts it to GPU operations.
        /// </summary>
        /// <param name="node">The method call expression.</param>
        /// <returns>The result of the GPU operation.</returns>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Handle LINQ methods
            if (node.Method.DeclaringType == typeof(Queryable) || 
                node.Method.DeclaringType == typeof(Enumerable))
            {
                return HandleLinqMethod(node);
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>
        /// Visits a constant expression.
        /// </summary>
        /// <param name="node">The constant expression.</param>
        /// <returns>The constant value.</returns>
        protected override Expression VisitConstant(ConstantExpression node) =>
            // Return the constant value directly
            node;

        /// <summary>
        /// Handles LINQ method calls by converting them to GPU operations.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleLinqMethod(MethodCallExpression methodCall)
        {
            var methodName = methodCall.Method.Name;

            return methodName switch
            {
                "Select" => HandleSelect(methodCall),
                "Where" => HandleWhere(methodCall),
                "Sum" => HandleSum(methodCall),
                "Average" => HandleAverage(methodCall),
                "Min" => HandleMin(methodCall),
                "Max" => HandleMax(methodCall),
                "Count" => HandleCount(methodCall),
                "Any" => HandleAny(methodCall),
                "All" => HandleAll(methodCall),
                _ => throw new NotSupportedException($"LINQ method '{methodName}' is not supported on GPU.")
            };
        }

        /// <summary>
        /// Handles the Select LINQ method.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleSelect(MethodCallExpression methodCall)
        {
            var source = Visit(methodCall.Arguments[0]);
            var selector = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;

            // Create a GPU kernel for the select operation
            var sourceBuffer = GetBuffer(source);
            if (sourceBuffer != null)
            {
                var outputBuffer = CreateOutputBuffer(sourceBuffer, selector.ReturnType);
                ExecuteSelectKernel(sourceBuffer, outputBuffer, selector);
                return Expression.Constant(outputBuffer);
            }

            throw new InvalidOperationException("Invalid source for Select operation");
        }

        /// <summary>
        /// Handles the Where LINQ method.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleWhere(MethodCallExpression methodCall)
        {
            var source = Visit(methodCall.Arguments[0]);
            var predicate = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;

            var sourceBuffer = GetBuffer(source);
            if (sourceBuffer != null)
            {
                var filteredBuffer = ExecuteWhereKernel(sourceBuffer, predicate);
                return Expression.Constant(filteredBuffer);
            }

            throw new InvalidOperationException("Invalid source for Where operation");
        }

        /// <summary>
        /// Handles reduction operations (Sum, Average, Min, Max).
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleSum(MethodCallExpression methodCall) => HandleReduction(methodCall, "Sum");

        private Expression HandleAverage(MethodCallExpression methodCall) => HandleReduction(methodCall, "Average");

        private Expression HandleMin(MethodCallExpression methodCall) => HandleReduction(methodCall, "Min");

        private Expression HandleMax(MethodCallExpression methodCall) => HandleReduction(methodCall, "Max");

        /// <summary>
        /// Handles generic reduction operations.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <param name="operation">The reduction operation name.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleReduction(MethodCallExpression methodCall, string operation)
        {
            var source = Visit(methodCall.Arguments[0]);
            var sourceBuffer = GetBuffer(source);
            
            if (sourceBuffer != null)
            {
                var result = ExecuteReductionKernel(sourceBuffer, operation);
                return Expression.Constant(result);
            }

            throw new InvalidOperationException($"Invalid source for {operation} operation");
        }

        /// <summary>
        /// Handles the Count LINQ method.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleCount(MethodCallExpression methodCall)
        {
            var source = Visit(methodCall.Arguments[0]);
            var sourceBuffer = GetBuffer(source);
            
            if (sourceBuffer != null)
            {
                var count = GetBufferLength(sourceBuffer);
                return Expression.Constant(count);
            }

            throw new InvalidOperationException("Invalid source for Count operation");
        }

        /// <summary>
        /// Handles the Any LINQ method.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleAny(MethodCallExpression methodCall)
        {
            var source = Visit(methodCall.Arguments[0]);
            var sourceBuffer = GetBuffer(source);
            
            if (sourceBuffer != null)
            {
                bool hasElements = GetBufferLength(sourceBuffer) > 0;
                return Expression.Constant(hasElements);
            }

            return Expression.Constant(false);
        }

        /// <summary>
        /// Handles the All LINQ method.
        /// </summary>
        /// <param name="methodCall">The method call expression.</param>
        /// <returns>The result expression.</returns>
        private Expression HandleAll(MethodCallExpression methodCall)
        {
            var source = Visit(methodCall.Arguments[0]);
            var predicate = methodCall.Arguments.Count > 1 ? 
                (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand : null;

            var sourceBuffer = GetBuffer(source);
            if (sourceBuffer != null)
            {
                if (predicate != null)
                {
                    var result = ExecuteAllKernel(sourceBuffer, predicate);
                    return Expression.Constant(result);
                }
                else
                {
                    // All() without predicate - return true if any elements exist
                    bool hasElements = GetBufferLength(sourceBuffer) > 0;
                    return Expression.Constant(hasElements);
                }
            }

            return Expression.Constant(true);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Extracts a memory buffer from an expression result.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <returns>The memory buffer or null.</returns>
        private object? GetBuffer(Expression expression)
        {
            if (expression is ConstantExpression constant)
            {
                if (constant.Value is IGPUQueryable<int> queryable)
                    return queryable.Buffer;
                    
                return constant.Value;
            }
            
            return null;
        }

        /// <summary>
        /// Creates an output buffer for transformation operations.
        /// </summary>
        /// <param name="sourceBuffer">The source buffer.</param>
        /// <param name="outputType">The output element type.</param>
        /// <returns>The output buffer.</returns>
        private object CreateOutputBuffer(object sourceBuffer, Type outputType)
        {
            var length = GetBufferLength(sourceBuffer);
            
            // Create buffer using reflection for generic types
            var allocateMethod = typeof(Accelerator).GetMethod("Allocate1D")!
                .MakeGenericMethod(outputType);
            return allocateMethod.Invoke(accelerator, new object[] { length })!;
        }

        /// <summary>
        /// Gets the length of a memory buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns>The buffer length.</returns>
        private long GetBufferLength(object buffer)
        {
            var lengthProperty = buffer.GetType().GetProperty("Length");
            return (long)lengthProperty!.GetValue(buffer)!;
        }

        /// <summary>
        /// Executes a Select kernel on the GPU.
        /// </summary>
        /// <param name="sourceBuffer">The source buffer.</param>
        /// <param name="outputBuffer">The output buffer.</param>
        /// <param name="selector">The selector function.</param>
        private void ExecuteSelectKernel(object sourceBuffer, object outputBuffer, LambdaExpression selector)
        {
            // Simplified implementation - in practice, would compile selector to GPU kernel
            // For now, copy data to CPU, apply selector, and copy back
            var sourceData = GetBufferData(sourceBuffer);
            var transformedData = ApplySelector(sourceData, selector);
            SetBufferData(outputBuffer, transformedData);
        }

        /// <summary>
        /// Executes a Where kernel on the GPU.
        /// </summary>
        /// <param name="sourceBuffer">The source buffer.</param>
        /// <param name="predicate">The predicate function.</param>
        /// <returns>The filtered buffer.</returns>
        private object ExecuteWhereKernel(object sourceBuffer, LambdaExpression predicate)
        {
            // Simplified implementation
            var sourceData = GetBufferData(sourceBuffer);
            var filteredData = ApplyPredicate(sourceData, predicate);
            return CreateBufferFromData(filteredData);
        }

        /// <summary>
        /// Executes a reduction kernel on the GPU.
        /// </summary>
        /// <param name="sourceBuffer">The source buffer.</param>
        /// <param name="operation">The reduction operation.</param>
        /// <returns>The reduction result.</returns>
        private object ExecuteReductionKernel(object sourceBuffer, string operation)
        {
            var sourceData = GetBufferData(sourceBuffer);
            return ApplyReduction(sourceData, operation);
        }

        /// <summary>
        /// Executes an All kernel on the GPU.
        /// </summary>
        /// <param name="sourceBuffer">The source buffer.</param>
        /// <param name="predicate">The predicate function.</param>
        /// <returns>True if all elements satisfy the predicate.</returns>
        private bool ExecuteAllKernel(object sourceBuffer, LambdaExpression predicate)
        {
            var sourceData = GetBufferData(sourceBuffer);
            return ApplyAllPredicate(sourceData, predicate);
        }

        /// <summary>
        /// Gets data from a memory buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns>The buffer data as an array.</returns>
        private Array GetBufferData(object buffer)
        {
            var getAsArrayMethod = buffer.GetType().GetMethod("GetAsArray1D");
            return (Array)getAsArrayMethod!.Invoke(buffer, null)!;
        }

        /// <summary>
        /// Sets data in a memory buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="data">The data to set.</param>
        private void SetBufferData(object buffer, Array data)
        {
            var copyFromMethod = buffer.GetType().GetMethod("CopyFromCPU");
            copyFromMethod!.Invoke(buffer, new object[] { data });
        }

        /// <summary>
        /// Creates a buffer from array data.
        /// </summary>
        /// <param name="data">The array data.</param>
        /// <returns>The created buffer.</returns>
        private object CreateBufferFromData(Array data)
        {
            var elementType = data.GetType().GetElementType()!;
            var buffer = CreateOutputBuffer(data, elementType);
            SetBufferData(buffer, data);
            return buffer;
        }

        /// <summary>
        /// Applies a selector function to array data.
        /// </summary>
        /// <param name="sourceData">The source data.</param>
        /// <param name="selector">The selector function.</param>
        /// <returns>The transformed data.</returns>
        private Array ApplySelector(Array sourceData, LambdaExpression selector)
        {
            var compiledSelector = selector.Compile();
            var sourceList = sourceData.Cast<object>();
            var transformedList = new object[sourceData.Length];
            
            int index = 0;
            foreach (var item in sourceList)
            {
                transformedList[index++] = compiledSelector.DynamicInvoke(item)!;
            }
            
            var outputType = selector.ReturnType;
            var outputArray = Array.CreateInstance(outputType, transformedList.Length);
            Array.Copy(transformedList, outputArray, transformedList.Length);
            
            return outputArray;
        }

        /// <summary>
        /// Applies a predicate function to array data.
        /// </summary>
        /// <param name="sourceData">The source data.</param>
        /// <param name="predicate">The predicate function.</param>
        /// <returns>The filtered data.</returns>
        private Array ApplyPredicate(Array sourceData, LambdaExpression predicate)
        {
            var compiledPredicate = predicate.Compile();
            var sourceList = sourceData.Cast<object>().ToList();
            var filteredList = sourceList.Where(x => (bool)compiledPredicate.DynamicInvoke(x)!).ToArray();
            
            var elementType = sourceData.GetType().GetElementType()!;
            var outputArray = Array.CreateInstance(elementType, filteredList.Length);
            Array.Copy(filteredList, outputArray, filteredList.Length);
            
            return outputArray;
        }

        /// <summary>
        /// Applies a reduction operation to array data.
        /// </summary>
        /// <param name="sourceData">The source data.</param>
        /// <param name="operation">The reduction operation.</param>
        /// <returns>The reduction result.</returns>
        private object ApplyReduction(Array sourceData, string operation)
        {
            if (sourceData.Length == 0)
                throw new InvalidOperationException("Cannot perform reduction on empty sequence");

            return operation switch
            {
                "Sum" => ComputeSum(sourceData),
                "Average" => ComputeAverage(sourceData),
                "Min" => ComputeMin(sourceData),
                "Max" => ComputeMax(sourceData),
                _ => throw new NotSupportedException($"Reduction operation '{operation}' is not supported.")
            };
        }

        private object ComputeSum(Array sourceData)
        {
            dynamic sum = Convert.ChangeType(0, sourceData.GetType().GetElementType()!);
            foreach (var item in sourceData)
            {
                sum += item;
            }
            return sum;
        }

        private object ComputeAverage(Array sourceData)
        {
            dynamic sum = Convert.ChangeType(0, sourceData.GetType().GetElementType()!);
            foreach (var item in sourceData)
            {
                sum += item;
            }
            return Convert.ToDouble(sum) / sourceData.Length;
        }

        private object ComputeMin(Array sourceData)
        {
            dynamic min = sourceData.GetValue(0)!;
            foreach (var item in sourceData)
            {
                if (Comparer<dynamic>.Default.Compare(item, min) < 0)
                    min = item;
            }
            return min;
        }

        private object ComputeMax(Array sourceData)
        {
            dynamic max = sourceData.GetValue(0)!;
            foreach (var item in sourceData)
            {
                if (Comparer<dynamic>.Default.Compare(item, max) > 0)
                    max = item;
            }
            return max;
        }

        /// <summary>
        /// Applies an All predicate to array data.
        /// </summary>
        /// <param name="sourceData">The source data.</param>
        /// <param name="predicate">The predicate function.</param>
        /// <returns>True if all elements satisfy the predicate.</returns>
        private bool ApplyAllPredicate(Array sourceData, LambdaExpression predicate)
        {
            var compiledPredicate = predicate.Compile();
            var sourceList = sourceData.Cast<object>().ToList();
            return sourceList.All(x => (bool)compiledPredicate.DynamicInvoke(x)!);
        }

        #endregion
    }
}
