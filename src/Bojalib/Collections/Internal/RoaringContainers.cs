using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Bojalib.Collections.Internal;

/// <summary>The low 16 bits of the values that share one high-16-bit key: stored as an array or a bitmap.</summary>
internal abstract class RoaringContainer
{
    /// <summary>Above this many values a bitmap (8 KB) is smaller than a sorted array of ushorts.</summary>
    public const int ArrayMaxSize = 4096;

    public abstract int Cardinality { get; }

    public abstract ushort Min { get; }

    public abstract ushort Max { get; }

    public abstract bool Contains(ushort value);

    /// <summary>Adds <paramref name="value"/>; returns the container to use from now on (it may change kind).</summary>
    public abstract RoaringContainer Add(ushort value, out bool added);

    /// <summary>Removes <paramref name="value"/>; returns the container to use from now on (it may change kind).</summary>
    public abstract RoaringContainer Remove(ushort value, out bool removed);

    /// <summary>Number of values &lt;= <paramref name="value"/>.</summary>
    public abstract int Rank(ushort value);

    /// <summary>The <paramref name="index"/>-th smallest value.</summary>
    public abstract ushort Select(int index);

    public abstract RoaringContainer Clone();

    public abstract int SerializedBytes { get; }

    public abstract void Serialize(Span<byte> destination);

    public abstract BitmapContainer ToBitmap();

    public static RoaringContainer? Normalize(BitmapContainer bitmap) =>
        bitmap.Cardinality == 0 ? null : bitmap.Cardinality <= ArrayMaxSize ? bitmap.ToArray() : bitmap;

    public static RoaringContainer? Normalize(ArrayContainer array) =>
        array.Cardinality == 0 ? null : array.Cardinality > ArrayMaxSize ? array.ToBitmap() : array;
}

internal sealed class ArrayContainer(ushort[] values, int count) : RoaringContainer
{
    public ushort[] Values = values;
    public int Count = count;

    public ArrayContainer()
        : this(new ushort[4], 0)
    {
    }

    public ReadOnlySpan<ushort> Span => Values.AsSpan(0, Count);

    public override int Cardinality => Count;

    public override ushort Min => Values[0];

    public override ushort Max => Values[Count - 1];

    public override bool Contains(ushort value) => Span.BinarySearch(value) >= 0;

    public override RoaringContainer Add(ushort value, out bool added)
    {
        int index = Span.BinarySearch(value);
        if (index >= 0)
        {
            added = false;
            return this;
        }

        added = true;
        if (Count == ArrayMaxSize)
        {
            BitmapContainer bitmap = ToBitmap();
            bitmap.Add(value, out _);
            return bitmap;
        }

        index = ~index;
        if (Count == Values.Length)
        {
            Array.Resize(ref Values, Math.Min(ArrayMaxSize, Values.Length * 2));
        }

        Array.Copy(Values, index, Values, index + 1, Count - index);
        Values[index] = value;
        Count++;
        return this;
    }

    public override RoaringContainer Remove(ushort value, out bool removed)
    {
        int index = Span.BinarySearch(value);
        removed = index >= 0;
        if (removed)
        {
            Count--;
            Array.Copy(Values, index + 1, Values, index, Count - index);
        }

        return this;
    }

    public override int Rank(ushort value)
    {
        int index = Span.BinarySearch(value);
        return index >= 0 ? index + 1 : ~index;
    }

    public override ushort Select(int index) => Values[index];

    public override RoaringContainer Clone() => new ArrayContainer((ushort[])Values.Clone(), Count);

    public override int SerializedBytes => Count * 2;

    public override void Serialize(Span<byte> destination)
    {
        for (int i = 0; i < Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], Values[i]);
        }
    }

    public override BitmapContainer ToBitmap()
    {
        var bitmap = new BitmapContainer();
        foreach (ushort value in Span)
        {
            bitmap.Words[value >> 6] |= 1UL << value;
        }

        bitmap.Count = Count;
        return bitmap;
    }
}

internal sealed class BitmapContainer : RoaringContainer
{
    public const int WordCount = 1024;

    public readonly ulong[] Words = new ulong[WordCount];
    public int Count;

    public override int Cardinality => Count;

    public override ushort Min
    {
        get
        {
            int word = 0;
            while (Words[word] == 0)
            {
                word++;
            }

            return (ushort)((word * 64) + BitOperations.TrailingZeroCount(Words[word]));
        }
    }

    public override ushort Max
    {
        get
        {
            int word = WordCount - 1;
            while (Words[word] == 0)
            {
                word--;
            }

            return (ushort)((word * 64) + 63 - BitOperations.LeadingZeroCount(Words[word]));
        }
    }

    public override bool Contains(ushort value) => (Words[value >> 6] & (1UL << value)) != 0;

    public override RoaringContainer Add(ushort value, out bool added)
    {
        ref ulong word = ref Words[value >> 6];
        ulong bit = 1UL << value;
        added = (word & bit) == 0;
        if (added)
        {
            word |= bit;
            Count++;
        }

        return this;
    }

    public override RoaringContainer Remove(ushort value, out bool removed)
    {
        ref ulong word = ref Words[value >> 6];
        ulong bit = 1UL << value;
        removed = (word & bit) != 0;
        if (!removed)
        {
            return this;
        }

        word &= ~bit;
        Count--;
        return Count <= ArrayMaxSize ? ToArray() : this;
    }

    public override int Rank(ushort value)
    {
        int word = value >> 6;
        int rank = 0;
        for (int i = 0; i < word; i++)
        {
            rank += BitOperations.PopCount(Words[i]);
        }

        // Bits 0..value of the last word.
        int shift = 63 - (value & 63);
        return rank + BitOperations.PopCount(Words[word] << shift);
    }

    public override ushort Select(int index)
    {
        for (int word = 0; ; word++)
        {
            int bits = BitOperations.PopCount(Words[word]);
            if (index < bits)
            {
                ulong w = Words[word];
                for (int i = 0; i < index; i++)
                {
                    w &= w - 1; // clear lowest set bit
                }

                return (ushort)((word * 64) + BitOperations.TrailingZeroCount(w));
            }

            index -= bits;
        }
    }

    public override RoaringContainer Clone()
    {
        var copy = new BitmapContainer { Count = Count };
        Words.CopyTo(copy.Words, 0);
        return copy;
    }

    public override int SerializedBytes => WordCount * 8;

    public override void Serialize(Span<byte> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(Words.AsSpan()).CopyTo(destination);
            return;
        }

        for (int i = 0; i < WordCount; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination[(i * 8)..], Words[i]);
        }
    }

    public override BitmapContainer ToBitmap() => this;

    public ArrayContainer ToArray()
    {
        var values = new ushort[Math.Max(4, Count)];
        int n = 0;
        for (int word = 0; word < WordCount; word++)
        {
            ulong w = Words[word];
            while (w != 0)
            {
                values[n++] = (ushort)((word * 64) + BitOperations.TrailingZeroCount(w));
                w &= w - 1;
            }
        }

        return new ArrayContainer(values, n);
    }

    /// <summary>Sets bits [start, end] inclusive.</summary>
    public void SetRange(int start, int end)
    {
        for (int word = start >> 6; word <= end >> 6; word++)
        {
            int low = word == start >> 6 ? start & 63 : 0;
            int high = word == end >> 6 ? end & 63 : 63;
            ulong mask = (high == 63 ? ulong.MaxValue : (1UL << (high + 1)) - 1) & (ulong.MaxValue << low);
            Words[word] |= mask;
        }
    }

    public void RecountCardinality()
    {
        int count = 0;
        foreach (ulong word in Words)
        {
            count += BitOperations.PopCount(word);
        }

        Count = count;
    }
}

/// <summary>Set operations between containers. Bitmap-with-bitmap work runs 256 bits at a time.</summary>
internal static class RoaringOps
{
    public enum Operation
    {
        Or,
        And,
        Xor,
        AndNot,
    }

    public static RoaringContainer? Apply(RoaringContainer a, RoaringContainer b, Operation op)
    {
        if (a is ArrayContainer arrayA && b is ArrayContainer arrayB)
        {
            return RoaringContainer.Normalize(MergeArrays(arrayA.Span, arrayB.Span, op));
        }

        if (op == Operation.And)
        {
            // Small side drives: filter the array through the bitmap.
            if (a is ArrayContainer sparse)
            {
                return Filter(sparse, (BitmapContainer)b, keep: true);
            }

            if (b is ArrayContainer sparseB)
            {
                return Filter(sparseB, (BitmapContainer)a, keep: true);
            }
        }

        if (op == Operation.AndNot && a is ArrayContainer left)
        {
            return Filter(left, (BitmapContainer)b, keep: false);
        }

        return RoaringContainer.Normalize(Words(a.ToBitmap(), b.ToBitmap(), op));
    }

    private static BitmapContainer Words(BitmapContainer a, BitmapContainer b, Operation op)
    {
        var result = new BitmapContainer();
        ref ulong x = ref MemoryMarshal.GetArrayDataReference(a.Words);
        ref ulong y = ref MemoryMarshal.GetArrayDataReference(b.Words);
        ref ulong z = ref MemoryMarshal.GetArrayDataReference(result.Words);
        int count = 0;
        for (nuint i = 0; i < BitmapContainer.WordCount; i += (nuint)Vector256<ulong>.Count)
        {
            Vector256<ulong> va = Vector256.LoadUnsafe(ref x, i);
            Vector256<ulong> vb = Vector256.LoadUnsafe(ref y, i);
            Vector256<ulong> vz = op switch
            {
                Operation.Or => va | vb,
                Operation.And => va & vb,
                Operation.Xor => va ^ vb,
                _ => Vector256.AndNot(va, vb),
            };
            vz.StoreUnsafe(ref z, i);
            for (int lane = 0; lane < Vector256<ulong>.Count; lane++)
            {
                count += BitOperations.PopCount(vz.GetElement(lane));
            }
        }

        result.Count = count;
        return result;
    }

    private static ArrayContainer? Filter(ArrayContainer array, BitmapContainer bitmap, bool keep)
    {
        var values = new ushort[Math.Max(4, array.Count)];
        int n = 0;
        foreach (ushort value in array.Span)
        {
            if (bitmap.Contains(value) == keep)
            {
                values[n++] = value;
            }
        }

        return n == 0 ? null : new ArrayContainer(values, n);
    }

    // Linear merge of two sorted arrays for every operation.
    private static ArrayContainer MergeArrays(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b, Operation op)
    {
        var values = new ushort[Math.Max(4, op == Operation.And ? Math.Min(a.Length, b.Length) : op == Operation.AndNot ? a.Length : a.Length + b.Length)];
        int n = 0;
        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            ushort x = a[i];
            ushort y = b[j];
            if (x == y)
            {
                if (op is Operation.Or or Operation.And)
                {
                    values[n++] = x;
                }

                i++;
                j++;
            }
            else if (x < y)
            {
                if (op != Operation.And)
                {
                    values[n++] = x;
                }

                i++;
            }
            else
            {
                if (op is Operation.Or or Operation.Xor)
                {
                    values[n++] = y;
                }

                j++;
            }
        }

        if (op != Operation.And)
        {
            for (; i < a.Length; i++)
            {
                values[n++] = a[i];
            }

            if (op != Operation.AndNot)
            {
                for (; j < b.Length; j++)
                {
                    values[n++] = b[j];
                }
            }
        }

        return new ArrayContainer(values, n);
    }
}
