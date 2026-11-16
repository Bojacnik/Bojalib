using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bojalib.Sorting;

/// <summary>
/// Stable least-significant-digit radix sort for any built-in integer type (including <see cref="Int128"/>).
/// O(n · bytes) with no comparisons, typically several times faster than comparison sorts on large inputs.
/// Uses a pooled buffer of n elements.
/// </summary>
/// <remarks>
/// One pass counts all byte histograms at once; byte positions where every element has the same value are skipped,
/// so e.g. <see langword="long"/>s that all fit in 20 bits take three passes, not eight.
/// </remarks>
public static class RadixSort
{
    // Below this, the histogram setup costs more than a comparison sort.
    private const int Threshold = 256;

    /// <summary>Sorts <paramref name="span"/> in ascending order.</summary>
    /// <typeparam name="T">A built-in integer type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    public static void Sort<T>(Span<T> span) where T : unmanaged, IBinaryInteger<T>, IMinMaxValue<T>
    {
        int n = span.Length;
        if (n < Threshold || !BitConverter.IsLittleEndian)
        {
            QuickSort.SortDefault(span);
            return;
        }

        int size = Unsafe.SizeOf<T>();
        bool signed = T.IsNegative(T.MinValue);
        int signByte = size - 1;

        Span<int> counts = stackalloc int[size * 256];
        counts.Clear();
        foreach (T value in span)
        {
            T copy = value;
            ref byte bytes = ref Unsafe.As<T, byte>(ref copy);
            for (int b = 0; b < size; b++)
            {
                counts[(b * 256) + Key(ref bytes, b, signed && b == signByte)]++;
            }
        }

        T[] rented = ArrayPool<T>.Shared.Rent(n);
        Span<T> source = span;
        Span<T> destination = rented.AsSpan(0, n);

        for (int b = 0; b < size; b++)
        {
            Span<int> histogram = counts.Slice(b * 256, 256);
            if (histogram.Contains(n))
            {
                continue; // Every element has the same byte here: this pass wouldn't move anything.
            }

            int offset = 0;
            for (int i = 0; i < 256; i++)
            {
                int count = histogram[i];
                histogram[i] = offset;
                offset += count;
            }

            ref T dst = ref MemoryMarshal.GetReference(destination);
            bool flip = signed && b == signByte;
            foreach (T value in source)
            {
                T copy = value;
                int key = Key(ref Unsafe.As<T, byte>(ref copy), b, flip);
                Unsafe.Add(ref dst, histogram[key]++) = value;
            }

            Span<T> swap = source;
            source = destination;
            destination = swap;
        }

        if (source != span)
        {
            source.CopyTo(span);
        }

        ArrayPool<T>.Shared.Return(rented);
    }

    // The sign byte is flipped so negative numbers (top bit set) sort before positive ones.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Key(ref byte bytes, int index, bool flipSign) => Unsafe.Add(ref bytes, index) ^ (flipSign ? 0x80 : 0);
}
