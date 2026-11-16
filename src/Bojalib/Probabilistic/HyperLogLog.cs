using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Bojalib.Hashing;

namespace Bojalib.Probabilistic;

/// <summary>
/// Estimates how many <em>distinct</em> items have been added, in a few kilobytes no matter how many there are.
/// With the default precision of 14 it uses 16 KB and is typically within ±0.8% (one standard error).
/// </summary>
/// <remarks>
/// <para>
/// Uses 64-bit hashes and Otmar Ertl's improved estimator (2017), which is accurate across the whole range — from a
/// handful of items to billions — without the empirical bias-correction tables of HyperLogLog++.
/// </para>
/// <para>
/// Sketches with the same precision and hashing can be merged (<see cref="UnionWith"/>, a SIMD byte-wise maximum) to
/// count the distinct items of several streams, e.g. per-server sketches combined centrally.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("Precision = {Precision}")]
public sealed class HyperLogLog<T>
{
    private readonly byte[] _registers;
    private readonly Func<T, ulong>? _hasher;

    /// <summary>Creates an empty sketch.</summary>
    /// <param name="precision">
    /// Log2 of the number of registers, 4–18. Memory is 2^precision bytes; standard error is about
    /// 1.04 / sqrt(2^precision). 14 gives 16 KB and ±0.8%.
    /// </param>
    /// <param name="hasher">An optional custom 64-bit hash.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is outside 4–18.</exception>
    public HyperLogLog(int precision = 14, Func<T, ulong>? hasher = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 18);
        Precision = precision;
        _registers = new byte[1 << precision];
        _hasher = hasher;
    }

    /// <summary>Gets the precision (log2 of the register count).</summary>
    public int Precision { get; }

    /// <summary>Gets the typical relative error of <see cref="Estimate"/> (one standard error).</summary>
    public double StandardError => 1.04 / Math.Sqrt(_registers.Length);

    /// <summary>Gets the size of the sketch in bytes.</summary>
    public int ByteCount => _registers.Length;

    /// <summary>Records <paramref name="item"/>. Adding the same item again has no effect.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if the sketch changed.</returns>
    public bool Add(T item)
    {
        ulong hash = ItemHasher<T>.Hash(item, _hasher);
        int index = (int)(hash >> (64 - Precision));

        // Rank: position of the first 1-bit in the remaining bits (1-based), capped at 64 - precision + 1.
        ulong rest = hash << Precision;
        int rank = rest == 0 ? 64 - Precision + 1 : BitOperations.LeadingZeroCount(rest) + 1;

        if (rank > _registers[index])
        {
            _registers[index] = (byte)rank;
            return true;
        }

        return false;
    }

    /// <summary>Estimates the number of distinct items added.</summary>
    /// <returns>The estimate.</returns>
    public long Estimate()
    {
        int q = 64 - Precision;
        Span<int> histogram = stackalloc int[q + 2];
        histogram.Clear();
        foreach (byte register in _registers)
        {
            histogram[register]++;
        }

        // Ertl, "New cardinality estimation algorithms for HyperLogLog sketches", improved raw estimator.
        double m = _registers.Length;
        double z = m * Tau(1 - (histogram[q + 1] / m));
        for (int k = q; k >= 1; k--)
        {
            z = 0.5 * (z + histogram[k]);
        }

        z += m * Sigma(histogram[0] / m);
        double alpha = 1 / (2 * Math.Log(2));
        return (long)Math.Round(alpha * m * m / z);
    }

    /// <summary>Merges <paramref name="other"/> into this sketch, so it estimates the distinct items of both.</summary>
    /// <param name="other">A sketch with the same precision and hashing.</param>
    /// <exception cref="ArgumentException">The precisions differ.</exception>
    public void UnionWith(HyperLogLog<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Precision != Precision)
        {
            throw new ArgumentException("Sketches must have the same precision.", nameof(other));
        }

        // Registers are at least 16 bytes (precision >= 4), a multiple of the vector size.
        ref byte target = ref MemoryMarshal.GetArrayDataReference(_registers);
        ref byte source = ref MemoryMarshal.GetArrayDataReference(other._registers);
        for (nuint i = 0; i < (nuint)_registers.Length; i += (nuint)Vector128<byte>.Count)
        {
            Vector128.Max(Vector128.LoadUnsafe(ref target, i), Vector128.LoadUnsafe(ref source, i)).StoreUnsafe(ref target, i);
        }
    }

    /// <summary>Resets the sketch to empty.</summary>
    public void Clear() => Array.Clear(_registers);

    /// <summary>Serializes the sketch: one byte of precision followed by the registers.</summary>
    /// <returns>The sketch's bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[1 + _registers.Length];
        bytes[0] = (byte)Precision;
        _registers.CopyTo(bytes, 1);
        return bytes;
    }

    /// <summary>Recreates a sketch from <see cref="ToBytes"/> output.</summary>
    /// <param name="bytes">The serialized sketch.</param>
    /// <param name="hasher">The same custom hasher the sketch was built with, if any.</param>
    /// <returns>The sketch.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a serialized sketch.</exception>
#pragma warning disable CA1000 // Factory on the generic type it creates.
    public static HyperLogLog<T> FromBytes(ReadOnlySpan<byte> bytes, Func<T, ulong>? hasher = null)
#pragma warning restore CA1000
    {
        if (bytes.IsEmpty || bytes[0] is < 4 or > 18 || bytes.Length != 1 + (1 << bytes[0]))
        {
            throw new ArgumentException("Not a serialized HyperLogLog sketch.", nameof(bytes));
        }

        var sketch = new HyperLogLog<T>(bytes[0], hasher);
        bytes[1..].CopyTo(sketch._registers);
        return sketch;
    }

    private static double Sigma(double x)
    {
        if (x == 1)
        {
            return double.PositiveInfinity;
        }

        double y = 1;
        double z = x;
        double previous;
        do
        {
            x *= x;
            previous = z;
            z += x * y;
            y += y;
        }
        while (z != previous);

        return z;
    }

    private static double Tau(double x)
    {
        if (x is 0 or 1)
        {
            return 0;
        }

        double y = 1;
        double z = 1 - x;
        double previous;
        do
        {
            x = Math.Sqrt(x);
            previous = z;
            y *= 0.5;
            z -= Math.Pow(1 - x, 2) * y;
        }
        while (z != previous);

        return z / 3;
    }
}
