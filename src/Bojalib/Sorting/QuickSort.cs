using System.Buffers;
using System.Runtime.CompilerServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>
/// Fast in-place unstable sort: pattern-defeating quicksort with SIMD partitioning for built-in numeric types.
/// O(n log n) worst case, O(n) for already sorted or reversed input, no allocations.
/// </summary>
/// <remarks>
/// With the default ordering, 8-bit integers (and large 16-bit inputs) are counting-sorted instead, and
/// <see langword="null"/>s and NaNs sort first, matching <see cref="Comparer{T}.Default"/>.
/// The relative order of equal elements is not preserved; use <see cref="MergeSort"/> when it must be.
/// </remarks>
public static class QuickSort
{
    /// <summary>Sorts <paramref name="span"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    public static void Sort<T>(Span<T> span) where T : IComparable<T>? => SortDefault(span);

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public static void Sort<T>(Span<T> span, IComparer<T>? comparer)
    {
        if (comparer is null || ReferenceEquals(comparer, Comparer<T>.Default))
        {
            SortDefault(span);
        }
        else
        {
            PdqSort.Sort(span, new ComparerLess<T, IComparer<T>>(comparer));
        }
    }

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparison"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparison">The comparison.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public static void Sort<T>(Span<T> span, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        PdqSort.Sort(span, new ComparisonLess<T>(comparison));
    }

    /// <summary>
    /// Sorts <paramref name="span"/> using a comparer whose type is known at compile time. A <see langword="struct"/>
    /// comparer is fully inlined into the sort loop.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TComparer">The comparer type; ideally a <see langword="struct"/>.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparer">The comparer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparer"/> is <see langword="null"/>.</exception>
    public static void Sort<T, TComparer>(Span<T> span, TComparer comparer) where TComparer : IComparer<T>
    {
        if (comparer is null)
        {
            throw new ArgumentNullException(nameof(comparer));
        }

        PdqSort.Sort(span, new ComparerLess<T, TComparer>(comparer));
    }

    /// <summary>
    /// Sorts <paramref name="keys"/> and rearranges <paramref name="items"/> the same way, so
    /// <c>items[i]</c> stays paired with <c>keys[i]</c>. Faster than sorting items with a key-selecting comparison.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The item type.</typeparam>
    /// <param name="keys">The keys to sort by.</param>
    /// <param name="items">The items, one per key.</param>
    /// <param name="comparer">Compares keys; <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <exception cref="ArgumentException">The spans have different lengths.</exception>
    public static void Sort<TKey, TValue>(Span<TKey> keys, Span<TValue> items, IComparer<TKey>? comparer = null)
    {
        if (keys.Length != items.Length)
        {
            throw new ArgumentException("Keys and items must have the same length.", nameof(items));
        }

        if (keys.Length < 2)
        {
            return;
        }

        KeyValuePair<TKey, TValue>[] pairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(keys.Length);
        try
        {
            Span<KeyValuePair<TKey, TValue>> span = pairs.AsSpan(0, keys.Length);
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = new(keys[i], items[i]);
            }

            if (comparer is null && !Primitives.IsFloatingPoint<TKey>())
            {
                PdqSort.Sort(span, new KeyLess<TKey, TValue, DefaultLess<TKey>>(default));
            }
            else
            {
                var less = new ComparerLess<TKey, IComparer<TKey>>(comparer ?? Comparer<TKey>.Default);
                PdqSort.Sort(span, new KeyLess<TKey, TValue, ComparerLess<TKey, IComparer<TKey>>>(less));
            }

            for (int i = 0; i < span.Length; i++)
            {
                keys[i] = span[i].Key;
                items[i] = span[i].Value;
            }
        }
        finally
        {
            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(
                pairs, RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<TKey, TValue>>());
        }
    }

    internal static void SortDefault<T>(Span<T> span)
    {
        if (span.Length < 2 || Primitives.TryCountingSort(span))
        {
            return;
        }

        if (Primitives.IsFloatingPoint<T>())
        {
            span = span[Primitives.MoveNaNsToFront(span)..];
        }

        if (Primitives.TryHandlePresorted(span))
        {
            return;
        }

        PdqSort.Sort(span, default(DefaultLess<T>));
    }
}
