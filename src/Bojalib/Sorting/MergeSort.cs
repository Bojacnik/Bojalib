using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>
/// Stable, adaptive merge sort in the style of TimSort: finds runs that are already in order (reversing descending
/// ones), extends short runs with binary insertion sort, and merges them with a balanced-run stack.
/// O(n log n) worst case, O(n) for sorted, reversed or few-run input; uses a pooled buffer of up to n/2 elements.
/// </summary>
/// <remarks>
/// Equal elements keep their original relative order. <see langword="null"/>s and NaNs sort first, matching
/// <see cref="Comparer{T}.Default"/>.
/// </remarks>
public static class MergeSort
{
    private const int MinMerge = 32;

    /// <summary>Sorts <paramref name="span"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    public static void Sort<T>(Span<T> span) where T : IComparable<T>?
    {
        if (Primitives.IsFloatingPoint<T>())
        {
            Core(span, new ComparerLess<T, Comparer<T>>(Comparer<T>.Default));
        }
        else
        {
            Core(span, default(DefaultLess<T>));
        }
    }

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public static void Sort<T>(Span<T> span, IComparer<T>? comparer) =>
        Core(span, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default));

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparison"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparison">The comparison.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public static void Sort<T>(Span<T> span, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        Core(span, new ComparisonLess<T>(comparison));
    }

    private static void Core<T, TLess>(Span<T> span, TLess less) where TLess : ILess<T>
    {
        int n = span.Length;
        if (n < 2)
        {
            return;
        }

        ref T r = ref MemoryMarshal.GetReference(span);
        if (n < MinMerge)
        {
            int initialRun = CountRunAndMakeAscending(ref r, 0, n, less);
            BinaryInsertionSort(ref r, 0, n, initialRun, less);
            return;
        }

        T[] buffer = ArrayPool<T>.Shared.Rent((n / 2) + 1);
        try
        {
            var state = new MergeState<T, TLess>(buffer, less);
            int minRun = MinRunLength(n);
            int start = 0;
            while (start < n)
            {
                int runLength = CountRunAndMakeAscending(ref r, start, n, less);
                if (runLength < minRun)
                {
                    int forced = Math.Min(minRun, n - start);
                    BinaryInsertionSort(ref r, start, start + forced, start + runLength, less);
                    runLength = forced;
                }

                state.PushRun(start, runLength);
                state.MergeCollapse(ref r);
                start += runLength;
            }

            state.MergeForceCollapse(ref r);
        }
        finally
        {
            ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    // Picks a run length in [16, 32] so that n / minRun is close to, but not above, a power of two: that keeps
    // the final merges balanced.
    private static int MinRunLength(int n)
    {
        int extra = 0;
        while (n >= MinMerge)
        {
            extra |= n & 1;
            n >>= 1;
        }

        return n + extra;
    }

    /// <summary>Length of the run starting at <paramref name="start"/>; a strictly descending run is reversed.</summary>
    private static int CountRunAndMakeAscending<T, TLess>(ref T r, int start, int end, TLess less) where TLess : ILess<T>
    {
        int runEnd = start + 1;
        if (runEnd == end)
        {
            return 1;
        }

        // Strictly descending only: reversing a run with equal elements would break stability.
        if (less.Less(Unsafe.Add(ref r, runEnd++), Unsafe.Add(ref r, start)))
        {
            while (runEnd < end && less.Less(Unsafe.Add(ref r, runEnd), Unsafe.Add(ref r, runEnd - 1)))
            {
                runEnd++;
            }

            MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, start), runEnd - start).Reverse();
        }
        else
        {
            while (runEnd < end && !less.Less(Unsafe.Add(ref r, runEnd), Unsafe.Add(ref r, runEnd - 1)))
            {
                runEnd++;
            }
        }

        return runEnd - start;
    }

    /// <summary>Sorts [start, end) given that [start, sortedEnd) is already sorted.</summary>
    private static void BinaryInsertionSort<T, TLess>(ref T r, int start, int end, int sortedEnd, TLess less)
        where TLess : ILess<T>
    {
        for (int i = sortedEnd; i < end; i++)
        {
            T pivot = Unsafe.Add(ref r, i);

            // Upper bound: insert after equal elements, which keeps the sort stable.
            int low = start;
            int high = i;
            while (low < high)
            {
                int middle = (int)((uint)(low + high) >> 1);
                if (less.Less(pivot, Unsafe.Add(ref r, middle)))
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            int moveCount = i - low;
            if (moveCount > 0)
            {
                MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, low), moveCount)
                    .CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, low + 1), moveCount));
                Unsafe.Add(ref r, low) = pivot;
            }
        }
    }

    private struct MergeState<T, TLess>(T[] buffer, TLess less) where TLess : ILess<T>
    {
        // With the merge invariants below, run lengths grow at least like Fibonacci numbers, so 85 runs cover
        // any int-sized input.
        private RunStack _runs;
        private int _count;

        public void PushRun(int start, int length)
        {
            _runs[_count] = (start, length);
            _count++;
        }

        /// <summary>
        /// Merges until the run-length invariants hold (len[i-2] &gt; len[i-1] + len[i] and len[i-1] &gt; len[i]),
        /// including the extra check that fixes the original TimSort invariant bug (de Gouw et al., 2015).
        /// </summary>
        public void MergeCollapse(ref T r)
        {
            while (_count > 1)
            {
                int n = _count - 2;
                if ((n > 0 && Len(n - 1) <= Len(n) + Len(n + 1)) || (n > 1 && Len(n - 2) <= Len(n - 1) + Len(n)))
                {
                    if (Len(n - 1) < Len(n + 1))
                    {
                        n--;
                    }
                }
                else if (Len(n) > Len(n + 1))
                {
                    break;
                }

                MergeAt(ref r, n);
            }
        }

        public void MergeForceCollapse(ref T r)
        {
            while (_count > 1)
            {
                int n = _count - 2;
                if (n > 0 && Len(n - 1) < Len(n + 1))
                {
                    n--;
                }

                MergeAt(ref r, n);
            }
        }

        private readonly int Len(int i) => _runs[i].Length;

        private void MergeAt(ref T r, int i)
        {
            (int startA, int lengthA) = _runs[i];
            (int startB, int lengthB) = _runs[i + 1];

            _runs[i] = (startA, lengthA + lengthB);
            if (i == _count - 3)
            {
                _runs[i + 1] = _runs[i + 2];
            }

            _count--;

            // Elements of A that are <= B's first element are already in place.
            int skip = UpperBound(ref Unsafe.Add(ref r, startA), lengthA, Unsafe.Add(ref r, startB));
            startA += skip;
            lengthA -= skip;
            if (lengthA == 0)
            {
                return;
            }

            // Elements of B that are >= A's last element are already in place.
            lengthB = LowerBound(ref Unsafe.Add(ref r, startB), lengthB, Unsafe.Add(ref r, startA + lengthA - 1));
            if (lengthB == 0)
            {
                return;
            }

            if (lengthA <= lengthB)
            {
                MergeLow(ref r, startA, lengthA, startB, lengthB);
            }
            else
            {
                MergeHigh(ref r, startA, lengthA, startB, lengthB);
            }
        }

        // A is the shorter run: copy it out and merge front to back.
        private readonly void MergeLow(ref T r, int startA, int lengthA, int startB, int lengthB)
        {
            ref T temp = ref MemoryMarshal.GetArrayDataReference(buffer);
            MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, startA), lengthA).CopyTo(buffer);

            int a = 0;
            int b = startB;
            int endB = startB + lengthB;
            int dest = startA;
            while (a < lengthA && b < endB)
            {
                // Take from A on ties: stable.
                if (less.Less(Unsafe.Add(ref r, b), Unsafe.Add(ref temp, a)))
                {
                    Unsafe.Add(ref r, dest++) = Unsafe.Add(ref r, b++);
                }
                else
                {
                    Unsafe.Add(ref r, dest++) = Unsafe.Add(ref temp, a++);
                }
            }

            if (a < lengthA)
            {
                MemoryMarshal.CreateSpan(ref Unsafe.Add(ref temp, a), lengthA - a)
                    .CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, dest), lengthA - a));
            }
        }

        // B is the shorter run: copy it out and merge back to front.
        private readonly void MergeHigh(ref T r, int startA, int lengthA, int startB, int lengthB)
        {
            ref T temp = ref MemoryMarshal.GetArrayDataReference(buffer);
            MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, startB), lengthB).CopyTo(buffer);

            int a = startA + lengthA - 1;
            int b = lengthB - 1;
            int dest = startB + lengthB - 1;
            while (a >= startA && b >= 0)
            {
                // Take from B on ties: stable when filling from the back.
                if (less.Less(Unsafe.Add(ref temp, b), Unsafe.Add(ref r, a)))
                {
                    Unsafe.Add(ref r, dest--) = Unsafe.Add(ref r, a--);
                }
                else
                {
                    Unsafe.Add(ref r, dest--) = Unsafe.Add(ref temp, b--);
                }
            }

            if (b >= 0)
            {
                MemoryMarshal.CreateSpan(ref temp, b + 1)
                    .CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref r, dest - b), b + 1));
            }
        }

        private readonly int UpperBound(ref T start, int length, T value)
        {
            int low = 0;
            int high = length;
            while (low < high)
            {
                int middle = (int)((uint)(low + high) >> 1);
                if (less.Less(value, Unsafe.Add(ref start, middle)))
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return low;
        }

        private readonly int LowerBound(ref T start, int length, T value)
        {
            int low = 0;
            int high = length;
            while (low < high)
            {
                int middle = (int)((uint)(low + high) >> 1);
                if (less.Less(Unsafe.Add(ref start, middle), value))
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }

    [InlineArray(85)]
    private struct RunStack
    {
        private (int Start, int Length) _element;
    }
}
