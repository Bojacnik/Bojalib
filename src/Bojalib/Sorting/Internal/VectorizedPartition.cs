using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Bojalib.Sorting.Internal;

/// <summary>
/// In-place quicksort partition that classifies a whole vector of elements per step and writes the "left" and
/// "right" elements to both ends of the range in one go, with no data-dependent branches.
/// </summary>
/// <remarks>
/// <para>
/// Each loaded vector is compared with the pivot, then rearranged so its left elements sit in the low lanes and its
/// right elements in the high lanes. The whole vector is written twice — at the left write cursor and ending at the
/// right write cursor — and each cursor advances by its own element count; the extra lanes land in free space that
/// is overwritten later. Reading one vector from each end before starting guarantees there is always enough free
/// space, and the next vector is read from whichever side has less of it.
/// </para>
/// <para>
/// AVX-512: lanes are packed with the <c>vpcompress</c>/<c>vpexpand</c> instructions. AVX2: with
/// <c>vpermd</c> and a precomputed shuffle per comparison mask. Supports 32- and 64-bit integers and floats
/// (floats must not contain NaN).
/// </para>
/// </remarks>
internal static class VectorizedPartition
{
    /// <summary>Ranges shorter than this use the scalar partition (the setup cost isn't worth it).</summary>
    public const int MinLength = 128;

    // For each 8-bit "goes right" mask: lane indices of the left lanes, then of the right lanes.
    private static readonly int[] s_permute32 = BuildPermuteTable(lanes: 8, intsPerLane: 1);

    // Same for 4 x 64-bit lanes, expressed as pairs of 32-bit lane indices for vpermd.
    private static readonly int[] s_permute64 = BuildPermuteTable(lanes: 4, intsPerLane: 2);

    /// <summary>Whether <typeparamref name="T"/> can be partitioned with SIMD on this machine.</summary>
    public static bool IsSupported<T>()
    {
        bool supportedType = typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(float) ||
                             typeof(T) == typeof(long) || typeof(T) == typeof(ulong) || typeof(T) == typeof(double);
        return supportedType && (Avx512F.IsSupported || Avx2.IsSupported);
    }

    /// <summary>
    /// Partitions <paramref name="length"/> elements starting at <paramref name="start"/>: elements that don't go
    /// right come first. An element goes right if it is greater than <paramref name="pivot"/>, or also if equal
    /// when <paramref name="equalGoesRight"/> is set.
    /// </summary>
    /// <returns>The number of elements that went left.</returns>
    public static int Partition<T>(ref T start, int length, T pivot, bool equalGoesRight)
    {
        if (Avx512F.IsSupported)
        {
            return Partition512(ref start, length, pivot, equalGoesRight);
        }

        return Partition256(ref start, length, pivot, equalGoesRight);
    }

    internal static int Partition512<T>(ref T start, int length, T pivot, bool equalGoesRight)
    {
        int lanes = Vector512<T>.Count;
        Vector512<T> pivots = Vector512.Create(pivot);

        // Read one vector from each end first: that frees exactly two vectors of space to write into.
        Vector512<T> first = Vector512.LoadUnsafe(ref start);
        Vector512<T> last = Vector512.LoadUnsafe(ref start, (nuint)(length - lanes));
        int readLeft = lanes;
        int readRight = length - lanes;
        int writeLeft = 0;
        int writeRight = length;

        while (readRight - readLeft >= lanes)
        {
            Vector512<T> vector;
            if (readLeft - writeLeft <= writeRight - readRight)
            {
                vector = Vector512.LoadUnsafe(ref start, (nuint)readLeft);
                readLeft += lanes;
            }
            else
            {
                readRight -= lanes;
                vector = Vector512.LoadUnsafe(ref start, (nuint)readRight);
            }

            Store512(ref start, vector, pivots, equalGoesRight, ref writeLeft, ref writeRight);
        }

        PartitionTail(ref start, readLeft, readRight, pivot, equalGoesRight, ref writeLeft, ref writeRight);
        Store512(ref start, first, pivots, equalGoesRight, ref writeLeft, ref writeRight);
        Store512(ref start, last, pivots, equalGoesRight, ref writeLeft, ref writeRight);
        return writeLeft;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store512<T>(ref T start, Vector512<T> vector, Vector512<T> pivots, bool equalGoesRight,
        ref int writeLeft, ref int writeRight)
    {
        int lanes = Vector512<T>.Count;
        Vector512<T> goesRight = equalGoesRight
            ? Vector512.GreaterThanOrEqual(vector, pivots)
            : Vector512.GreaterThan(vector, pivots);
        int rightCount = BitOperations.PopCount(goesRight.ExtractMostSignificantBits());
        int leftCount = lanes - rightCount;

        if (Unsafe.SizeOf<T>() == sizeof(int))
        {
            Vector512<int> values = vector.AsInt32();
            Vector512<int> mask = goesRight.AsInt32();
            // Right elements packed into the low lanes, then spread into the top `rightCount` lanes...
            Vector512<int> rightsLow = Avx512F.Compress(Vector512<int>.Zero, mask, values);
            Vector512<int> topLanes = Vector512.GreaterThanOrEqual(Vector512<int>.Indices, Vector512.Create(leftCount));
            Vector512<int> rightsHigh = Avx512F.Expand(Vector512<int>.Zero, topLanes, rightsLow);
            // ...then the left elements are packed into the low lanes, keeping the high lanes from rightsHigh.
            Vector512<int> packed = Avx512F.Compress(rightsHigh, ~mask, values);
            ref int target = ref Unsafe.As<T, int>(ref start);
            packed.StoreUnsafe(ref target, (nuint)writeLeft);
            packed.StoreUnsafe(ref target, (nuint)(writeRight - lanes));
        }
        else
        {
            Vector512<long> values = vector.AsInt64();
            Vector512<long> mask = goesRight.AsInt64();
            Vector512<long> rightsLow = Avx512F.Compress(Vector512<long>.Zero, mask, values);
            Vector512<long> topLanes = Vector512.GreaterThanOrEqual(Vector512<long>.Indices, Vector512.Create((long)leftCount));
            Vector512<long> rightsHigh = Avx512F.Expand(Vector512<long>.Zero, topLanes, rightsLow);
            Vector512<long> packed = Avx512F.Compress(rightsHigh, ~mask, values);
            ref long target = ref Unsafe.As<T, long>(ref start);
            packed.StoreUnsafe(ref target, (nuint)writeLeft);
            packed.StoreUnsafe(ref target, (nuint)(writeRight - lanes));
        }

        writeLeft += leftCount;
        writeRight -= rightCount;
    }

    internal static int Partition256<T>(ref T start, int length, T pivot, bool equalGoesRight)
    {
        int lanes = Vector256<T>.Count;
        Vector256<T> pivots = Vector256.Create(pivot);

        Vector256<T> first = Vector256.LoadUnsafe(ref start);
        Vector256<T> last = Vector256.LoadUnsafe(ref start, (nuint)(length - lanes));
        int readLeft = lanes;
        int readRight = length - lanes;
        int writeLeft = 0;
        int writeRight = length;

        while (readRight - readLeft >= lanes)
        {
            Vector256<T> vector;
            if (readLeft - writeLeft <= writeRight - readRight)
            {
                vector = Vector256.LoadUnsafe(ref start, (nuint)readLeft);
                readLeft += lanes;
            }
            else
            {
                readRight -= lanes;
                vector = Vector256.LoadUnsafe(ref start, (nuint)readRight);
            }

            Store256(ref start, vector, pivots, equalGoesRight, ref writeLeft, ref writeRight);
        }

        PartitionTail(ref start, readLeft, readRight, pivot, equalGoesRight, ref writeLeft, ref writeRight);
        Store256(ref start, first, pivots, equalGoesRight, ref writeLeft, ref writeRight);
        Store256(ref start, last, pivots, equalGoesRight, ref writeLeft, ref writeRight);
        return writeLeft;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store256<T>(ref T start, Vector256<T> vector, Vector256<T> pivots, bool equalGoesRight,
        ref int writeLeft, ref int writeRight)
    {
        int lanes = Vector256<T>.Count;
        Vector256<T> goesRight = equalGoesRight
            ? Vector256.GreaterThanOrEqual(vector, pivots)
            : Vector256.GreaterThan(vector, pivots);
        uint mask = goesRight.ExtractMostSignificantBits();
        int rightCount = BitOperations.PopCount(mask);

        int[] table = Unsafe.SizeOf<T>() == sizeof(int) ? s_permute32 : s_permute64;
        Vector256<int> shuffle = Vector256.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(table), mask * 8);
        Vector256<int> packed = Avx2.PermuteVar8x32(vector.AsInt32(), shuffle);

        ref int target = ref Unsafe.As<T, int>(ref start);
        int intsPerLane = Unsafe.SizeOf<T>() / sizeof(int);
        packed.StoreUnsafe(ref target, (nuint)(writeLeft * intsPerLane));
        packed.StoreUnsafe(ref target, (nuint)((writeRight - lanes) * intsPerLane));

        writeLeft += lanes - rightCount;
        writeRight -= rightCount;
    }

    /// <summary>
    /// Places the fewer-than-one-vector elements left between the read cursors. They're copied out first so
    /// their slots become free space too, then written one by one.
    /// </summary>
    private static void PartitionTail<T>(ref T start, int readLeft, int readRight, T pivot, bool equalGoesRight,
        ref int writeLeft, ref int writeRight)
    {
        int count = readRight - readLeft;
        Vector512<T> buffer = default;
        ref T tail = ref Unsafe.As<Vector512<T>, T>(ref buffer);
        Unsafe.CopyBlockUnaligned(
            ref Unsafe.As<T, byte>(ref tail),
            ref Unsafe.As<T, byte>(ref Unsafe.Add(ref start, readLeft)),
            (uint)(count * Unsafe.SizeOf<T>()));

        DefaultLess<T> less = default;
        for (int i = 0; i < count; i++)
        {
            T value = Unsafe.Add(ref tail, i);
            bool goesRight = equalGoesRight ? !less.Less(value, pivot) : less.Less(pivot, value);
            if (goesRight)
            {
                Unsafe.Add(ref start, --writeRight) = value;
            }
            else
            {
                Unsafe.Add(ref start, writeLeft++) = value;
            }
        }
    }

    private static int[] BuildPermuteTable(int lanes, int intsPerLane)
    {
        int[] table = new int[(1 << lanes) * 8];
        for (int mask = 0; mask < 1 << lanes; mask++)
        {
            int position = 0;
            foreach (bool wantRight in (ReadOnlySpan<bool>)[false, true])
            {
                for (int lane = 0; lane < lanes; lane++)
                {
                    if ((((mask >> lane) & 1) == 1) == wantRight)
                    {
                        for (int part = 0; part < intsPerLane; part++)
                        {
                            table[(mask * 8) + position++] = (lane * intsPerLane) + part;
                        }
                    }
                }
            }
        }

        return table;
    }
}
