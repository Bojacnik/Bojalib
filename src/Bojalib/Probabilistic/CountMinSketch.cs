using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Bojalib.Hashing;

namespace Bojalib.Probabilistic;

/// <summary>
/// Estimates how often each item occurs in a stream, in fixed memory: heavy hitters, rate limiting, "top URLs".
/// Estimates never undercount; they overcount by at most <c>epsilon × TotalCount</c> with probability
/// <c>1 − delta</c>.
/// </summary>
/// <remarks>
/// <para>
/// A table of <see cref="Depth"/> rows by <see cref="Width"/> counters. Adding an item increments one counter per row;
/// the estimate is the smallest of those counters. With <c>conservativeUpdate</c>, only the counters that are at the
/// current minimum are raised, which reduces overcounting considerably (but sketches built that way should only be
/// merged with care, since the merge of two conservative sketches is no longer conservative).
/// </para>
/// <para>Sketches with the same dimensions and hashing can be merged with <see cref="UnionWith"/> (SIMD addition).</para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("Width = {Width}, Depth = {Depth}, TotalCount = {TotalCount}")]
public sealed class CountMinSketch<T>
{
    private readonly long[] _counters;
    private readonly Func<T, ulong>? _hasher;

    /// <summary>Creates a sketch sized for an error bound.</summary>
    /// <param name="epsilon">Estimates exceed the true count by at most epsilon × <see cref="TotalCount"/>, e.g. 0.001.</param>
    /// <param name="delta">The probability of exceeding that bound, e.g. 0.01.</param>
    /// <param name="conservativeUpdate">Raise only the minimal counters on add (more accurate).</param>
    /// <param name="hasher">An optional custom 64-bit hash.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside (0, 1).</exception>
    public CountMinSketch(double epsilon, double delta, bool conservativeUpdate = false, Func<T, ulong>? hasher = null)
        : this(WidthFor(epsilon), DepthFor(delta), conservativeUpdate, hasher)
    {
    }

    /// <summary>Creates a sketch with explicit dimensions.</summary>
    /// <param name="width">Counters per row.</param>
    /// <param name="depth">Number of rows (independent hash functions).</param>
    /// <param name="conservativeUpdate">Raise only the minimal counters on add (more accurate).</param>
    /// <param name="hasher">An optional custom 64-bit hash.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="depth"/> is less than 1.</exception>
    public CountMinSketch(int width, int depth, bool conservativeUpdate = false, Func<T, ulong>? hasher = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)width * depth, Array.MaxLength, nameof(width));
        Width = width;
        Depth = depth;
        ConservativeUpdate = conservativeUpdate;
        _counters = new long[width * depth];
        _hasher = hasher;
    }

    /// <summary>Gets the number of counters per row.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Depth { get; }

    /// <summary>Gets whether adds use conservative update.</summary>
    public bool ConservativeUpdate { get; }

    /// <summary>Gets the sum of all counts added.</summary>
    public long TotalCount { get; private set; }

    /// <summary>Adds <paramref name="count"/> occurrences of <paramref name="item"/>.</summary>
    /// <param name="item">The item.</param>
    /// <param name="count">How many occurrences; must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public void Add(T item, long count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ulong hash = ItemHasher<T>.Hash(item, _hasher);
        TotalCount += count;

        if (!ConservativeUpdate)
        {
            for (int row = 0; row < Depth; row++)
            {
                _counters[Index(hash, row)] += count;
            }

            return;
        }

        long target = EstimateHash(hash) + count;
        for (int row = 0; row < Depth; row++)
        {
            ref long counter = ref _counters[Index(hash, row)];
            counter = Math.Max(counter, target);
        }
    }

    /// <summary>Estimates how many times <paramref name="item"/> was added. Never less than the true count.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The estimate.</returns>
    public long Estimate(T item) => EstimateHash(ItemHasher<T>.Hash(item, _hasher));

    /// <summary>Adds all counts of <paramref name="other"/> to this sketch.</summary>
    /// <param name="other">A sketch with the same width, depth and hashing.</param>
    /// <exception cref="ArgumentException">The dimensions differ.</exception>
    public void UnionWith(CountMinSketch<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Width != Width || other.Depth != Depth)
        {
            throw new ArgumentException("Sketches must have the same width and depth.", nameof(other));
        }

        ref long target = ref MemoryMarshal.GetArrayDataReference(_counters);
        ref long source = ref MemoryMarshal.GetArrayDataReference(other._counters);
        nuint length = (nuint)_counters.Length;
        nuint i = 0;
        for (; i + (nuint)Vector128<long>.Count <= length; i += (nuint)Vector128<long>.Count)
        {
            (Vector128.LoadUnsafe(ref target, i) + Vector128.LoadUnsafe(ref source, i)).StoreUnsafe(ref target, i);
        }

        for (; i < length; i++)
        {
            _counters[i] += other._counters[i];
        }

        TotalCount += other.TotalCount;
    }

    /// <summary>Resets all counts to zero.</summary>
    public void Clear()
    {
        Array.Clear(_counters);
        TotalCount = 0;
    }

    /// <summary>Serializes the sketch (little-endian).</summary>
    /// <returns>The sketch's bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[17 + (_counters.Length * 8)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Depth);
        bytes[8] = ConservativeUpdate ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(9), TotalCount);
        for (int i = 0; i < _counters.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(17 + (i * 8)), _counters[i]);
        }

        return bytes;
    }

    /// <summary>Recreates a sketch from <see cref="ToBytes"/> output.</summary>
    /// <param name="bytes">The serialized sketch.</param>
    /// <param name="hasher">The same custom hasher the sketch was built with, if any.</param>
    /// <returns>The sketch.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a serialized sketch.</exception>
#pragma warning disable CA1000 // Factory on the generic type it creates.
    public static CountMinSketch<T> FromBytes(ReadOnlySpan<byte> bytes, Func<T, ulong>? hasher = null)
#pragma warning restore CA1000
    {
        if (bytes.Length < 17)
        {
            throw new ArgumentException("Not a serialized Count-Min sketch.", nameof(bytes));
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int depth = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (width < 1 || depth < 1 || bytes.Length != 17 + ((long)width * depth * 8))
        {
            throw new ArgumentException("Not a serialized Count-Min sketch.", nameof(bytes));
        }

        var sketch = new CountMinSketch<T>(width, depth, bytes[8] == 1, hasher) { TotalCount = BinaryPrimitives.ReadInt64LittleEndian(bytes[9..]) };
        for (int i = 0; i < sketch._counters.Length; i++)
        {
            sketch._counters[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes[(17 + (i * 8))..]);
        }

        return sketch;
    }

    private long EstimateHash(ulong hash)
    {
        long minimum = long.MaxValue;
        for (int row = 0; row < Depth; row++)
        {
            minimum = Math.Min(minimum, _counters[Index(hash, row)]);
        }

        return minimum;
    }

    // Double hashing (Kirsch–Mitzenmacher): row i uses h1 + i·h2, mapped to a column by multiply-shift.
    private int Index(ulong hash, int row)
    {
        uint h1 = (uint)hash;
        uint h2 = (uint)(hash >> 32) | 1;
        uint combined = h1 + ((uint)row * h2);
        return (row * Width) + (int)(((ulong)combined * (ulong)Width) >> 32);
    }

    private static int WidthFor(double epsilon)
    {
        if (epsilon is <= 0 or >= 1 || double.IsNaN(epsilon))
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon), "Must be between 0 and 1 (exclusive).");
        }

        return (int)Math.Min(int.MaxValue, Math.Ceiling(Math.E / epsilon));
    }

    private static int DepthFor(double delta)
    {
        if (delta is <= 0 or >= 1 || double.IsNaN(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "Must be between 0 and 1 (exclusive).");
        }

        return Math.Max(1, (int)Math.Ceiling(Math.Log(1 / delta)));
    }
}
