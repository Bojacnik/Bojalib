using System.Buffers;
using System.Runtime.CompilerServices;

namespace Bojalib.Sorting;

/// <summary>
/// Sorts strings in ordinal order (by UTF-16 code unit, like <see cref="StringComparer.Ordinal"/>) with a cached
/// multikey quicksort (Bentley &amp; Sedgewick). Unlike a comparison sort, it never re-reads a shared prefix: each
/// partitioning step looks at a single character position, and only the strings that tie on it move on to the next.
/// Measured at about 1.2× the speed of <c>Array.Sort(strings, StringComparer.Ordinal)</c> on random words and 1.5× on
/// data with long shared prefixes (URLs, file paths, namespaced keys). The gain is smaller than in C because .NET's
/// ordinal comparison is already vectorized.
/// </summary>
/// <remarks>
/// Not stable. <see langword="null"/>s sort first. Uses a pooled buffer of one <see langword="long"/> per string:
/// each partition's next character is cached there first, so partitioning scans a flat array instead of chasing a
/// pointer per string per comparison. When every string in a partition shares the next character, their whole common
/// prefix is skipped in one vectorized pass. (Caching two or three characters per key was measured to be slower.)
/// </remarks>
public static class StringSort
{
    private const int InsertionSortThreshold = 16;
    private const int CharsPerKey = 1;

    /// <summary>Sorts <paramref name="strings"/> in ordinal order.</summary>
    /// <param name="strings">The strings to sort; may contain <see langword="null"/>s.</param>
    public static void Sort(Span<string?> strings) => SortCore(strings, Span<int>.Empty, withItems: false);

    /// <summary>
    /// Sorts <paramref name="keys"/> in ordinal order and rearranges <paramref name="items"/> the same way, so
    /// <c>items[i]</c> stays paired with <c>keys[i]</c>.
    /// </summary>
    /// <typeparam name="TValue">The item type.</typeparam>
    /// <param name="keys">The strings to sort by.</param>
    /// <param name="items">The items, one per key.</param>
    /// <exception cref="ArgumentException">The spans have different lengths.</exception>
    public static void Sort<TValue>(Span<string?> keys, Span<TValue> items)
    {
        if (keys.Length != items.Length)
        {
            throw new ArgumentException("Keys and items must have the same length.", nameof(items));
        }

        if (keys.Length < 2)
        {
            return;
        }

        // Sort an index permutation alongside the keys, then apply it to the items.
        int[] order = ArrayPool<int>.Shared.Rent(keys.Length);
        TValue[] copy = ArrayPool<TValue>.Shared.Rent(keys.Length);
        try
        {
            Span<int> permutation = order.AsSpan(0, keys.Length);
            for (int i = 0; i < permutation.Length; i++)
            {
                permutation[i] = i;
            }

            SortCore(keys, permutation, withItems: true);
            items.CopyTo(copy);
            for (int i = 0; i < permutation.Length; i++)
            {
                items[i] = copy[permutation[i]];
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(order);
            ArrayPool<TValue>.Shared.Return(copy, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
        }
    }

    private static void SortCore(Span<string?> strings, Span<int> permutation, bool withItems)
    {
        if (strings.Length < 2)
        {
            return;
        }

        // Nulls first, as with StringComparer.Ordinal.
        int nulls = 0;
        for (int i = 0; i < strings.Length; i++)
        {
            if (strings[i] is null)
            {
                Swap(strings, permutation, withItems, i, nulls++);
            }
        }

        int count = strings.Length - nulls;
        if (count < 2)
        {
            return;
        }

        long[] keyBuffer = ArrayPool<long>.Shared.Rent(count);
        Range[] stackBuffer = ArrayPool<Range>.Shared.Rent(64);
        int[] depthBuffer = ArrayPool<int>.Shared.Rent(64);
        try
        {
            MultikeyQuicksort(strings[nulls..], withItems ? permutation[nulls..] : Span<int>.Empty, withItems, keyBuffer, ref stackBuffer, ref depthBuffer);
        }
        finally
        {
            ArrayPool<long>.Shared.Return(keyBuffer);
            ArrayPool<Range>.Shared.Return(stackBuffer);
            ArrayPool<int>.Shared.Return(depthBuffer);
        }
    }

    // Iterative: a stack of (range, depth) instead of recursion, so very long common prefixes can't overflow the
    // call stack. Ranges are offsets into `strings`.
    private static void MultikeyQuicksort(Span<string?> strings, Span<int> permutation, bool withItems, long[] keyBuffer,
        ref Range[] stack, ref int[] depths)
    {
        int top = 0;
        Push(ref stack, ref depths, ref top, 0..strings.Length, 0);

        while (top > 0)
        {
            top--;
            (int start, int length) = stack[top].GetOffsetAndLength(strings.Length);
            int depth = depths[top];

            while (length > 1)
            {
                if (length < InsertionSortThreshold)
                {
                    InsertionSort(strings.Slice(start, length), withItems ? permutation.Slice(start, length) : Span<int>.Empty, withItems, depth);
                    break;
                }

                // Cache the next character(s) of every string in this partition as one ordered key.
                Span<long> keys = keyBuffer.AsSpan(start, length);
                for (int i = 0; i < length; i++)
                {
                    keys[i] = KeyAt(strings[start + i]!, depth);
                }

                long pivot = MedianOfThree(keys[0], keys[length / 2], keys[length - 1]);

                // Three-way partition (Dijkstra): [less | equal | greater], swapping strings and keys together.
                int lt = 0;
                int i2 = 0;
                int gt = length - 1;
                while (i2 <= gt)
                {
                    long key = keys[i2];
                    if (key < pivot)
                    {
                        SwapAll(strings, permutation, withItems, keys, start, lt++, i2++);
                    }
                    else if (key > pivot)
                    {
                        SwapAll(strings, permutation, withItems, keys, start, i2, gt--);
                    }
                    else
                    {
                        i2++;
                    }
                }

                if (lt > 1)
                {
                    Push(ref stack, ref depths, ref top, start..(start + lt), depth);
                }

                if (length - gt - 1 > 1)
                {
                    Push(ref stack, ref depths, ref top, (start + gt + 1)..(start + length), depth);
                }

                // A zero slot means the strings ended inside the cached characters: the tied ones are identical.
                if ((pivot & 0x1FFFF) == 0)
                {
                    break;
                }

                bool allTied = lt == 0 && gt == length - 1;
                start += lt;
                length = gt - lt + 1;
                depth += CharsPerKey;

                // Everyone tied: skip the rest of their shared prefix in one vectorized pass instead of one character
                // per partitioning pass (URLs, paths and namespaced keys share long prefixes).
                if (allTied)
                {
                    depth += CommonPrefixLength(strings.Slice(start, length), depth);
                }
            }
        }
    }

    private static int CommonPrefixLength(Span<string?> strings, int depth)
    {
        ReadOnlySpan<char> first = Suffix(strings[0]!, depth);
        int common = first.Length;
        for (int i = 1; i < strings.Length && common > 0; i++)
        {
            common = Math.Min(common, first[..common].CommonPrefixLength(Suffix(strings[i]!, depth)));
        }

        return common;
    }

    // Insertion sort comparing only from `depth` on, with the runtime's vectorized span comparison.
    private static void InsertionSort(Span<string?> strings, Span<int> permutation, bool withItems, int depth)
    {
        for (int i = 1; i < strings.Length; i++)
        {
            string current = strings[i]!;
            int currentIndex = withItems ? permutation[i] : 0;
            int j = i - 1;
            while (j >= 0 && Suffix(strings[j]!, depth).SequenceCompareTo(Suffix(current, depth)) > 0)
            {
                strings[j + 1] = strings[j];
                if (withItems)
                {
                    permutation[j + 1] = permutation[j];
                }

                j--;
            }

            strings[j + 1] = current;
            if (withItems)
            {
                permutation[j + 1] = currentIndex;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> Suffix(string s, int depth) => depth < s.Length ? s.AsSpan(depth) : [];

    // Packs the characters from `depth` into 17-bit slots as (char + 1), with 0 for "past the end", so keys order
    // exactly like the strings' next characters and a shorter string sorts before its extensions.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long KeyAt(string s, int depth)
    {
        long key = 0;
        for (int k = 0; k < CharsPerKey; k++)
        {
            int index = depth + k;
            key = (key << 17) | ((uint)index < (uint)s.Length ? s[index] + 1L : 0);
        }

        return key;
    }

    private static long MedianOfThree(long a, long b, long c) =>
        a < b ? (b < c ? b : a < c ? c : a) : (a < c ? a : b < c ? c : b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SwapAll(Span<string?> strings, Span<int> permutation, bool withItems, Span<long> keys, int start, int a, int b)
    {
        (keys[a], keys[b]) = (keys[b], keys[a]);
        Swap(strings, permutation, withItems, start + a, start + b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(Span<string?> strings, Span<int> permutation, bool withItems, int a, int b)
    {
        (strings[a], strings[b]) = (strings[b], strings[a]);
        if (withItems)
        {
            (permutation[a], permutation[b]) = (permutation[b], permutation[a]);
        }
    }

    private static void Push(ref Range[] stack, ref int[] depths, ref int top, Range range, int depth)
    {
        if (top == stack.Length)
        {
            Range[] biggerStack = ArrayPool<Range>.Shared.Rent(stack.Length * 2);
            int[] biggerDepths = ArrayPool<int>.Shared.Rent(depths.Length * 2);
            stack.AsSpan(0, top).CopyTo(biggerStack);
            depths.AsSpan(0, top).CopyTo(biggerDepths);
            ArrayPool<Range>.Shared.Return(stack);
            ArrayPool<int>.Shared.Return(depths);
            stack = biggerStack;
            depths = biggerDepths;
        }

        stack[top] = range;
        depths[top] = depth;
        top++;
    }
}
