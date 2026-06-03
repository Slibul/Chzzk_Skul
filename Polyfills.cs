// net462 (.NET Framework 4.6.2) + LangVersion latest 호환성 폴리필
// C# 8~12의 최신 기능을 net462에서 사용하기 위한 타입 정의
//
// 포함:
//   - System.Index          : ^n 인덱스 연산자
//   - System.Range          : a..b 범위 연산자
//   - IsExternalInit        : init 전용 세터
//   - RequiresLocationAttribute : 일부 C#10+ 기능

#if !NET5_0_OR_GREATER

using System.Runtime.CompilerServices;

// ── System.Index ─────────────────────────────────────────────────
namespace System
{
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _value = fromEnd ? ~value : value;
        }

        private Index(int value) { _value = value; }

        public static Index Start => new Index(0);
        public static Index End   => new Index(~0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Index(int value) => new Index(value);

        public bool IsFromEnd => _value < 0;
        public int  Value     => IsFromEnd ? ~_value : _value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd) offset += length + 1;
            return offset;
        }

        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object value) => value is Index index && _value == index._value;
        public override int  GetHashCode() => _value;
        public override string ToString() => IsFromEnd ? $"^{Value}" : Value.ToString();
    }

    // ── System.Range ─────────────────────────────────────────────────
    internal readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End   { get; }

        public Range(Index start, Index end) { Start = start; End = end; }

        public static Range All                    => new Range(Index.Start, Index.End);
        public static Range StartAt(Index start)   => new Range(start, Index.End);
        public static Range EndAt(Index end)       => new Range(Index.Start, end);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end   = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object value) => value is Range r && r.Equals(this);
        public override int  GetHashCode() => Start.GetHashCode() ^ (End.GetHashCode() << 16);
        public override string ToString() => $"{Start}..{End}";
    }
}

// ── IsExternalInit (C# 9 init 전용 세터) ──────────────────────────
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

#endif
