// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: BFloat16Tests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using Xunit;

namespace ILGPU.Tests.CPU
{
    public class BFloat16Tests
    {
        [Fact]
        public void BFloat16_Constants_AreCorrect()
        {
            Assert.Equal((ushort)0x0000, BFloat16.Zero.RawValue);
            Assert.Equal((ushort)0x8000, BFloat16.NegativeZero.RawValue);
            Assert.Equal((ushort)0x7F80, BFloat16.PositiveInfinity.RawValue);
            Assert.Equal((ushort)0xFF80, BFloat16.NegativeInfinity.RawValue);
            Assert.Equal((ushort)0x3F80, BFloat16.One.RawValue);
            Assert.Equal((ushort)0xBF80, BFloat16.NegativeOne.RawValue);
        }

        [Fact]
        public void BFloat16_FromFloat_BasicConversion()
        {
            var bf16_1 = BFloat16.FromFloat(1.0f);
            var bf16_0 = BFloat16.FromFloat(0.0f);
            var bf16_neg1 = BFloat16.FromFloat(-1.0f);

            Assert.Equal(BFloat16.One.RawValue, bf16_1.RawValue);
            Assert.Equal(BFloat16.Zero.RawValue, bf16_0.RawValue);
            Assert.Equal(BFloat16.NegativeOne.RawValue, bf16_neg1.RawValue);
        }

        [Fact]
        public void BFloat16_ToFloat_BasicConversion()
        {
            Assert.Equal(1.0f, BFloat16.One.ToFloat());
            Assert.Equal(0.0f, BFloat16.Zero.ToFloat());
            Assert.Equal(-1.0f, BFloat16.NegativeOne.ToFloat());
        }

        [Fact]
        public void BFloat16_RoundTrip_PreservesValue()
        {
            float[] testValues = { 0.0f, 1.0f, -1.0f, 42.0f, -42.0f, 0.5f, -0.5f };

            foreach (var value in testValues)
            {
                var bf16 = BFloat16.FromFloat(value);
                var roundTrip = bf16.ToFloat();
                
                // BFloat16 has less precision, so we allow some tolerance
                // For these simple values, they should be exact or very close
                Assert.True(Math.Abs(value - roundTrip) < 0.01f, 
                    $"Round-trip failed for {value}: got {roundTrip}");
            }
        }

        [Fact]
        public void BFloat16_SpecialValues_Infinity()
        {
            var posInf = BFloat16.FromFloat(float.PositiveInfinity);
            var negInf = BFloat16.FromFloat(float.NegativeInfinity);

            Assert.True(posInf.IsPositiveInfinity);
            Assert.True(negInf.IsNegativeInfinity);
            Assert.True(posInf.IsInfinity);
            Assert.True(negInf.IsInfinity);
            Assert.False(posInf.IsFinite);
            Assert.False(negInf.IsFinite);
        }

        [Fact]
        public void BFloat16_SpecialValues_NaN()
        {
            var nan = BFloat16.FromFloat(float.NaN);
            Assert.True(nan.IsNaN);
            Assert.False(nan.IsFinite);
            Assert.False(nan.IsInfinity);
        }

        [Fact]
        public void BFloat16_Arithmetic_Addition()
        {
            var a = BFloat16.FromFloat(2.0f);
            var b = BFloat16.FromFloat(3.0f);
            var result = a + b;
            
            Assert.True(Math.Abs(5.0f - result.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_Arithmetic_Subtraction()
        {
            var a = BFloat16.FromFloat(5.0f);
            var b = BFloat16.FromFloat(3.0f);
            var result = a - b;
            
            Assert.True(Math.Abs(2.0f - result.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_Arithmetic_Multiplication()
        {
            var a = BFloat16.FromFloat(2.0f);
            var b = BFloat16.FromFloat(3.0f);
            var result = a * b;
            
            Assert.True(Math.Abs(6.0f - result.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_Arithmetic_Division()
        {
            var a = BFloat16.FromFloat(6.0f);
            var b = BFloat16.FromFloat(2.0f);
            var result = a / b;
            
            Assert.True(Math.Abs(3.0f - result.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_Comparison_Equality()
        {
            var a = BFloat16.FromFloat(1.0f);
            var b = BFloat16.FromFloat(1.0f);
            var c = BFloat16.FromFloat(2.0f);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
        }

        [Fact]
        public void BFloat16_Comparison_Ordering()
        {
            var a = BFloat16.FromFloat(1.0f);
            var b = BFloat16.FromFloat(2.0f);

            Assert.True(a < b);
            Assert.True(b > a);
            Assert.True(a <= b);
            Assert.True(b >= a);
            Assert.False(a > b);
            Assert.False(b < a);
        }

        [Fact]
        public void BFloat16_MathFunctions_Abs()
        {
            var positive = BFloat16.FromFloat(5.0f);
            var negative = BFloat16.FromFloat(-5.0f);

            var absPos = BFloat16.Abs(positive);
            var absNeg = BFloat16.Abs(negative);

            Assert.True(Math.Abs(5.0f - absPos.ToFloat()) < 0.01f);
            Assert.True(Math.Abs(5.0f - absNeg.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_MathFunctions_MinMax()
        {
            var a = BFloat16.FromFloat(3.0f);
            var b = BFloat16.FromFloat(7.0f);

            var min = BFloat16.Min(a, b);
            var max = BFloat16.Max(a, b);

            Assert.True(Math.Abs(3.0f - min.ToFloat()) < 0.01f);
            Assert.True(Math.Abs(7.0f - max.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_Properties_Sign()
        {
            var positive = BFloat16.FromFloat(5.0f);
            var negative = BFloat16.FromFloat(-5.0f);
            var zero = BFloat16.Zero;

            Assert.False(positive.IsNegative);
            Assert.True(negative.IsNegative);
            Assert.False(zero.IsNegative);
        }

        [Fact]
        public void BFloat16_ToString_Works()
        {
            var value = BFloat16.FromFloat(1.5f);
            var str = value.ToString();
            
            // Should be parseable as a float and close to 1.5
            Assert.True(float.TryParse(str, out float parsed));
            Assert.True(Math.Abs(1.5f - parsed) < 0.1f);
        }

        [Fact]
        public void BFloat16_Conversion_Implicit()
        {
            var bf16 = BFloat16.FromFloat(2.5f);
            
            // Implicit conversion to float
            float f = bf16;
            Assert.True(Math.Abs(2.5f - f) < 0.01f);
        }

        [Fact]
        public void BFloat16_Conversion_Explicit()
        {
            // Explicit conversion from float
            var bf16 = (BFloat16)2.5f;
            Assert.True(Math.Abs(2.5f - bf16.ToFloat()) < 0.01f);

            // Explicit conversion from double
            var bf16FromDouble = (BFloat16)2.5;
            Assert.True(Math.Abs(2.5f - bf16FromDouble.ToFloat()) < 0.01f);
        }

        [Fact]
        public void BFloat16_HashCode_Consistent()
        {
            var a = BFloat16.FromFloat(1.0f);
            var b = BFloat16.FromFloat(1.0f);
            var c = BFloat16.FromFloat(2.0f);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a.GetHashCode(), c.GetHashCode());
        }
    }
}