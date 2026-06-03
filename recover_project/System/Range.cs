using System.Runtime.CompilerServices;

namespace System;

internal readonly struct Range : IEquatable<Range>
{
	public Index Start { get; }

	public Index End { get; }

	public static Range All => Index.Start..Index.End;

	public Range(Index start, Index end)
	{
		Start = start;
		End = end;
	}

	public static Range StartAt(Index start)
	{
		return start..Index.End;
	}

	public static Range EndAt(Index end)
	{
		return Index.Start..end;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public (int Offset, int Length) GetOffsetAndLength(int length)
	{
		int offset = Start.GetOffset(length);
		int offset2 = End.GetOffset(length);
		if ((uint)offset2 > (uint)length || (uint)offset > (uint)offset2)
		{
			throw new ArgumentOutOfRangeException("length");
		}
		return (Offset: offset, Length: offset2 - offset);
	}

	public bool Equals(Range other)
	{
		return Start.Equals(other.Start) && End.Equals(other.End);
	}

	public override bool Equals(object value)
	{
		return value is Range range && range.Equals(this);
	}

	public override int GetHashCode()
	{
		return Start.GetHashCode() ^ (End.GetHashCode() << 16);
	}

	public override string ToString()
	{
		return $"{Start}..{End}";
	}
}
