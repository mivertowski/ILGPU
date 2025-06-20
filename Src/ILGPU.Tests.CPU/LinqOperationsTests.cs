// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: LinqOperationsTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Runtime.LINQ;
using ILGPU.Runtime.CPU;
using System;
using System.Linq;
using Xunit;

namespace ILGPU.Tests.CPU
{
    /// <summary>
    /// Tests for LINQ-style GPU operations.
    /// </summary>
    public class LinqOperationsTests : IDisposable
    {
        #region Fields

        private readonly Context context;
        private readonly Accelerator accelerator;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="LinqOperationsTests"/> class.
        /// </summary>
        public LinqOperationsTests()
        {
            context = Context.Create(builder => builder.CPU());
            accelerator = context.CreateCPUaccelerator(0);
        }

        #endregion

        #region Test Methods

        [Fact]
        public void LinqOperations_AsGPUQueryable_FromArray()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var result = queryable.ToArray();
            
            Assert.Equal(sourceData, result);
        }

        [Fact]
        public void LinqOperations_AsGPUQueryable_FromBuffer()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            using var buffer = accelerator.Allocate1D(sourceData);
            
            using var queryable = buffer.AsGPUQueryable();
            var result = queryable.ToArray();
            
            Assert.Equal(sourceData, result);
        }

        [Fact]
        public void LinqOperations_AsGPUQueryable_FromSpan()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            ReadOnlySpan<int> span = sourceData;
            
            using var queryable = accelerator.AsGPUQueryable(span);
            var result = queryable.ToArray();
            
            Assert.Equal(sourceData, result);
        }

        [Fact]
        public void LinqOperations_Select_Transformation()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            var expected = sourceData.Select(x => x * 2).ToArray();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            using var result = queryable.Select(x => x * 2);
            var actual = result.ToArray();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Where_Filtering()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var expected = sourceData.Where(x => x % 2 == 0).ToArray();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            using var result = queryable.Where(x => x % 2 == 0);
            var actual = result.ToArray();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Sum_Reduction()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            var expected = sourceData.Sum();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Sum();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Average_Reduction()
        {
            var sourceData = new int[] { 2, 4, 6, 8, 10 };
            var expected = sourceData.Average();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Average();
            
            Assert.Equal(expected, actual, 5);
        }

        [Fact]
        public void LinqOperations_Min_Reduction()
        {
            var sourceData = new int[] { 5, 1, 9, 3, 7 };
            var expected = sourceData.Min();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Min();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Max_Reduction()
        {
            var sourceData = new int[] { 5, 1, 9, 3, 7 };
            var expected = sourceData.Max();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Max();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Count_Aggregation()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            var expected = sourceData.Count();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Count();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Any_Predicate()
        {
            var sourceData = new int[] { 1, 3, 5, 7, 9 };
            var expected = sourceData.Any(x => x > 5);
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Any(x => x > 5);
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_All_Predicate()
        {
            var sourceData = new int[] { 2, 4, 6, 8, 10 };
            var expected = sourceData.All(x => x % 2 == 0);
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.All(x => x % 2 == 0);
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_Aggregate_Accumulation()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            var expected = sourceData.Aggregate((a, b) => a + b);
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var actual = queryable.Aggregate((a, b) => a + b);
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_ChainedOperations()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var expected = sourceData
                .Where(x => x % 2 == 0)
                .Select(x => x * 3)
                .Sum();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            using var filtered = queryable.Where(x => x % 2 == 0);
            using var transformed = filtered.Select(x => x * 3);
            var actual = transformed.Sum();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_ForEach_Action()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            var processingFlags = new bool[sourceData.Length];
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            using var result = queryable.ForEach(x => { /* Custom processing */ });
            
            // Verify the operation completed successfully
            var resultData = result.ToArray();
            Assert.Equal(sourceData, resultData);
        }

        [Fact]
        public void LinqOperations_ExecuteTo_OutputBuffer()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            var expected = sourceData.Select(x => x * 2).ToArray();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            using var transformedQueryable = queryable.Select(x => x * 2);
            using var outputBuffer = accelerator.Allocate1D<int>(sourceData.Length);
            
            transformedQueryable.ExecuteTo(outputBuffer);
            var actual = outputBuffer.GetAsArray1D();
            
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_LargeDataSet_Performance()
        {
            const int dataSize = 100000;
            var sourceData = Enumerable.Range(1, dataSize).ToArray();
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            using var result = queryable
                .Where(x => x % 2 == 0)
                .Select(x => x * 2);
            
            var actual = result.ToArray();
            var expected = sourceData
                .Where(x => x % 2 == 0)
                .Select(x => x * 2)
                .ToArray();
            
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void LinqOperations_FloatingPoint_Operations()
        {
            var sourceData = new float[] { 1.5f, 2.5f, 3.5f, 4.5f, 5.5f };
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var sum = queryable.Sum();
            var average = queryable.Average();
            var min = queryable.Min();
            var max = queryable.Max();
            
            Assert.Equal(sourceData.Sum(), sum, 3);
            Assert.Equal(sourceData.Average(), average, 5);
            Assert.Equal(sourceData.Min(), min, 3);
            Assert.Equal(sourceData.Max(), max, 3);
        }

        [Fact]
        public void LinqOperations_EmptySequence_Handling()
        {
            var sourceData = new int[0];
            
            using var queryable = accelerator.AsGPUQueryable(sourceData);
            var count = queryable.Count();
            var any = queryable.Any(x => x > 0);
            var all = queryable.All(x => x > 0);
            
            Assert.Equal(0, count);
            Assert.False(any);
            Assert.True(all); // All() returns true for empty sequences
        }

        [Fact]
        public void LinqOperations_Dispose_Cleanup()
        {
            var sourceData = new int[] { 1, 2, 3, 4, 5 };
            
            var queryable = accelerator.AsGPUQueryable(sourceData);
            var result = queryable.ToArray();
            
            queryable.Dispose();
            
            // Verify the queryable was disposed properly
            Assert.Throws<ObjectDisposedException>(() => queryable.ToArray());
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes of the test resources.
        /// </summary>
        public void Dispose()
        {
            accelerator?.Dispose();
            context?.Dispose();
        }

        #endregion
    }
}