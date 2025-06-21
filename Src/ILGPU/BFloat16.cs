// ---------------------------------------------------------------------------------------
//                                     ILGPU-AOT
//                        Copyright (c) 2024-2025 ILGPU-AOT Project

// Developed by:           Michael Ivertowski
//
//
// File: BFloat16.cs
//
// This file is part of ILGPU-AOT and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ILGPU
{
    /// <summary>
    /// Represents a brain floating-point number (bfloat16).
    /// </summary>
    /// <remarks>
    /// BFloat16 is a 16-bit floating point format that maintains the same exponent
    /// size as float32 (8 bits) but reduces the mantissa to 7 bits. This makes it
    /// particularly useful for machine learning workloads where the extended range
    /// is more important than precision.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 2)]
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public readonly struct BFloat16 : 
        IEquatable<BFloat16>, 
        IComparable<BFloat16>,
        IConvertible,
        IFormattable
    {
        #region Constants

        /// <summary>
        /// Represents positive zero.
        /// </summary>
        public static readonly BFloat16 Zero = new BFloat16(0x0000);

        /// <summary>
        /// Represents negative zero.
        /// </summary>
        public static readonly BFloat16 NegativeZero = new BFloat16(0x8000);

        /// <summary>
        /// Represents positive infinity.
        /// </summary>
        public static readonly BFloat16 PositiveInfinity = new BFloat16(0x7F80);

        /// <summary>
        /// Represents negative infinity.
        /// </summary>
        public static readonly BFloat16 NegativeInfinity = new BFloat16(0xFF80);

        /// <summary>
        /// Represents not-a-number (NaN).
        /// </summary>
        public static readonly BFloat16 NaN = new BFloat16(0x7FC0);

        /// <summary>
        /// Represents the smallest positive normalized value.
        /// </summary>
        public static readonly BFloat16 MinValue = new BFloat16(0xFF7F);

        /// <summary>
        /// Represents the largest positive value.
        /// </summary>
        public static readonly BFloat16 MaxValue = new BFloat16(0x7F7F);

        /// <summary>
        /// Represents the smallest positive subnormal value.
        /// </summary>
        public static readonly BFloat16 Epsilon = new BFloat16(0x0001);

        /// <summary>
        /// Represents one.
        /// </summary>
        public static readonly BFloat16 One = new BFloat16(0x3F80);

        /// <summary>
        /// Represents negative one.
        /// </summary>
        public static readonly BFloat16 NegativeOne = new BFloat16(0xBF80);

        #endregion

        #region Instance

        /// <summary>
        /// The raw 16-bit value.
        /// </summary>
        private readonly ushort value;

        /// <summary>
        /// Initializes a new BFloat16 from raw bits.
        /// </summary>
        /// <param name="rawValue">The raw 16-bit value.</param>
        private BFloat16(ushort rawValue)
        {
            value = rawValue;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the raw bits representation.
        /// </summary>
        public ushort RawValue => value;

        /// <summary>
        /// Returns true if this value is NaN.
        /// </summary>
        public bool IsNaN => (value & 0x7FFF) > 0x7F80;

        /// <summary>
        /// Returns true if this value is positive or negative infinity.
        /// </summary>
        public bool IsInfinity => (value & 0x7FFF) == 0x7F80;

        /// <summary>
        /// Returns true if this value is positive infinity.
        /// </summary>
        public bool IsPositiveInfinity => value == 0x7F80;

        /// <summary>
        /// Returns true if this value is negative infinity.
        /// </summary>
        public bool IsNegativeInfinity => value == 0xFF80;

        /// <summary>
        /// Returns true if this value is finite (not NaN or infinity).
        /// </summary>
        public bool IsFinite => (value & 0x7F80) != 0x7F80;

        /// <summary>
        /// Returns true if this value is negative.
        /// </summary>
        public bool IsNegative => (value & 0x8000) != 0;

        /// <summary>
        /// Returns true if this value is subnormal.
        /// </summary>
        public bool IsSubnormal => (value & 0x7F80) == 0 && (value & 0x007F) != 0;

        #endregion

        #region Conversions

        /// <summary>
        /// Converts a float to BFloat16.
        /// </summary>
        /// <param name="value">The float value to convert.</param>
        /// <returns>The BFloat16 representation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 FromFloat(float value)
        {
            uint bits = Unsafe.As<float, uint>(ref value);
            
            // Extract sign, exponent, and mantissa
            uint sign = bits & 0x80000000u;
            uint exp = bits & 0x7F800000u;
            uint mantissa = bits & 0x007FFFFFu;

            // Handle special cases
            if (exp == 0x7F800000u) // Infinity or NaN
            {
                ushort result = (ushort)((sign >> 16) | 0x7F80u);
                if (mantissa != 0) // NaN
                    result |= 0x0040; // Ensure NaN has a set mantissa bit
                return new BFloat16(result);
            }

            // Round to nearest even
            uint rounding = (mantissa & 0x00008000u) + 0x00007FFFu;
            if ((mantissa & 0x00018000u) == 0x00018000u)
                rounding = 0x00008000u;
            
            bits += rounding;
            
            // Extract new exponent after rounding
            exp = bits & 0x7F800000u;
            
            // Check for overflow to infinity
            if (exp == 0x7F800000u)
                return new BFloat16((ushort)((sign >> 16) | 0x7F80u));
            
            // Truncate mantissa and combine
            return new BFloat16((ushort)((bits >> 16) & 0xFFFF));
        }

        /// <summary>
        /// Converts a BFloat16 to float.
        /// </summary>
        /// <returns>The float representation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat()
        {
            uint bits = ((uint)value) << 16;
            return Unsafe.As<uint, float>(ref bits);
        }

        /// <summary>
        /// Implicit conversion from BFloat16 to float.
        /// </summary>
        public static implicit operator float(BFloat16 value) => value.ToFloat();

        /// <summary>
        /// Explicit conversion from float to BFloat16.
        /// </summary>
        public static explicit operator BFloat16(float value) => FromFloat(value);

        /// <summary>
        /// Explicit conversion from double to BFloat16.
        /// </summary>
        public static explicit operator BFloat16(double value) => FromFloat((float)value);

        #endregion

        #region Arithmetic Operations

        /// <summary>
        /// Adds two BFloat16 values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator +(BFloat16 left, BFloat16 right) =>
            FromFloat(left.ToFloat() + right.ToFloat());

        /// <summary>
        /// Subtracts two BFloat16 values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator -(BFloat16 left, BFloat16 right) =>
            FromFloat(left.ToFloat() - right.ToFloat());

        /// <summary>
        /// Multiplies two BFloat16 values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator *(BFloat16 left, BFloat16 right) =>
            FromFloat(left.ToFloat() * right.ToFloat());

        /// <summary>
        /// Divides two BFloat16 values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator /(BFloat16 left, BFloat16 right) =>
            FromFloat(left.ToFloat() / right.ToFloat());

        /// <summary>
        /// Negates a BFloat16 value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator -(BFloat16 value) =>
            new BFloat16((ushort)(value.value ^ 0x8000));

        /// <summary>
        /// Returns the value unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator +(BFloat16 value) => value;

        #endregion

        #region Comparison

        /// <summary>
        /// Compares two BFloat16 values for equality.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(BFloat16 left, BFloat16 right)
        {
            if (left.IsNaN || right.IsNaN)
                return false;
            return left.value == right.value;
        }

        /// <summary>
        /// Compares two BFloat16 values for inequality.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(BFloat16 left, BFloat16 right) => !(left == right);

        /// <summary>
        /// Compares if left is less than right.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(BFloat16 left, BFloat16 right) =>
            left.ToFloat() < right.ToFloat();

        /// <summary>
        /// Compares if left is greater than right.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(BFloat16 left, BFloat16 right) =>
            left.ToFloat() > right.ToFloat();

        /// <summary>
        /// Compares if left is less than or equal to right.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(BFloat16 left, BFloat16 right) =>
            left.ToFloat() <= right.ToFloat();

        /// <summary>
        /// Compares if left is greater than or equal to right.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(BFloat16 left, BFloat16 right) =>
            left.ToFloat() >= right.ToFloat();

        #endregion

        #region IEquatable

        /// <inheritdoc/>
        public bool Equals(BFloat16 other) => this == other;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is BFloat16 other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => value.GetHashCode();

        #endregion

        #region IComparable

        /// <inheritdoc/>
        public int CompareTo(BFloat16 other) => ToFloat().CompareTo(other.ToFloat());

        #endregion

        #region IConvertible

        /// <inheritdoc/>
        TypeCode IConvertible.GetTypeCode() => TypeCode.Single;

        /// <inheritdoc/>
        bool IConvertible.ToBoolean(IFormatProvider? provider) => ToFloat() != 0f;

        /// <inheritdoc/>
        byte IConvertible.ToByte(IFormatProvider? provider) => (byte)ToFloat();

        /// <inheritdoc/>
        char IConvertible.ToChar(IFormatProvider? provider) => (char)ToFloat();

        /// <inheritdoc/>
        DateTime IConvertible.ToDateTime(IFormatProvider? provider) => 
            throw new InvalidCastException("Cannot convert BFloat16 to DateTime");

        /// <inheritdoc/>
        decimal IConvertible.ToDecimal(IFormatProvider? provider) => (decimal)ToFloat();

        /// <inheritdoc/>
        double IConvertible.ToDouble(IFormatProvider? provider) => ToFloat();

        /// <inheritdoc/>
        short IConvertible.ToInt16(IFormatProvider? provider) => (short)ToFloat();

        /// <inheritdoc/>
        int IConvertible.ToInt32(IFormatProvider? provider) => (int)ToFloat();

        /// <inheritdoc/>
        long IConvertible.ToInt64(IFormatProvider? provider) => (long)ToFloat();

        /// <inheritdoc/>
        sbyte IConvertible.ToSByte(IFormatProvider? provider) => (sbyte)ToFloat();

        /// <inheritdoc/>
        float IConvertible.ToSingle(IFormatProvider? provider) => ToFloat();

        /// <inheritdoc/>
        string IConvertible.ToString(IFormatProvider? provider) => ToString(null, provider);

        /// <inheritdoc/>
        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType(ToFloat(), conversionType, provider);

        /// <inheritdoc/>
        ushort IConvertible.ToUInt16(IFormatProvider? provider) => (ushort)ToFloat();

        /// <inheritdoc/>
        uint IConvertible.ToUInt32(IFormatProvider? provider) => (uint)ToFloat();

        /// <inheritdoc/>
        ulong IConvertible.ToUInt64(IFormatProvider? provider) => (ulong)ToFloat();

        #endregion

        #region Object

        /// <inheritdoc/>
        public override string ToString() => ToFloat().ToString();

        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            ToFloat().ToString(format, formatProvider);

        /// <summary>
        /// Gets the debugger display string.
        /// </summary>
        private string GetDebuggerDisplay() => $"{ToFloat()} (0x{value:X4})";

        #endregion

        #region Math Functions

        /// <summary>
        /// Returns the absolute value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Abs(BFloat16 value) =>
            new BFloat16((ushort)(value.value & 0x7FFF));

        /// <summary>
        /// Returns the maximum of two values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Max(BFloat16 x, BFloat16 y) =>
            x > y ? x : y;

        /// <summary>
        /// Returns the minimum of two values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Min(BFloat16 x, BFloat16 y) =>
            x < y ? x : y;

        /// <summary>
        /// Returns the square root.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Sqrt(BFloat16 value) =>
            FromFloat(MathF.Sqrt(value.ToFloat()));

        #endregion
    }
}
