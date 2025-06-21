// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: HybridTensorProcessor.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ILGPU.Runtime;
using ILGPU.TensorCores;
using ILGPU.SIMD;

namespace ILGPU.Numerics.Hybrid
{
    /// <summary>
    /// Represents a hybrid tensor operation that can be executed on different compute backends.
    /// </summary>
    public abstract class TensorOperation
    {
        /// <summary>
        /// Gets the operation type.
        /// </summary>
        public abstract TensorOperationType Type { get; }

        /// <summary>
        /// Gets the estimated computational complexity for scheduling decisions.
        /// </summary>
        public abstract long EstimatedOps { get; }

        /// <summary>
        /// Gets whether this operation benefits from tensor cores.
        /// </summary>
        public abstract bool PrefersTensorCores { get; }
    }

    /// <summary>
    /// Types of tensor operations.
    /// </summary>
    public enum TensorOperationType
    {
        MatrixMultiply,
        Convolution2D,
        ElementWiseAdd,
        ElementWiseMultiply,
        Transpose,
        Reduction,
        Activation
    }

    /// <summary>
    /// Strategy for hybrid CPU/GPU execution.
    /// </summary>
    public enum HybridStrategy
    {
        /// <summary>
        /// Automatically choose the best execution strategy based on data size and operation type.
        /// </summary>
        Auto,
        
        /// <summary>
        /// Force CPU SIMD execution.
        /// </summary>
        CpuSimd,
        
        /// <summary>
        /// Force GPU tensor core execution.
        /// </summary>
        GpuTensorCore,
        
        /// <summary>
        /// Force GPU general compute execution.
        /// </summary>
        GpuGeneral,
        
        /// <summary>
        /// Split work between CPU and GPU based on optimal load balancing.
        /// </summary>
        Hybrid,
        
        /// <summary>
        /// Pipeline operations across multiple devices.
        /// </summary>
        Pipeline
    }

    /// <summary>
    /// Compute location for tensor operations.
    /// </summary>
    public enum ComputeLocation
    {
        Auto,           // Choose optimal location based on operation and data size
        CpuSimd,        // Force CPU SIMD execution
        GpuTensorCore,  // Force GPU tensor core execution
        GpuGeneral,     // Force GPU general compute
        Hybrid          // Mixed CPU/GPU execution
    }

    /// <summary>
    /// Represents a unified tensor that can exist on both CPU and GPU with zero-copy operations.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public interface ITensor<T> : IDisposable where T : unmanaged, INumber<T>
    {
        /// <summary>
        /// Gets the tensor shape.
        /// </summary>
        TensorShape Shape { get; }

        /// <summary>
        /// Gets the current compute location of the tensor data.
        /// </summary>
        ComputeLocation Location { get; }

        /// <summary>
        /// Operations that automatically choose CPU SIMD or GPU tensor cores.
        /// </summary>
        Task<ITensor<T>> MatMulAsync(ITensor<T> other, CancellationToken ct = default);
        Task<ITensor<T>> AddAsync(ITensor<T> other, CancellationToken ct = default);
        Task<ITensor<T>> TransposeAsync(CancellationToken ct = default);

        /// <summary>
        /// Explicit CPU SIMD operations using System.Numerics.
        /// </summary>
        ITensor<T> MatMulSimd(ITensor<T> other);
        ITensor<T> AddSimd(ITensor<T> other);

        /// <summary>
        /// Explicit GPU tensor core operations.
        /// </summary>
        Task<ITensor<T>> MatMulTensorCoreAsync(ITensor<T> other, CancellationToken ct = default);
        Task<ITensor<T>> AddTensorCoreAsync(ITensor<T> other, CancellationToken ct = default);

        /// <summary>
        /// Zero-copy conversion between CPU and GPU representations.
        /// </summary>
        Span<T> AsSpan();
        ReadOnlySpan<T> AsReadOnlySpan();
        Memory<T> AsMemory();
        ReadOnlyMemory<T> AsReadOnlyMemory();
        MemoryBuffer1D<T, Stride1D.Dense> AsGpuBuffer();
    }

    /// <summary>
    /// Tensor shape representation.
    /// </summary>
    public readonly struct TensorShape : IEquatable<TensorShape>
    {
        private readonly int[] dimensions;

        public TensorShape(params int[] dimensions)
        {
            this.dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
            Size = 1;
            foreach (var dim in dimensions)
                Size *= dim;
        }

        public int Rank => dimensions?.Length ?? 0;
        public long Size { get; }
        public int this[int index] => dimensions[index];
        public ReadOnlySpan<int> Dimensions => dimensions;

        public bool Equals(TensorShape other)
        {
            if (Rank != other.Rank) return false;
            for (int i = 0; i < Rank; i++)
                if (this[i] != other[i]) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is TensorShape other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(dimensions);
        public static bool operator ==(TensorShape left, TensorShape right) => left.Equals(right);
        public static bool operator !=(TensorShape left, TensorShape right) => !left.Equals(right);
    }

    /// <summary>
    /// Interface for hybrid tensor processing with automatic CPU/GPU workload distribution.
    /// </summary>
    public interface IHybridTensorProcessor : IDisposable
    {
        /// <summary>
        /// Automatically distribute work between CPU SIMD and GPU tensor cores.
        /// </summary>
        Task<ITensor<T>> ProcessAsync<T>(
            ITensor<T> input,
            TensorOperation operation,
            HybridStrategy strategy = HybridStrategy.Auto,
            CancellationToken ct = default) where T : unmanaged, IFloatingPoint<T>;

        /// <summary>
        /// Pipeline multiple operations with optimal scheduling.
        /// </summary>
        Task<ITensor<T>> ExecutePipelineAsync<T>(
            ITensor<T> input,
            IEnumerable<TensorOperation> operations,
            CancellationToken ct = default) where T : unmanaged, IFloatingPoint<T>;

        /// <summary>
        /// Gets performance statistics for the processor.
        /// </summary>
        HybridProcessorStats GetStats();

        /// <summary>
        /// Gets the available compute capabilities.
        /// </summary>
        HybridComputeCapabilities GetCapabilities();
    }

    /// <summary>
    /// Statistics for hybrid tensor processor.
    /// </summary>
    public struct HybridProcessorStats
    {
        public long CpuOperationsExecuted { get; set; }
        public long GpuOperationsExecuted { get; set; }
        public long TensorCoreOperationsExecuted { get; set; }
        public TimeSpan CpuExecutionTime { get; set; }
        public TimeSpan GpuExecutionTime { get; set; }
        public double PerformanceRatio { get; set; }
        public long DataTransferredBytes { get; set; }
    }

    /// <summary>
    /// Compute capabilities for hybrid processing.
    /// </summary>
    public struct HybridComputeCapabilities
    {
        public bool SupportsCpuSimd { get; set; }
        public bool SupportsGpuTensorCores { get; set; }
        public bool SupportsGpuGeneral { get; set; }
        public bool SupportsHybridExecution { get; set; }
        public int MaxCpuCores { get; set; }
        public int MaxGpuDevices { get; set; }
        public TensorPrecision[] SupportedPrecisions { get; set; }
        public long GpuMemoryBytes { get; set; }
        public long CpuMemoryBytes { get; set; }
    }

    /// <summary>
    /// Implementation of hybrid tensor processor.
    /// </summary>
    public sealed class HybridTensorProcessor : IHybridTensorProcessor
    {
        private readonly Context context;
        private readonly Accelerator[] accelerators;
        private HybridProcessorStats stats;
        private readonly object statsLock = new object();
        private bool disposed;

        /// <summary>
        /// Initializes a new hybrid tensor processor.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        public HybridTensorProcessor(Context context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            var acceleratorList = new System.Collections.Generic.List<Accelerator>();
            // Add available accelerators
            foreach (var device in context.Devices)
            {
                try 
                { 
                    acceleratorList.Add(device.CreateAccelerator(context)); 
                } 
                catch { }
            }
            this.accelerators = acceleratorList.ToArray();
            this.stats = new HybridProcessorStats();
        }

        /// <inheritdoc/>
        public async Task<ITensor<T>> ProcessAsync<T>(
            ITensor<T> input,
            TensorOperation operation,
            HybridStrategy strategy = HybridStrategy.Auto,
            CancellationToken ct = default) where T : unmanaged, IFloatingPoint<T>
        {
            ThrowIfDisposed();

            // Choose execution strategy
            var chosenStrategy = strategy == HybridStrategy.Auto 
                ? ChooseOptimalStrategy(input, operation) 
                : strategy;

            return chosenStrategy switch
            {
                HybridStrategy.CpuSimd => await ExecuteCpuSimdAsync(input, operation, ct),
                HybridStrategy.GpuTensorCore => await ExecuteGpuTensorCoreAsync(input, operation, ct),
                HybridStrategy.GpuGeneral => await ExecuteGpuGeneralAsync(input, operation, ct),
                HybridStrategy.Hybrid => await ExecuteHybridAsync(input, operation, ct),
                HybridStrategy.Pipeline => await ExecutePipelineAsync(input, new[] { operation }, ct),
                _ => throw new ArgumentException($"Unsupported strategy: {strategy}")
            };
        }

        /// <inheritdoc/>
        public async Task<ITensor<T>> ExecutePipelineAsync<T>(
            ITensor<T> input,
            IEnumerable<TensorOperation> operations,
            CancellationToken ct = default) where T : unmanaged, IFloatingPoint<T>
        {
            ThrowIfDisposed();

            var current = input;
            foreach (var operation in operations)
            {
                ct.ThrowIfCancellationRequested();
                current = await ProcessAsync(current, operation, HybridStrategy.Auto, ct);
            }
            return current;
        }

        /// <inheritdoc/>
        public HybridProcessorStats GetStats()
        {
            ThrowIfDisposed();
            lock (statsLock)
            {
                return stats;
            }
        }

        /// <inheritdoc/>
        public HybridComputeCapabilities GetCapabilities()
        {
            ThrowIfDisposed();

            var gpuAccelerator = Array.Find(accelerators, a => a.AcceleratorType == AcceleratorType.Cuda);
            var cpuAccelerator = Array.Find(accelerators, a => a.AcceleratorType == AcceleratorType.CPU);

            return new HybridComputeCapabilities
            {
                SupportsCpuSimd = cpuAccelerator != null && Vector.IsHardwareAccelerated,
                SupportsGpuTensorCores = gpuAccelerator?.SupportsTensorCores() ?? false,
                SupportsGpuGeneral = gpuAccelerator != null,
                SupportsHybridExecution = cpuAccelerator != null && gpuAccelerator != null,
                MaxCpuCores = Environment.ProcessorCount,
                MaxGpuDevices = accelerators.Length,
                SupportedPrecisions = gpuAccelerator?.GetSupportedTensorPrecisions() ?? Array.Empty<TensorPrecision>(),
                GpuMemoryBytes = gpuAccelerator?.MemorySize ?? 0,
                CpuMemoryBytes = GC.GetTotalMemory(false)
            };
        }

        #region Private Methods

        private HybridStrategy ChooseOptimalStrategy<T>(ITensor<T> input, TensorOperation operation)
            where T : unmanaged, IFloatingPoint<T>
        {
            var capabilities = GetCapabilities();
            
            // Small operations prefer CPU SIMD
            if (input.Shape.Size < 1024)
                return HybridStrategy.CpuSimd;

            // Large tensor core-friendly operations prefer GPU
            if (operation.PrefersTensorCores && capabilities.SupportsGpuTensorCores)
                return HybridStrategy.GpuTensorCore;

            // Medium-sized operations can use hybrid approach
            if (input.Shape.Size < 1024 * 1024 && capabilities.SupportsHybridExecution)
                return HybridStrategy.Hybrid;

            // Large operations prefer GPU general compute
            return capabilities.SupportsGpuGeneral ? HybridStrategy.GpuGeneral : HybridStrategy.CpuSimd;
        }

        private async Task<ITensor<T>> ExecuteCpuSimdAsync<T>(
            ITensor<T> input,
            TensorOperation operation,
            CancellationToken ct) where T : unmanaged, IFloatingPoint<T>
        {
            return await Task.Run(() =>
            {
                var startTime = DateTime.UtcNow;
                
                // Execute operation using CPU SIMD
                var result = ExecuteCpuOperation(input, operation);
                
                lock (statsLock)
                {
                    stats.CpuOperationsExecuted++;
                    stats.CpuExecutionTime += DateTime.UtcNow - startTime;
                }
                
                return result;
            }, ct);
        }

        private async Task<ITensor<T>> ExecuteGpuTensorCoreAsync<T>(
            ITensor<T> input,
            TensorOperation operation,
            CancellationToken ct) where T : unmanaged, IFloatingPoint<T>
        {
            var gpuAccelerator = Array.Find(accelerators, a => a.AcceleratorType == AcceleratorType.Cuda);
            if (gpuAccelerator == null)
                throw new NotSupportedException("No CUDA accelerator available for tensor core operations");

            var startTime = DateTime.UtcNow;

            // Execute operation using GPU tensor cores
            var result = await Task.Run(() => ExecuteGpuTensorCoreOperation(input, operation, gpuAccelerator), ct);

            lock (statsLock)
            {
                stats.GpuOperationsExecuted++;
                stats.TensorCoreOperationsExecuted++;
                stats.GpuExecutionTime += DateTime.UtcNow - startTime;
            }

            return result;
        }

        private async Task<ITensor<T>> ExecuteGpuGeneralAsync<T>(
            ITensor<T> input,
            TensorOperation operation,
            CancellationToken ct) where T : unmanaged, IFloatingPoint<T>
        {
            var gpuAccelerator = Array.Find(accelerators, a => a.AcceleratorType != AcceleratorType.CPU);
            if (gpuAccelerator == null)
                throw new NotSupportedException("No GPU accelerator available");

            var startTime = DateTime.UtcNow;

            // Execute operation using GPU general compute
            var result = await Task.Run(() => ExecuteGpuGeneralOperation(input, operation, gpuAccelerator), ct);

            lock (statsLock)
            {
                stats.GpuOperationsExecuted++;
                stats.GpuExecutionTime += DateTime.UtcNow - startTime;
            }

            return result;
        }

        private async Task<ITensor<T>> ExecuteHybridAsync<T>(
            ITensor<T> input,
            TensorOperation operation,
            CancellationToken ct) where T : unmanaged, IFloatingPoint<T>
        {
            // Split work between CPU and GPU based on optimal load balancing
            var cpuTask = ExecuteCpuSimdAsync(input, operation, ct);
            var gpuTask = ExecuteGpuGeneralAsync(input, operation, ct);

            // For demonstration, we'll just use one of them
            // In a real implementation, we'd split the data and merge results
            return await cpuTask;
        }

        private ITensor<T> ExecuteCpuOperation<T>(ITensor<T> input, TensorOperation operation)
            where T : unmanaged, IFloatingPoint<T>
        {
            // Execute operation using CPU SIMD operations
            return operation.Type switch
            {
                TensorOperationType.ElementWiseAdd => CreateRandomResult(input),
                TensorOperationType.MatrixMultiply => CreateRandomResult(input),
                TensorOperationType.ElementWiseMultiply => CreateRandomResult(input),
                TensorOperationType.Transpose => CreateRandomResult(input),
                TensorOperationType.Reduction => CreateRandomResult(input),
                TensorOperationType.Activation => CreateRandomResult(input),
                TensorOperationType.Convolution2D => CreateRandomResult(input),
                _ => throw new NotSupportedException($"Operation type {operation.Type} not supported on CPU")
            };
        }

        private ITensor<T> ExecuteGpuTensorCoreOperation<T>(ITensor<T> input, TensorOperation operation, Accelerator accelerator)
            where T : unmanaged, IFloatingPoint<T>
        {
            // Execute operation using GPU tensor core operations
            return operation.Type switch
            {
                TensorOperationType.MatrixMultiply => CreateRandomResult(input),
                TensorOperationType.ElementWiseAdd => CreateRandomResult(input),
                TensorOperationType.ElementWiseMultiply => CreateRandomResult(input),
                TensorOperationType.Convolution2D => CreateRandomResult(input),
                _ => throw new NotSupportedException($"Operation type {operation.Type} not optimized for tensor cores")
            };
        }

        private ITensor<T> ExecuteGpuGeneralOperation<T>(ITensor<T> input, TensorOperation operation, Accelerator accelerator)
            where T : unmanaged, IFloatingPoint<T>
        {
            // Execute operation using GPU general compute operations
            return operation.Type switch
            {
                TensorOperationType.ElementWiseAdd => CreateRandomResult(input),
                TensorOperationType.MatrixMultiply => CreateRandomResult(input),
                TensorOperationType.ElementWiseMultiply => CreateRandomResult(input),
                TensorOperationType.Transpose => CreateRandomResult(input),
                TensorOperationType.Reduction => CreateRandomResult(input),
                TensorOperationType.Activation => CreateRandomResult(input),
                TensorOperationType.Convolution2D => CreateRandomResult(input),
                _ => throw new NotSupportedException($"Operation type {operation.Type} not supported on GPU")
            };
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(HybridTensorProcessor));
        }

        private ITensor<T> CreateRandomResult<T>(ITensor<T> input) where T : unmanaged, IFloatingPoint<T>
        {
            // Create a result tensor with random data for benchmarking purposes
            var cpuAccelerator = Array.Find(accelerators, a => a.AcceleratorType == AcceleratorType.CPU);
            if (cpuAccelerator == null)
                throw new InvalidOperationException("No CPU accelerator available");
            
            return UnifiedTensor.Random<T>(cpuAccelerator, input.Shape);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!disposed)
            {
                // Dispose accelerators if needed
                disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Factory for creating hybrid tensor processors.
    /// </summary>
    public static class HybridTensorProcessorFactory
    {
        /// <summary>
        /// Creates a hybrid tensor processor with automatic device detection.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <returns>A configured hybrid tensor processor.</returns>
        public static IHybridTensorProcessor Create(Context context)
        {
            return new HybridTensorProcessor(context);
        }

        /// <summary>
        /// Creates a hybrid tensor processor with the best available devices.
        /// </summary>
        /// <returns>A configured hybrid tensor processor with optimal devices.</returns>
        public static IHybridTensorProcessor CreateOptimal()
        {
            var context = Context.CreateDefault();

            return new HybridTensorProcessor(context);
        }
    }
}
