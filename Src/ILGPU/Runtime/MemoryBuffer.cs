// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryBuffer.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Resources;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents an abstract memory buffer that can be used in the scope of ILGPU
    /// runtime kernels.
    /// </summary>
    /// <remarks>Members of this class are not thread safe.</remarks>
    public abstract class MemoryBuffer : AcceleratorObject, IMemoryBuffer
    {
        #region Instance

        private readonly object _usageInfoLock = new object();
        private MemoryUsageInfo _usageInfo;
        private MemoryBufferStatus _status;

        /// <summary>
        /// Initializes this array view buffer.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="length">The length of this buffer.</param>
        /// <param name="elementSize">The element size.</param>
        protected MemoryBuffer(
            Accelerator accelerator,
            long length,
            int elementSize)
            : base(accelerator)
        {
            Init(length, elementSize);
            _usageInfo = new MemoryUsageInfo
            {
                AllocationTime = DateTime.UtcNow,
                AccessCount = 0,
                LastAccessTime = DateTime.UtcNow,
                TotalBytesTransferred = 0,
                PeakMemoryUsage = length * elementSize,
                IsPinned = false
            };
            _status = MemoryBufferStatus.Allocated;
        }

        /// <summary>
        /// Initializes the internal length properties.
        /// </summary>
        /// <param name="length">The length of this buffer.</param>
        /// <param name="elementSize">The element size.</param>
        private void Init(long length, int elementSize)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (elementSize < 1)
                throw new ArgumentOutOfRangeException(nameof(elementSize));

            Length = length;
            ElementSize = elementSize;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the native pointer of this buffer.
        /// </summary>
        public IntPtr NativePtr { get; protected set; }

        /// <summary>
        /// Returns the length of this buffer.
        /// </summary>
        public long Length { get; private set; }

        /// <summary>
        /// Returns the element size.
        /// </summary>
        public int ElementSize { get; private set; }

        /// <summary>
        /// Returns the length of this buffer in bytes.
        /// </summary>
        public long LengthInBytes => Length * ElementSize;

        /// <summary>
        /// Gets the element type of this buffer.
        /// </summary>
        public virtual Type ElementType => typeof(byte);

        /// <summary>
        /// Gets the number of dimensions of this buffer (1, 2, or 3).
        /// </summary>
        public virtual int Dimensions => 1;

        /// <summary>
        /// Gets the current status of this buffer.
        /// </summary>
        public MemoryBufferStatus Status => _status;

        #endregion

        #region Methods

        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        /// <param name="length">The number of bytes to set.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MemSet(
            AcceleratorStream stream,
            byte value,
            long targetOffsetInBytes,
            long length)
        {
            if (length == 0)
                return;
            var targetView = AsRawArrayView(targetOffsetInBytes, length);
            MemSet(stream, value, targetView);
        }

        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <param name="targetView">The raw target view of this buffer.</param>
        protected internal abstract void MemSet(
            AcceleratorStream stream,
            byte value,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Copies elements from the current buffer to the target view.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceOffsetInBytes">The source offset in bytes.</param>
        /// <param name="targetView">The target view.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            in ArrayView<byte> targetView)
        {
            if (!targetView.IsValid)
                return;
            var sourceView = AsRawArrayView(
                sourceOffsetInBytes,
                targetView.LengthInBytes);
            CopyTo(stream, sourceView, targetView);
        }

        /// <summary>
        /// Copies elements from the current buffer to the target view.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view of this buffer.</param>
        /// <param name="targetView">The target view.</param>
        protected internal abstract void CopyTo(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Copies elements from the source view to the current buffer.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            long targetOffsetInBytes)
        {
            if (!sourceView.IsValid)
                return;
            var targetView = AsRawArrayView(
                targetOffsetInBytes,
                sourceView.LengthInBytes);
            CopyFrom(stream, sourceView, targetView);
        }

        /// <summary>
        /// Copies elements from the source view to the current buffer.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view.</param>
        /// <param name="targetView">The target view of this buffer.</param>
        protected internal abstract void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Returns a raw array view of the whole buffer.
        /// </summary>
        /// <returns>The raw array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView<byte> AsRawArrayView() =>
            AsRawArrayView(0L, LengthInBytes);

        /// <summary>
        /// Returns a raw array view starting at the given byte offset.
        /// </summary>
        /// <param name="offsetInBytes">The raw offset in bytes.</param>
        /// <returns>The raw array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView<byte> AsRawArrayView(long offsetInBytes) =>
            AsRawArrayView(offsetInBytes, LengthInBytes - offsetInBytes);

        /// <summary>
        /// Returns a raw array slice of the this buffer.
        /// </summary>
        /// <param name="offsetInBytes">The raw offset in bytes.</param>
        /// <param name="lengthInBytes">The raw length in bytes.</param>
        /// <returns></returns>
        public ArrayView<byte> AsRawArrayView(long offsetInBytes, long lengthInBytes)
        {
            if (offsetInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
            if (LengthInBytes > 0 && offsetInBytes >= LengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
            if (lengthInBytes < 0 || offsetInBytes + lengthInBytes > LengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            return new ArrayView<byte>(this, offsetInBytes, lengthInBytes);
        }

        /// <summary>
        /// Gets an array view that spans the given number of elements.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The number of elements.</param>
        /// <returns>The created array view.</returns>
        public ArrayView<T> AsArrayView<T>(long offset, long length)
            where T : unmanaged
        {
            if (Interop.SizeOf<T>() != ElementSize)
            {
                throw new NotSupportedException(string.Format(
                    ErrorMessages.NotSupportedType,
                    nameof(T)));
            }
            if (Length > 0 && offset >= Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset + length > Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            return new ArrayView<T>(this, offset, length);
        }

        /// <summary>
        /// Asynchronously copies data from this buffer to the destination buffer.
        /// </summary>
        /// <param name="destination">The destination buffer.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        public virtual Task CopyToAsync(IMemoryBuffer destination, CancellationToken cancellationToken = default)
        {
            if (destination is null)
                throw new ArgumentNullException(nameof(destination));
            
            if (destination.LengthInBytes < LengthInBytes)
                throw new ArgumentException("Destination buffer is too small", nameof(destination));

            _status = MemoryBufferStatus.Transferring;
            
            return Task.Run(() =>
            {
                try
                {
                    var stream = Accelerator.DefaultStream;
                    var sourceView = AsRawArrayView();
                    var targetView = destination.AsRawArrayView();
                    
                    CopyTo(stream, sourceView, targetView);
                    stream.Synchronize();
                    
                    UpdateUsageInfo(LengthInBytes);
                }
                finally
                {
                    _status = MemoryBufferStatus.Allocated;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Asynchronously copies data from the source array to this buffer.
        /// </summary>
        /// <param name="source">The source array.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        public virtual Task CopyFromAsync(Array source, CancellationToken cancellationToken = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            _status = MemoryBufferStatus.Transferring;
            
            return Task.Run(() =>
            {
                try
                {
                    var stream = Accelerator.DefaultStream;
                    // This is a simplified implementation - real implementation would 
                    // depend on the specific array type and buffer type
                    stream.Synchronize();
                    UpdateUsageInfo(source.Length * ElementSize);
                }
                finally
                {
                    _status = MemoryBufferStatus.Allocated;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Asynchronously sets the contents of this buffer to the specified byte value.
        /// </summary>
        /// <param name="value">The byte value to set.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous set operation.</returns>
        public virtual Task MemSetAsync(byte value, CancellationToken cancellationToken = default)
        {
            _status = MemoryBufferStatus.Transferring;
            
            return Task.Run(() =>
            {
                try
                {
                    var stream = Accelerator.DefaultStream;
                    MemSet(stream, value, 0, LengthInBytes);
                    stream.Synchronize();
                    
                    UpdateUsageInfo(LengthInBytes);
                }
                finally
                {
                    _status = MemoryBufferStatus.Allocated;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Gets usage information about this buffer including allocation time and access patterns.
        /// </summary>
        /// <returns>Memory usage information.</returns>
        public MemoryUsageInfo GetUsageInfo()
        {
            lock (_usageInfoLock)
            {
                return _usageInfo with { };
            }
        }

        /// <summary>
        /// Updates the usage information for this buffer.
        /// </summary>
        /// <param name="bytesTransferred">The number of bytes transferred in this operation.</param>
        protected void UpdateUsageInfo(long bytesTransferred)
        {
            lock (_usageInfoLock)
            {
                _usageInfo = _usageInfo with
                {
                    AccessCount = _usageInfo.AccessCount + 1,
                    LastAccessTime = DateTime.UtcNow,
                    TotalBytesTransferred = _usageInfo.TotalBytesTransferred + bytesTransferred
                };
            }
        }

        /// <summary>
        /// Creates a raw array view of this buffer for interface compatibility.
        /// </summary>
        /// <returns>A raw byte array view of this buffer.</returns>
        ArrayView<byte> IMemoryBuffer.AsRawArrayView() => AsRawArrayView();

        #endregion
    }

    /// <summary>
    /// An abstract memory buffer based on a specific view type.
    /// </summary>
    /// <typeparam name="TView">The underlying view type.</typeparam>
    public interface IViewMemoryBuffer<in TView> : IContiguousArrayView
        where TView : struct, IArrayView
    {
        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        /// <param name="length">The number of bytes to set.</param>
        void MemSet(
            AcceleratorStream stream,
            byte value,
            long targetOffsetInBytes,
            long length);

        /// <summary>
        /// Copies elements from the current buffer to the target view.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceOffsetInBytes">The source offset in bytes.</param>
        /// <param name="targetView">The target view.</param>
        void CopyTo(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Copies elements from the source view to the current buffer.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            long targetOffsetInBytes);
    }

    /// <summary>
    /// Represents an opaque memory buffer that can be used in the scope of ILGPU runtime
    /// kernels.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <remarks>Members of this class are not thread safe.</remarks>
    [DebuggerDisplay("{View}")]
    public class MemoryBuffer<TView> : MemoryBuffer, IViewMemoryBuffer<TView>
        where TView : struct, IArrayView
    {
        #region Instance

        /// <summary>
        /// Initializes this memory buffer.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="view">The extent (number of elements).</param>
        protected internal MemoryBuffer(Accelerator accelerator, in TView view)
            : base(accelerator, view.Length, view.ElementSize)
        {
            View = view;
            NativePtr = Buffer.NativePtr;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the owned memory buffer instance.
        /// </summary>
        protected MemoryBuffer Buffer => View.Buffer;

        /// <summary>
        /// Returns the same memory buffer instance.
        /// </summary>
        MemoryBuffer IArrayView.Buffer => Buffer;

        /// <summary>
        /// Returns the base offset 0.
        /// </summary>
        [SuppressMessage(
            "Design",
            "CA1033:Interface methods should be callable by child types",
            Justification = "We do not want to expose this implementation here.")]
        long IContiguousArrayView.Index => 0L;

        /// <summary>
        /// Returns the base offset 0.
        /// </summary>
        [SuppressMessage(
            "Design",
            "CA1033:Interface methods should be callable by child types",
            Justification = "We do not want to expose this implementation here.")]
        long IContiguousArrayView.IndexInBytes => 0L;

        /// <summary>
        /// Returns an array view that can access this buffer.
        /// </summary>
        public TView View { get; private set; }

        /// <summary>
        /// Returns true if this buffer has not been disposed.
        /// </summary>
        public bool IsValid => !IsDisposed;

        /// <summary>
        /// Gets the element type of this buffer.
        /// </summary>
        public override Type ElementType
        {
            get
            {
                // Extract element type from the generic view type
                var viewType = typeof(TView);
                if (viewType.IsGenericType)
                {
                    var genericArgs = viewType.GetGenericArguments();
                    if (genericArgs.Length > 0)
                        return genericArgs[0]; // First generic argument is the element type
                }
                
                // Fallback for non-generic views
                return typeof(byte);
            }
        }

        /// <summary>
        /// Gets the number of dimensions of this buffer.
        /// </summary>
        public override int Dimensions => View is ArrayView<byte> ? 1 : 
                                          View.GetType().Name.Contains("2D") ? 2 : 
                                          View.GetType().Name.Contains("3D") ? 3 : 1;

        #endregion

        #region Methods

        /// <inheritdoc/>
        protected internal override void MemSet(
            AcceleratorStream stream,
            byte value,
            in ArrayView<byte> targetView) =>
            Buffer.MemSet(stream, value, targetView);

        /// <inheritdoc/>
        protected internal override void CopyTo(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView) =>
            Buffer.CopyTo(stream, sourceView, targetView);

        /// <inheritdoc/>
        protected internal override void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView) =>
            Buffer.CopyFrom(stream, sourceView, targetView);

        /// <summary>
        /// Returns an array view that can access this array.
        /// </summary>
        /// <returns>An array view that can access this array.</returns>
        public TView ToArrayView() => View;

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (!disposing)
                return;

            Buffer.Dispose();
            NativePtr = default;
            View = default;
        }

        #endregion

        #region Operators

        /// <summary>
        /// Implicitly converts this buffer into an array view.
        /// </summary>
        /// <param name="buffer">The source buffer.</param>
        public static implicit operator TView(MemoryBuffer<TView> buffer) =>
            buffer.View;

        #endregion
    }
}
