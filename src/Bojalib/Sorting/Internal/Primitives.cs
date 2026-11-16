using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Bojalib.Sorting.Internal;

/// <summary>Fast paths for built-in numeric types under the default ordering.</summary>
internal static class Primitives
{
    public static bool IsFloatingPoint<T>() => typeof(T) == typeof(float) || typeof(T) == typeof(double);

    /// <summary>
    /// Moves NaNs to the front, where <see cref="Comparer{T}.Default"/> orders them, so the rest can be compared
    /// with a plain <c>&lt;</c>. Order among the other elements is not preserved.
    /// </summary>
    /// <returns>The number of NaNs.</returns>
    public static int MoveNaNsToFront<T>(Span<T> span)
    {
        ref T start = ref MemoryMarshal.GetReference(span);
        int nanCount = 0;
        for (int i = 0; i < span.Length; i++)
        {
            ref T value = ref Unsafe.Add(ref start, i);
            if (IsNaN(value))
            {
                (Unsafe.Add(ref start, nanCount), value) = (value, Unsafe.Add(ref start, nanCount));
                nanCount++;
            }
        }

        return nanCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNaN<T>(T value)
    {
        if (typeof(T) == typeof(float)) return float.IsNaN(Unsafe.As<T, float>(ref value));
        if (typeof(T) == typeof(double)) return double.IsNaN(Unsafe.As<T, double>(ref value));
        return false;
    }

    /// <summary>
    /// SIMD check of whether <paramref name="span"/> is ascending or descending. Ascending input is left alone and
    /// descending input is reversed (also SIMD), so both cost one linear pass instead of a full sort.
    /// Random input is rejected within the first vector or two.
    /// </summary>
    /// <returns><see langword="true"/> if the span is now sorted.</returns>
    public static bool TryHandlePresorted<T>(Span<T> span)
    {
        if (!Vector256.IsHardwareAccelerated || !Vector256<T>.IsSupported || span.Length <= Vector256<T>.Count)
        {
            return false;
        }

        if (IsOrdered(span, descending: false))
        {
            return true;
        }

        if (IsOrdered(span, descending: true))
        {
            span.Reverse();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether every element is &lt;= its successor (or &gt;= when <paramref name="descending"/>). Compares each
    /// vector with the same vector shifted by one element. Requires a SIMD-supported <typeparamref name="T"/>
    /// without NaNs and more than one vector of elements.
    /// </summary>
    public static bool IsOrdered<T>(ReadOnlySpan<T> span, bool descending)
    {
        int lanes = Vector256<T>.Count;
        ref T start = ref MemoryMarshal.GetReference(span);
        nuint lastStart = (nuint)(span.Length - lanes - 1);

        for (nuint i = 0; ; i += (nuint)lanes)
        {
            if (i > lastStart)
            {
                // Final, possibly overlapping, vector.
                i = lastStart;
            }

            Vector256<T> current = Vector256.LoadUnsafe(ref start, i);
            Vector256<T> next = Vector256.LoadUnsafe(ref start, i + 1);
            bool violation = descending ? Vector256.LessThanAny(current, next) : Vector256.GreaterThanAny(current, next);
            if (violation)
            {
                return false;
            }

            if (i == lastStart)
            {
                return true;
            }
        }
    }

    /// <summary>Counting sort for 8- and 16-bit integers: O(n + 2^bits), no comparisons at all.</summary>
    /// <returns><see langword="true"/> if <typeparamref name="T"/> was handled.</returns>
    public static bool TryCountingSort<T>(Span<T> span)
    {
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte))
        {
            CountingSortBytes(span, signed: typeof(T) == typeof(sbyte));
            return true;
        }

        // A 64K-entry histogram only pays off for large inputs.
        if (span.Length >= 1 << 14 && (typeof(T) == typeof(ushort) || typeof(T) == typeof(short) || typeof(T) == typeof(char)))
        {
            CountingSort16(span, signed: typeof(T) == typeof(short));
            return true;
        }

        return false;
    }

    private static void CountingSortBytes<T>(Span<T> span, bool signed)
    {
        Span<byte> bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)), span.Length);
        Span<int> counts = stackalloc int[256];
        counts.Clear();
        foreach (byte b in bytes)
        {
            counts[b]++;
        }

        // Signed bytes: 0x80..0xFF (negative) come before 0x00..0x7F.
        int position = 0;
        for (int i = 0; i < 256; i++)
        {
            int value = signed ? (i + 128) & 0xFF : i;
            bytes.Slice(position, counts[value]).Fill((byte)value);
            position += counts[value];
        }
    }

    private static void CountingSort16<T>(Span<T> span, bool signed)
    {
        Span<ushort> values = MemoryMarshal.CreateSpan(ref Unsafe.As<T, ushort>(ref MemoryMarshal.GetReference(span)), span.Length);
        int[] counts = new int[65536];
        foreach (ushort v in values)
        {
            counts[v]++;
        }

        int position = 0;
        for (int i = 0; i < 65536; i++)
        {
            int value = signed ? (i + 32768) & 0xFFFF : i;
            values.Slice(position, counts[value]).Fill((ushort)value);
            position += counts[value];
        }
    }
}
