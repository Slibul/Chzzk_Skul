using System.Runtime.CompilerServices;

namespace System;

internal readonly struct Index : IEquatable<Index>
{
	private readonly int _value;

	public static Index Start => new Index(0);

	public static Index End => new Index(-1);

	public bool IsFromEnd => _value < 0;

	public int Value => IsFromEnd ? (~_value) : _value;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Index(int value, bool fromEnd = false)
	{
		if (value < 0)
		{
			throw new ArgumentOutOfRangeException("value");
		}
		_value = (fromEnd ? (~value) : value);
	}

	private Index(int value)
	{
		_value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator Index(int value)
	{
		return new Index(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetOffset(int length)
	{
		int num = _value;
		if (IsFromEnd)
		{
			num += length + 1;
		}
		return num;
	}

	public bool Equals(Index other)
	{
		return _value == other._value;
	}

	public override bool Equals(object value)
	{
		return value is Index index && _value == index._value;
	}

	public override int GetHashCode()
	{
		return _value;
	}

	public override string ToString()
	{
		return IsFromEnd ? $"^{Value}" : Value.ToString();
	}
}
