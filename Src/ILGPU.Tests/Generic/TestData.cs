﻿// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2021-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: TestData.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable CA1051 // Do not declare visible instance fields
#pragma warning disable CA2231 // Overload operator equals on overriding value type Equals

// Uses annotated structures supporting the IXunitSerializable interface
// More information can be found here: https://github.com/xunit/xunit/issues/429

namespace ILGPU.Tests
{
    /// <summary>
    /// Implements a test data helper.
    /// </summary>
    public static class TestData
    {
        /// <summary>
        /// Creates a new serializable test data instance.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="data">The data element.</param>
        /// <returns>The wrapped test data instance.</returns>
        public static TestData<T> Create<T>(T data)
        {
            return new TestData<T>(data);
        }

    }

    /// <summary>
    /// Wraps a test value.
    /// </summary>
    /// <typeparam name="T">The value to wrap.</typeparam>
    public class TestData<T> : IXunitSerializable
    {
        public TestData()
        {
            Value = Utilities.InitNotNullable<T>();
        }

        public TestData(T value)
        {
            Value = value;
        }

        public T Value { get; private set; }

        public void Deserialize(IXunitSerializationInfo info)
        {
            Value = info.GetValue<T>(nameof(Value));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Value), Value);
        }

        public override string ToString()
        {
            return $"{Value}";
        }

    }

    #region Data Structures

    /// <summary>
    /// An abstract value structure that contains a nested property of type
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public interface IValueStructure<T>
    {
        /// <summary>
        /// The nested element value.
        /// </summary>
        T NestedValue { get; set; }
    }

    [Serializable]
    public struct EmptyStruct : IXunitSerializable, IEquatable<EmptyStruct>
    {
        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }

        public bool Equals(EmptyStruct other)
        {
            return true;
        }


        public override bool Equals(object? obj)
        {
            return obj is EmptyStruct other && Equals(other);
        }


        public override int GetHashCode()
        {
            return 0;
        }

    }

    [Serializable]
    // warning disabled intentionally for testing this scenario
#pragma warning disable CS0659 // Type does not override Object.GetHashCode()
    public struct NoHashCodeStruct : IXunitSerializable, IEquatable<NoHashCodeStruct>
#pragma warning restore CS0659 // Type does not override Object.GetHashCode()
    {
        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }

        public bool Equals(NoHashCodeStruct other)
        {
            return true;
        }


        public override bool Equals(object? obj)
        {
            return obj is NoHashCodeStruct other && Equals(other);
        }

    }

    public static class PairStruct
    {
        public static PairStruct<float, float> MaxFloats =>
            new PairStruct<float, float>(float.MaxValue, float.MaxValue);

        public static PairStruct<double, double> MaxDoubles =>
            new PairStruct<double, double>(double.MaxValue, double.MaxValue);
    }

    [Serializable]
    public struct PairStruct<T1, T2> : IXunitSerializable
        where T1 : struct
        where T2 : struct
    {
        public PairStruct(T1 val0, T2 val1)
        {
            Val0 = val0;
            Val1 = val1;
        }

        public T1 Val0;
        public T2 Val1;

        public void Deserialize(IXunitSerializationInfo info)
        {
            Val0 = info.GetValue<T1>(nameof(Val0));
            Val1 = info.GetValue<T2>(nameof(Val1));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Val0), Val0);
            info.AddValue(nameof(Val1), Val1);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Val0, Val1);
        }


        public override string ToString()
        {
            return $"{Val0}, {Val1}";
        }

    }

    [Serializable]
    public struct TestStruct : IXunitSerializable, IEquatable<TestStruct>
    {
        public int X;
        public long Y;
        public short Z;
        public int W;

        public void Deserialize(IXunitSerializationInfo info)
        {
            X = info.GetValue<int>(nameof(X));
            Y = info.GetValue<long>(nameof(Y));
            Z = info.GetValue<short>(nameof(Z));
            W = info.GetValue<int>(nameof(W));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Z), Z);
            info.AddValue(nameof(W), W);
        }

        public bool Equals(TestStruct other)
        {
            return X == other.X &&
            Y == other.Y &&
            Z == other.Z &&
            W == other.W;
        }


        public override bool Equals(object? obj)
        {
            return obj is TestStruct other && Equals(other);
        }


        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, W);
        }

    }

    [Serializable]
    public struct TestStruct<T> : IXunitSerializable, IValueStructure<T>
        where T : struct
    {
        public byte Val0;
        public T Val1;
        public short Val2;

        T IValueStructure<T>.NestedValue
        {
            get => Val1;
            set => Val1 = value;
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            Val0 = info.GetValue<byte>(nameof(Val0));
            Val1 = info.GetValue<T>(nameof(Val1));
            Val2 = info.GetValue<short>(nameof(Val2));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Val0), Val0);
            info.AddValue(nameof(Val1), Val1);
            info.AddValue(nameof(Val2), Val2);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Val0, Val1, Val2);
        }


        public override string ToString()
        {
            return $"{Val0}, {Val1}, {Val2}";
        }

    }

    [Serializable]
    public struct TestStructEquatable<T> :
        IXunitSerializable,
        IValueStructure<T>,
        IEquatable<TestStructEquatable<T>>
        where T : struct, IEquatable<T>
    {
        private TestStruct<T> data;

        public byte Val0
        {
            get => data.Val0;
            set => data.Val0 = value;
        }

        public T Val1
        {
            get => data.Val1;
            set => data.Val1 = value;
        }

        public short Val2
        {
            get => data.Val2;
            set => data.Val2 = value;
        }

        T IValueStructure<T>.NestedValue
        {
            get => data.Val1;
            set => data.Val1 = value;
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            data.Deserialize(info);
        }


        public void Serialize(IXunitSerializationInfo info)
        {
            data.Serialize(info);
        }


        public bool Equals(TestStructEquatable<T> other)
        {
            return Val0 == other.Val0 &&
            Val2 == other.Val2 &&
            Val1.Equals(other.Val1);
        }


        public override bool Equals(object? obj)
        {
            return obj is TestStructEquatable<T> other && Equals(other);
        }


        public override int GetHashCode()
        {
            return HashCode.Combine(Val0, Val1, Val2);
        }


        public override string ToString()
        {
            return data.ToString();
        }

    }

    [Serializable]
    public struct TestStruct<T1, T2> : IXunitSerializable, IValueStructure<T2>
        where T1 : struct
        where T2 : struct
    {
        public T1 Val0;
        public ushort Val1;
        public T2 Val2;

        T2 IValueStructure<T2>.NestedValue
        {
            get => Val2;
            set => Val2 = value;
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            Val0 = info.GetValue<T1>(nameof(Val0));
            Val1 = info.GetValue<ushort>(nameof(Val1));
            Val2 = info.GetValue<T2>(nameof(Val2));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Val0), Val0);
            info.AddValue(nameof(Val1), Val1);
            info.AddValue(nameof(Val2), Val2);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Val0, Val1, Val2);
        }


        public override string ToString()
        {
            return $"{Val0}, {Val1}, {Val2}";
        }

    }

    [Serializable]
    public struct TestStructEquatable<T1, T2> :
        IXunitSerializable,
        IValueStructure<T2>,
        IEquatable<TestStructEquatable<T1, T2>>
        where T1 : struct, IEquatable<T1>
        where T2 : struct, IEquatable<T2>
    {
        private TestStruct<T1, T2> data;

        public T1 Val0
        {
            get => data.Val0;
            set => data.Val0 = value;
        }

        public ushort Val1
        {
            get => data.Val1;
            set => data.Val1 = value;
        }

        public T2 Val2
        {
            get => data.Val2;
            set => data.Val2 = value;
        }

        T2 IValueStructure<T2>.NestedValue
        {
            get => data.Val2;
            set => data.Val2 = value;
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            data.Deserialize(info);
        }


        public void Serialize(IXunitSerializationInfo info)
        {
            data.Serialize(info);
        }


        public bool Equals(TestStructEquatable<T1, T2> other)
        {
            return Val1 == other.Val1 &&
            Val0.Equals(other.Val0) &&
            Val2.Equals(other.Val2);
        }


        public override bool Equals(object? obj)
        {
            return obj is TestStructEquatable<T1, T2> other && Equals(other);
        }


        public override int GetHashCode()
        {
            return data.GetHashCode();
        }


        public override string ToString()
        {
            return data.ToString();
        }

    }

    public struct DeepStructure<T> : IXunitSerializable
        where T : struct
    {
        public TestStruct<
            T,
            TestStruct<
                long,
                TestStruct<
                    float,
                    TestStruct<
                        T,
                        T>>>> Value;

        public DeepStructure(T val0, T val1, T val2)
        {
            Value = new TestStruct<
                T,
                TestStruct<
                    long,
                    TestStruct<
                        float,
                        TestStruct<
                            T,
                            T>>>>()
            {
                Val0 = val0,
                Val2 = new TestStruct<
                    long,
                    TestStruct<float, TestStruct<T, T>>>()
                {
                    Val2 = new TestStruct<
                        float,
                        TestStruct<T, T>>()
                    {

                        Val2 = new TestStruct<T, T>()
                        {
                            Val0 = val1,
                            Val2 = val2,
                        }
                    }
                }
            };
        }

        public T Val0 => Value.Val0;

        public T Val1 => Value.Val2.Val2.Val2.Val0;

        public T Val2 => Value.Val2.Val2.Val2.Val2;

        public void Deserialize(IXunitSerializationInfo info)
        {
            Value.Deserialize(info);
        }


        public void Serialize(IXunitSerializationInfo info)
        {
            Value.Serialize(info);
        }

    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct SmallCustomSizeStruct : IXunitSerializable
    {
        public byte Data;

        public void Deserialize(IXunitSerializationInfo info)
        {
            Data = info.GetValue<byte>(nameof(Data));
        }


        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Data), Data);
        }

    }

    [StructLayout(LayoutKind.Sequential, Size = 512)]
    public struct CustomSizeStruct : IXunitSerializable
    {
        public byte Data;

        public void Deserialize(IXunitSerializationInfo info)
        {
            Data = info.GetValue<byte>(nameof(Data));
        }


        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Data), Data);
        }

    }

    public unsafe struct ShortFixedBufferStruct : IXunitSerializable
    {
        public const int Length = 7;

        public fixed short Data[Length];

        public ShortFixedBufferStruct(short data)
        {
            for (int i = 0; i < Length; ++i)
                Data[i] = data;
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            for (int i = 0; i < Length; ++i)
                Data[i] = info.GetValue<short>(nameof(Data) + i);
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            for (int i = 0; i < Length; ++i)
                info.AddValue(nameof(Data) + i, Data[i]);
        }

        public override string ToString()
        {
            string result = string.Empty;
            for (int i = 0; i < Length; ++i)
            {
                result += Data[i];
                if (i + 1 < Length)
                    result += ", ";
            }
            return result;
        }
    }

    public unsafe struct LongFixedBufferStruct : IXunitSerializable
    {
        public const int Length = 19;

        public fixed long Data[Length];

        public LongFixedBufferStruct(long data)
        {
            for (int i = 0; i < Length; ++i)
                Data[i] = data;
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            for (int i = 0; i < Length; ++i)
                Data[i] = info.GetValue<long>(nameof(Data) + i);
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            for (int i = 0; i < Length; ++i)
                info.AddValue(nameof(Data) + i, Data[i]);
        }

        public override string ToString()
        {
            string result = string.Empty;
            for (int i = 0; i < Length; ++i)
            {
                result += Data[i];
                if (i + 1 < Length)
                    result += ", ";
            }
            return result;
        }
    }

    #endregion

    #region Length Structures

    /// <summary>
    /// An abstraction to inline a specialized sizes.
    /// </summary>
    public interface ILength : IXunitSerializable
    {
        int Length { get; }
    }

    /// <summary>
    /// Array size of 0.
    /// </summary>
    public struct Length0 : ILength
    {
        public int Length => 0;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 1.
    /// </summary>
    public struct Length1 : ILength
    {
        public int Length => 1;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 2.
    /// </summary>
    public struct Length2 : ILength
    {
        public int Length => 2;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 31.
    /// </summary>
    public struct Length31 : ILength
    {
        public int Length => 31;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 32.
    /// </summary>
    public struct Length32 : ILength
    {
        public int Length => 32;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 33.
    /// </summary>
    public struct Length33 : ILength
    {
        public int Length => 33;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 65.
    /// </summary>
    public struct Length65 : ILength
    {
        public int Length => 65;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    /// <summary>
    /// Array size of 127.
    /// </summary>
    public struct Length127 : ILength
    {
        public int Length => 127;

        public void Deserialize(IXunitSerializationInfo info) { }

        public void Serialize(IXunitSerializationInfo info) { }
    }

    #endregion
}

#pragma warning restore CA2231 // Overload operator equals on overriding value type Equals
#pragma warning restore CA1051 // Do not declare visible instance fields
#pragma warning restore CA1815 // Override equals and operator equals on value types
