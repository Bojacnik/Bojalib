using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>
/// Multithreaded sort for large arrays: a parallel sample sort whose buckets are sorted with <see cref="QuickSort"/>
/// (pdqsort with SIMD partitioning). Not stable. Uses a pooled buffer of n elements plus n bytes.
/// </summary>
/// <remarks>
/// <para>
/// A sorted random sample picks up to 127 splitters. All threads then classify their part of the input into buckets
/// in parallel (a binary search over the splitters), prefix sums give every thread its exact output positions, the
/// elements are scattered without locks, and the buckets are sorted in parallel, largest first. Values equal to a
/// splitter get a bucket of their own that needs no sorting, so inputs with many duplicates stay balanced.
/// </para>
/// <para>
/// Inputs smaller than about 64K elements are sorted on the calling thread, where threading would cost more than it
/// gains. Custom comparers are called from several threads at once and must be thread-safe.
/// <see langword="null"/>s and NaNs sort first, matching <see cref="Comparer{T}.Default"/>.
/// </para>
/// </remarks>
public static class ParallelSort
{
    private const int SequentialThreshold = 1 << 16;
    private const int MaxSplitters = 127; // 2 * 127 + 1 = 255 buckets, so a bucket id fits in a byte.
    private const int Oversampling = 16;

    /// <summary>Sorts <paramref name="array"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The elements to sort.</param>
    /// <param name="maxDegreeOfParallelism">The most threads to use; -1 for one per processor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDegreeOfParallelism"/> is 0 or less than -1.</exception>
    public static void Sort<T>(T[] array, int maxDegreeOfParallelism = -1) where T : IComparable<T>?
    {
        ArgumentNullException.ThrowIfNull(array);
        Sort(array.AsMemory(), maxDegreeOfParallelism);
    }

    /// <summary>Sorts <paramref name="memory"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="memory">The elements to sort.</param>
    /// <param name="maxDegreeOfParallelism">The most threads to use; -1 for one per processor.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDegreeOfParallelism"/> is 0 or less than -1.</exception>
    public static void Sort<T>(Memory<T> memory, int maxDegreeOfParallelism = -1) where T : IComparable<T>?
    {
        int threads = Threads(maxDegreeOfParallelism);

        // 8/16-bit integers are counting-sorted in one memory-bound pass; threads wouldn't help.
        if (memory.Length < SequentialThreshold || threads == 1 || IsCountingSortType<T>())
        {
            QuickSort.SortDefault(memory.Span);
            return;
        }

        if (Primitives.IsFloatingPoint<T>())
        {
            memory = memory[Primitives.MoveNaNsToFront(memory.Span)..];
        }

        if (Primitives.TryHandlePresorted(memory.Span))
        {
            return;
        }

        SampleSort(memory, default(DefaultLess<T>), threads);
    }

    /// <summary>Sorts <paramref name="array"/> using <paramref name="comparer"/>, which must be thread-safe.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The elements to sort.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <param name="maxDegreeOfParallelism">The most threads to use; -1 for one per processor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDegreeOfParallelism"/> is 0 or less than -1.</exception>
    public static void Sort<T>(T[] array, IComparer<T>? comparer, int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(array);
        Sort(array.AsMemory(), comparer, maxDegreeOfParallelism);
    }

    /// <summary>Sorts <paramref name="memory"/> using <paramref name="comparer"/>, which must be thread-safe.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="memory">The elements to sort.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <param name="maxDegreeOfParallelism">The most threads to use; -1 for one per processor.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDegreeOfParallelism"/> is 0 or less than -1.</exception>
    public static void Sort<T>(Memory<T> memory, IComparer<T>? comparer, int maxDegreeOfParallelism = -1) =>
        Core(memory, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default), Threads(maxDegreeOfParallelism));

    /// <summary>Sorts <paramref name="array"/> using <paramref name="comparison"/>, which must be thread-safe.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The elements to sort.</param>
    /// <param name="comparison">The comparison.</param>
    /// <param name="maxDegreeOfParallelism">The most threads to use; -1 for one per processor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> or <paramref name="comparison"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDegreeOfParallelism"/> is 0 or less than -1.</exception>
    public static void Sort<T>(T[] array, Comparison<T> comparison, int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(array);
        Sort(array.AsMemory(), comparison, maxDegreeOfParallelism);
    }

    /// <summary>Sorts <paramref name="memory"/> using <paramref name="comparison"/>, which must be thread-safe.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="memory">The elements to sort.</param>
    /// <param name="comparison">The comparison.</param>
    /// <param name="maxDegreeOfParallelism">The most threads to use; -1 for one per processor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDegreeOfParallelism"/> is 0 or less than -1.</exception>
    public static void Sort<T>(Memory<T> memory, Comparison<T> comparison, int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        Core(memory, new ComparisonLess<T>(comparison), Threads(maxDegreeOfParallelism));
    }

    private static int Threads(int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism == 0 || maxDegreeOfParallelism < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Must be -1 or a positive number.");
        }

        return maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : Math.Min(maxDegreeOfParallelism, Environment.ProcessorCount);
    }

    private static bool IsCountingSortType<T>() =>
        typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) ||
        typeof(T) == typeof(ushort) || typeof(T) == typeof(char);

    private static void Core<T, TLess>(Memory<T> memory, TLess less, int threads) where TLess : ILess<T>
    {
        if (memory.Length < SequentialThreshold || threads == 1)
        {
            PdqSort.Sort(memory.Span, less);
            return;
        }

        SampleSort(memory, less, threads);
    }

    private static void SampleSort<T, TLess>(Memory<T> memory, TLess less, int threads) where TLess : ILess<T>
    {
        int n = memory.Length;
        T[] splitters = ChooseSplitters(memory.Span, less, threads, out int splitterCount);
        if (splitterCount == 0)
        {
            // The whole sample was one value: almost certainly heavy duplication, which pdqsort handles in O(n).
            PdqSort.Sort(memory.Span, less);
            ArrayPool<T>.Shared.Return(splitters, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            return;
        }

        int bucketCount = (2 * splitterCount) + 1;
        int chunkCount = Math.Min(threads * 4, (n + 4095) / 4096);
        int chunkSize = (n + chunkCount - 1) / chunkCount;

        byte[] bucketOf = ArrayPool<byte>.Shared.Rent(n);
        int[] counts = new int[chunkCount * bucketCount]; // counts[chunk * buckets + bucket], then write offsets
        T[] buffer = ArrayPool<T>.Shared.Rent(n);
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        try
        {
            // 1. Classify every element and count bucket sizes per chunk.
            Parallel.For(0, chunkCount, options, chunk =>
            {
                ReadOnlySpan<T> items = memory.Span;
                ReadOnlySpan<T> splitterSpan = splitters.AsSpan(0, splitterCount);
                Span<int> chunkCounts = counts.AsSpan(chunk * bucketCount, bucketCount);
                int end = Math.Min(n, (chunk + 1) * chunkSize);
                for (int i = chunk * chunkSize; i < end; i++)
                {
                    int bucket = Classify(items[i], splitterSpan, less);
                    bucketOf[i] = (byte)bucket;
                    chunkCounts[bucket]++;
                }
            });

            // 2. Prefix sums: bucket b of chunk c starts after all smaller buckets, and after bucket b of earlier chunks.
            int[] bucketStarts = new int[bucketCount + 1];
            int position = 0;
            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                bucketStarts[bucket] = position;
                for (int chunk = 0; chunk < chunkCount; chunk++)
                {
                    int count = counts[(chunk * bucketCount) + bucket];
                    counts[(chunk * bucketCount) + bucket] = position;
                    position += count;
                }
            }

            bucketStarts[bucketCount] = n;

            // 3. Scatter into the buffer (each chunk writes to its own disjoint positions: no locks).
            Parallel.For(0, chunkCount, options, chunk =>
            {
                ReadOnlySpan<T> items = memory.Span;
                Span<int> offsets = counts.AsSpan(chunk * bucketCount, bucketCount);
                int end = Math.Min(n, (chunk + 1) * chunkSize);
                for (int i = chunk * chunkSize; i < end; i++)
                {
                    buffer[offsets[bucketOf[i]]++] = items[i];
                }
            });

            // 4. Copy back in parallel.
            Parallel.For(0, chunkCount, options, chunk =>
            {
                int start = chunk * chunkSize;
                int length = Math.Min(n, start + chunkSize) - start;
                buffer.AsSpan(start, length).CopyTo(memory.Span.Slice(start, length));
            });

            // 5. Sort the "between splitters" buckets (even indices) in parallel, largest first for load balance.
            //    Odd buckets hold values equal to one splitter and are already sorted.
            int[] order = [.. Enumerable.Range(0, splitterCount + 1).Select(static i => 2 * i)];
            Array.Sort(order, (a, b) => (bucketStarts[b + 1] - bucketStarts[b]).CompareTo(bucketStarts[a + 1] - bucketStarts[a]));
            Parallel.ForEach(order, options, bucket =>
            {
                Memory<T> part = memory[bucketStarts[bucket]..bucketStarts[bucket + 1]];
                if (part.Length > SequentialThreshold && part.Length > n / 2)
                {
                    // Badly skewed sample: split this bucket again in parallel.
                    SampleSort(part, less, threads);
                }
                else
                {
                    PdqSort.Sort(part.Span, less);
                }
            });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bucketOf);
            ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            ArrayPool<T>.Shared.Return(splitters, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    /// <summary>Sorts a pseudo-random sample and takes evenly spaced, distinct elements of it as splitters.</summary>
    private static T[] ChooseSplitters<T, TLess>(ReadOnlySpan<T> items, TLess less, int threads, out int count)
        where TLess : ILess<T>
    {
        int wanted = Math.Min(MaxSplitters, (threads * 8) - 1);
        int sampleSize = (wanted + 1) * Oversampling;
        T[] sample = ArrayPool<T>.Shared.Rent(sampleSize);
        ulong state = (ulong)items.Length * 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < sampleSize; i++)
        {
            // SplitMix64: cheap, deterministic, well spread.
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            sample[i] = items[(int)((z >> 32) * (ulong)items.Length >> 32)];
        }

        Span<T> sorted = sample.AsSpan(0, sampleSize);
        PdqSort.Sort(sorted, less);

        T[] splitters = ArrayPool<T>.Shared.Rent(wanted);
        count = 0;
        for (int i = 1; i <= wanted; i++)
        {
            T candidate = sorted[i * sampleSize / (wanted + 1)];
            if (count == 0 || less.Less(splitters[count - 1], candidate))
            {
                splitters[count++] = candidate;
            }
        }

        // A single distinct value carries no information: report "no splitters" if the sample was all equal.
        if (count == 1 && !less.Less(sorted[0], sorted[^1]))
        {
            count = 0;
        }

        ArrayPool<T>.Shared.Return(sample, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        return splitters;
    }

    /// <summary>
    /// Bucket of <paramref name="value"/>: 2k for values strictly between splitters k-1 and k, 2k+1 for values equal
    /// to splitter k. The splitter search is branchless.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Classify<T, TLess>(T value, ReadOnlySpan<T> splitters, TLess less) where TLess : ILess<T>
    {
        // Number of splitters <= value.
        ref T r = ref MemoryMarshal.GetReference(splitters);
        int start = 0;
        int length = splitters.Length;
        while (length > 1)
        {
            int half = length >> 1;
            start = !less.Less(value, Unsafe.Add(ref r, start + half)) ? start + half : start;
            length -= half;
        }

        int atMost = start + (!less.Less(value, Unsafe.Add(ref r, start)) ? 1 : 0);
        return atMost > 0 && !less.Less(Unsafe.Add(ref r, atMost - 1), value) ? (2 * atMost) - 1 : 2 * atMost;
    }
}
