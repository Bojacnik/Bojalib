using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bojalib.Hashing;
using Bojalib.Sorting;

namespace Bojalib.Probabilistic;

/// <summary>
/// An immutable set-membership filter built once from a fixed set of items (Graf &amp; Lemire's binary fuse filter,
/// 2022). Like a Bloom filter it never gives false negatives, but it is about 30% smaller for the same false-positive
/// rate, and a lookup is always exactly three memory reads and two XORs.
/// </summary>
/// <remarks>
/// <para>
/// With 8-bit fingerprints the false-positive rate is about 0.39% (1/256) at roughly 9 bits per item; with 16-bit
/// fingerprints about 0.0015% at roughly 18 bits per item. Items can't be added after construction — use
/// <see cref="BloomFilter{T}"/> for a set that grows.
/// </para>
/// <para>
/// Hashing is the same as <see cref="BloomFilter{T}"/>: strings and primitives hash stably, so
/// <see cref="ToBytes"/>/<see cref="FromBytes"/> work across processes; other types need a custom hasher for that.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("Count = {Count}, FingerprintBits = {FingerprintBits}")]
public sealed class BinaryFuseFilter<T>
{
    private const int Arity = 3;
    private const int MaxAttempts = 100;
    private const int HeaderBytes = 32;

    private readonly Func<T, ulong>? _hasher;
    private readonly ulong _seed;
    private readonly int _segmentLength;
    private readonly int _segmentLengthMask;
    private readonly int _segmentCountLength;
    private readonly byte[]? _fingerprints8;
    private readonly ushort[]? _fingerprints16;

    /// <summary>Builds a filter containing <paramref name="items"/>. Duplicates are fine.</summary>
    /// <param name="items">The items.</param>
    /// <param name="fingerprintBits">8 (≈0.39% false positives) or 16 (≈0.0015%, twice the size).</param>
    /// <param name="hasher">An optional custom 64-bit hash, e.g. for portable filters of non-primitive types.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fingerprintBits"/> is not 8 or 16.</exception>
    public BinaryFuseFilter(IEnumerable<T> items, int fingerprintBits = 8, Func<T, ulong>? hasher = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (fingerprintBits is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(fingerprintBits), "Must be 8 or 16.");
        }

        _hasher = hasher;
        ulong[] keys = [.. items.Select(item => ItemHasher<T>.Hash(item, hasher))];
        RadixSort.Sort<ulong>(keys);
        int count = keys.AsSpan().Length == 0 ? 0 : Unique(keys);
        Count = count;

        (_segmentLength, int segmentCount) = Dimensions(count);
        _segmentLengthMask = _segmentLength - 1;
        _segmentCountLength = segmentCount * _segmentLength;
        int arrayLength = (segmentCount + Arity - 1) * _segmentLength;

        ulong[] order = new ulong[count];
        int[] slots = new int[count];
        ulong seed = 0x726b2b9d438b9d4dUL;
        for (int attempt = 0; ; attempt++)
        {
            if (attempt == MaxAttempts)
            {
                throw new InvalidOperationException("Could not build the filter (this should be astronomically unlikely).");
            }

            seed = ItemHasher<T>.Mix(seed + 0x9E3779B97F4A7C15UL);
            if (TryPeel(keys.AsSpan(0, count), seed, arrayLength, order, slots))
            {
                break;
            }
        }

        _seed = seed;

        // Assign fingerprints in reverse peeling order: each key's own slot is the last of its three to be set,
        // so it can be chosen to make the XOR of all three equal the key's fingerprint.
        if (fingerprintBits == 8)
        {
            _fingerprints8 = new byte[arrayLength];
            for (int k = count - 1; k >= 0; k--)
            {
                ulong h = order[k];
                (int a, int b, int c) = Positions(h);
                _fingerprints8[slots[k]] = 0;
                _fingerprints8[slots[k]] = (byte)(Fingerprint(h) ^ _fingerprints8[a] ^ _fingerprints8[b] ^ _fingerprints8[c]);
            }
        }
        else
        {
            _fingerprints16 = new ushort[arrayLength];
            for (int k = count - 1; k >= 0; k--)
            {
                ulong h = order[k];
                (int a, int b, int c) = Positions(h);
                _fingerprints16[slots[k]] = 0;
                _fingerprints16[slots[k]] = (ushort)(Fingerprint(h) ^ _fingerprints16[a] ^ _fingerprints16[b] ^ _fingerprints16[c]);
            }
        }
    }

    private BinaryFuseFilter(Func<T, ulong>? hasher, ulong seed, int segmentLength, int segmentCount, int count, byte[]? fingerprints8, ushort[]? fingerprints16)
    {
        _hasher = hasher;
        _seed = seed;
        _segmentLength = segmentLength;
        _segmentLengthMask = segmentLength - 1;
        _segmentCountLength = segmentCount * segmentLength;
        Count = count;
        _fingerprints8 = fingerprints8;
        _fingerprints16 = fingerprints16;
    }

    /// <summary>Gets the number of distinct items the filter was built from.</summary>
    public int Count { get; }

    /// <summary>Gets the fingerprint size: 8 or 16 bits.</summary>
    public int FingerprintBits => _fingerprints8 is not null ? 8 : 16;

    /// <summary>Gets the expected false-positive rate: 2^-<see cref="FingerprintBits"/>.</summary>
    public double FalsePositiveRate => 1.0 / (1 << FingerprintBits);

    /// <summary>Gets the size of the fingerprint table in bytes.</summary>
    public int ByteCount => _fingerprints8?.Length ?? (_fingerprints16!.Length * 2);

    /// <summary>Gets whether <see cref="ToBytes"/> output is meaningful in another process (see <see cref="BloomFilter{T}.IsPortable"/>).</summary>
    public bool IsPortable => ItemHasher<T>.IsPortable(_hasher);

    /// <summary>Returns <see langword="false"/> if <paramref name="item"/> was definitely not in the set, <see langword="true"/> if it probably was.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Whether the item might be in the set.</returns>
    public bool MightContain(T item)
    {
        if (Count == 0)
        {
            return false;
        }

        ulong h = ItemHasher<T>.Mix(ItemHasher<T>.Hash(item, _hasher) + _seed);
        (int a, int b, int c) = Positions(h);
        if (_fingerprints8 is not null)
        {
            return (byte)(Fingerprint(h) ^ _fingerprints8[a] ^ _fingerprints8[b] ^ _fingerprints8[c]) == 0;
        }

        return (ushort)(Fingerprint(h) ^ _fingerprints16![a] ^ _fingerprints16[b] ^ _fingerprints16[c]) == 0;
    }

    /// <summary>Serializes the filter (little-endian). Only meaningful in another process if <see cref="IsPortable"/>.</summary>
    /// <returns>The filter's bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[HeaderBytes + ByteCount];
        Span<byte> header = bytes;
        BinaryPrimitives.WriteUInt64LittleEndian(header, _seed);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], _segmentLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], _segmentCountLength / _segmentLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], Count);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], FingerprintBits);
        if (_fingerprints8 is not null)
        {
            _fingerprints8.CopyTo(bytes, HeaderBytes);
        }
        else
        {
            for (int i = 0; i < _fingerprints16!.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(HeaderBytes + (i * 2)), _fingerprints16[i]);
            }
        }

        return bytes;
    }

    /// <summary>Recreates a filter from <see cref="ToBytes"/> output.</summary>
    /// <param name="bytes">The serialized filter.</param>
    /// <param name="hasher">The same custom hasher the filter was built with, if any.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a serialized filter.</exception>
#pragma warning disable CA1000 // Factory on the generic type it creates.
    public static BinaryFuseFilter<T> FromBytes(ReadOnlySpan<byte> bytes, Func<T, ulong>? hasher = null)
#pragma warning restore CA1000
    {
        if (bytes.Length < HeaderBytes)
        {
            throw new ArgumentException("Too short to be a serialized filter.", nameof(bytes));
        }

        ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        int segmentLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int segmentCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        int bits = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]);
        long arrayLength = (long)(segmentCount + Arity - 1) * segmentLength;
        bool valid = bits is 8 or 16 && segmentLength > 0 && (segmentLength & (segmentLength - 1)) == 0 && segmentCount > 0
                     && count >= 0 && bytes.Length == HeaderBytes + (arrayLength * (bits / 8));
        if (!valid)
        {
            throw new ArgumentException("Not a serialized binary fuse filter.", nameof(bytes));
        }

        ReadOnlySpan<byte> data = bytes[HeaderBytes..];
        if (bits == 8)
        {
            return new BinaryFuseFilter<T>(hasher, seed, segmentLength, segmentCount, count, data.ToArray(), null);
        }

        ushort[] fingerprints = new ushort[arrayLength];
        for (int i = 0; i < fingerprints.Length; i++)
        {
            fingerprints[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
        }

        return new BinaryFuseFilter<T>(hasher, seed, segmentLength, segmentCount, count, null, fingerprints);
    }

    // Segment length and count for `size` keys, following the reference implementation's tuning for arity 3.
    private static (int SegmentLength, int SegmentCount) Dimensions(int size)
    {
        int segmentLength = size == 0 ? 4 : 1 << (int)Math.Floor((Math.Log(size) / Math.Log(3.33)) + 2.25);
        segmentLength = Math.Min(segmentLength, 1 << 18);
        double sizeFactor = size <= 1 ? 0 : Math.Max(1.125, 0.875 + (0.25 * Math.Log(1_000_000) / Math.Log(size)));
        long capacity = (long)Math.Round(size * sizeFactor);
        long segmentCount = Math.Max(1, ((capacity + segmentLength - 1) / segmentLength) - (Arity - 1));
        return (segmentLength, (int)segmentCount);
    }

    // Three slots, one in each of three consecutive segments.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int A, int B, int C) Positions(ulong h)
    {
        int a = (int)Math.BigMul(h, (ulong)_segmentCountLength, out _);
        int b = a + _segmentLength;
        int c = b + _segmentLength;
        b ^= (int)(h >> 18) & _segmentLengthMask;
        c ^= (int)h & _segmentLengthMask;
        return (a, b, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Fingerprint(ulong h) => h ^ (h >> 32);

    private static int Unique(ulong[] sorted)
    {
        int count = 1;
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] != sorted[count - 1])
            {
                sorted[count++] = sorted[i];
            }
        }

        return count;
    }

    /// <summary>
    /// Peeling: a slot used by exactly one key determines that key. Remove it, which may leave other slots with a
    /// single key, and repeat. Succeeds when every key gets peeled; records the order and each key's own slot.
    /// </summary>
    private bool TryPeel(ReadOnlySpan<ulong> keys, ulong seed, int arrayLength, ulong[] order, int[] slots)
    {
        int[] counts = new int[arrayLength];
        ulong[] xors = new ulong[arrayLength];
        foreach (ulong key in keys)
        {
            ulong h = ItemHasher<T>.Mix(key + seed);
            (int a, int b, int c) = Positions(h);
            counts[a]++;
            counts[b]++;
            counts[c]++;
            xors[a] ^= h;
            xors[b] ^= h;
            xors[c] ^= h;
        }

        int[] queue = new int[arrayLength];
        int queueLength = 0;
        for (int i = 0; i < arrayLength; i++)
        {
            if (counts[i] == 1)
            {
                queue[queueLength++] = i;
            }
        }

        int peeled = 0;
        while (queueLength > 0)
        {
            int slot = queue[--queueLength];
            if (counts[slot] != 1)
            {
                continue;
            }

            ulong h = xors[slot]; // the only key left in this slot
            order[peeled] = h;
            slots[peeled++] = slot;

            (int a, int b, int c) = Positions(h);
            foreach (int position in (ReadOnlySpan<int>)[a, b, c])
            {
                counts[position]--;
                xors[position] ^= h;
                if (counts[position] == 1)
                {
                    queue[queueLength++] = position;
                }
            }
        }

        return peeled == keys.Length;
    }
}
