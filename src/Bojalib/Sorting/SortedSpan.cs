using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>
/// Searches over sorted spans. The binary searches are branchless (the loop always runs ⌈log₂ n⌉ times and each
/// step is a conditional move), so they don't suffer branch mispredictions on random lookups.
/// </summary>
public static class SortedSpan
{
    /// <summary>Returns the index of the first element that is not less than <paramref name="value"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">A span sorted in ascending order.</param>
    /// <param name="value">The value to search for.</param>
    /// <returns>An index in [0, <c>span.Length</c>]: where <paramref name="value"/> would be inserted before any equal elements.</returns>
    public static int LowerBound<T>(ReadOnlySpan<T> span, T value) where T : IComparable<T>? =>
        Primitives.IsFloatingPoint<T>()
            ? LowerBoundCore(span, value, new ComparerLess<T, Comparer<T>>(Comparer<T>.Default))
            : LowerBoundCore(span, value, default(DefaultLess<T>));

    /// <summary>Returns the index of the first element that is not less than <paramref name="value"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">A span sorted by <paramref name="comparer"/>.</param>
    /// <param name="value">The value to search for.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <returns>An index in [0, <c>span.Length</c>].</returns>
    public static int LowerBound<T>(ReadOnlySpan<T> span, T value, IComparer<T>? comparer) =>
        LowerBoundCore(span, value, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default));

    /// <summary>Returns the index of the first element that is greater than <paramref name="value"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">A span sorted in ascending order.</param>
    /// <param name="value">The value to search for.</param>
    /// <returns>An index in [0, <c>span.Length</c>]: where <paramref name="value"/> would be inserted after any equal elements.</returns>
    public static int UpperBound<T>(ReadOnlySpan<T> span, T value) where T : IComparable<T>? =>
        Primitives.IsFloatingPoint<T>()
            ? UpperBoundCore(span, value, new ComparerLess<T, Comparer<T>>(Comparer<T>.Default))
            : UpperBoundCore(span, value, default(DefaultLess<T>));

    /// <summary>Returns the index of the first element that is greater than <paramref name="value"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">A span sorted by <paramref name="comparer"/>.</param>
    /// <param name="value">The value to search for.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <returns>An index in [0, <c>span.Length</c>].</returns>
    public static int UpperBound<T>(ReadOnlySpan<T> span, T value, IComparer<T>? comparer) =>
        UpperBoundCore(span, value, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default));

    /// <summary>Returns the range of elements equal to <paramref name="value"/> (empty if there are none).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">A span sorted in ascending order.</param>
    /// <param name="value">The value to search for.</param>
    /// <returns>The range; use <c>span[range]</c> to get the matching elements.</returns>
    public static Range EqualRange<T>(ReadOnlySpan<T> span, T value) where T : IComparable<T>? =>
        new(LowerBound(span, value), UpperBound(span, value));

    /// <summary>Returns the range of elements equal to <paramref name="value"/> (empty if there are none).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">A span sorted by <paramref name="comparer"/>.</param>
    /// <param name="value">The value to search for.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <returns>The range; use <c>span[range]</c> to get the matching elements.</returns>
    public static Range EqualRange<T>(ReadOnlySpan<T> span, T value, IComparer<T>? comparer) =>
        new(LowerBound(span, value, comparer), UpperBound(span, value, comparer));

    /// <summary>Returns whether <paramref name="span"/> is sorted in ascending order. SIMD for built-in numeric types.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <returns><see langword="true"/> if every element is &lt;= the next.</returns>
    public static bool IsSorted<T>(ReadOnlySpan<T> span) where T : IComparable<T>?
    {
        if (Primitives.IsFloatingPoint<T>())
        {
            return IsSortedCore(span, new ComparerLess<T, Comparer<T>>(Comparer<T>.Default));
        }

        if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported && span.Length > Vector256<T>.Count)
        {
            return Primitives.IsOrdered(span, descending: false);
        }

        return IsSortedCore(span, default(DefaultLess<T>));
    }

    /// <summary>Returns whether <paramref name="span"/> is sorted according to <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <returns><see langword="true"/> if no element is greater than the next.</returns>
    public static bool IsSorted<T>(ReadOnlySpan<T> span, IComparer<T>? comparer) =>
        IsSortedCore(span, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default));

    private static int LowerBoundCore<T, TLess>(ReadOnlySpan<T> span, T value, TLess less) where TLess : ILess<T>
    {
        int length = span.Length;
        if (length == 0)
        {
            return 0;
        }

        ref T r = ref MemoryMarshal.GetReference(span);
        int start = 0;
        while (length > 1)
        {
            int half = length >> 1;
            start = less.Less(Unsafe.Add(ref r, start + half), value) ? start + half : start;
            length -= half;
        }

        return start + (less.Less(Unsafe.Add(ref r, start), value) ? 1 : 0);
    }

    private static int UpperBoundCore<T, TLess>(ReadOnlySpan<T> span, T value, TLess less) where TLess : ILess<T>
    {
        int length = span.Length;
        if (length == 0)
        {
            return 0;
        }

        ref T r = ref MemoryMarshal.GetReference(span);
        int start = 0;
        while (length > 1)
        {
            int half = length >> 1;
            start = !less.Less(value, Unsafe.Add(ref r, start + half)) ? start + half : start;
            length -= half;
        }

        return start + (!less.Less(value, Unsafe.Add(ref r, start)) ? 1 : 0);
    }

    private static bool IsSortedCore<T, TLess>(ReadOnlySpan<T> span, TLess less) where TLess : ILess<T>
    {
        for (int i = 1; i < span.Length; i++)
        {
            if (less.Less(span[i], span[i - 1]))
            {
                return false;
            }
        }

        return true;
    }
}
