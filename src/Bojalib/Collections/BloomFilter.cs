using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Bojalib.Hashing;

namespace Bojalib.Collections;

/// <summary>
/// A space-efficient set that can say "definitely not present" or "probably present": no false negatives, and a
/// tunable false-positive rate. Uses the split-block design (as in Apache Parquet and Impala): each item touches one
/// 32-byte block — within one cache line — and sets one bit in each of its eight 32-bit words, computed with a single
/// AVX2 multiply/shift sequence. A classic Bloom filter touches k random cache lines per operation instead; the
/// trade-off is slightly more memory for the same false-positive rate.
/// </summary>
/// <remarks>
/// <para>
/// Strings and built-in primitive types are hashed with <see cref="XxHash64"/>, which is stable across processes,
/// so <see cref="ToBytes"/> / <see cref="FromBytes"/> round-trip between machines (<see cref="IsPortable"/>).
/// Other types use <see cref="EqualityComparer{T}.Default"/> unless a hash function is supplied.
/// </para>
/// <para>Not thread-safe for concurrent writes.</para>
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Blocks = {BlockCount}")]
public sealed class BloomFilter<T>
{
    private const int WordsPerBlock = 8;
    private const int BytesPerBlock = WordsPerBlock * sizeof(uint);

    // Odd constants from the Parquet spec: each lane multiplies the hash by its own salt and keeps the top 5 bits
    // as the bit index within its word.
    private static readonly Vector256<uint> s_salts = Vector256.Create(
        0x47b6137bU, 0x44974d91U, 0x8824ad5bU, 0xa2b7289dU, 0x705495c7U, 0x2df1424bU, 0x9efc4947U, 0x5c6bfb31U);

    private readonly uint[] _words;
    private readonly int _blockCount;
    private readonly Func<T, ulong>? _hasher;

    /// <summary>Creates a filter sized for <paramref name="expectedItems"/> items at <paramref name="falsePositiveRate"/>.</summary>
    /// <param name="expectedItems">How many distinct items will be added.</param>
    /// <param name="falsePositiveRate">The acceptable chance that <see cref="MightContain"/> wrongly returns true, e.g. 0.01.</param>
    /// <param name="hasher">
    /// A custom 64-bit hash. Required for portable filters of types other than strings and primitives; the default
    /// for those types is <see cref="EqualityComparer{T}.Default"/>, which changes between processes.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public BloomFilter(int expectedItems, double falsePositiveRate, Func<T, ulong>? hasher = null)
        : this(BlocksFor(expectedItems, falsePositiveRate), hasher)
    {
    }

    private BloomFilter(int blockCount, Func<T, ulong>? hasher)
    {
        _blockCount = blockCount;
        _words = new uint[blockCount * WordsPerBlock];
        _hasher = hasher;
    }

    /// <summary>Gets the number of 32-byte blocks.</summary>
    public int BlockCount => _blockCount;

    /// <summary>Gets the size of the filter in bytes.</summary>
    public int ByteCount => _blockCount * BytesPerBlock;

    /// <summary>
    /// Gets whether the hashes don't depend on the process, so <see cref="ToBytes"/> output is meaningful elsewhere:
    /// true for strings, built-in primitives, enums and filters with a custom hasher.
    /// </summary>
    public bool IsPortable => _hasher is not null || UsesStableHash;

    private static bool UsesStableHash => typeof(T) == typeof(string) || typeof(T).IsPrimitive || typeof(T).IsEnum;

    /// <summary>Adds <paramref name="item"/>.</summary>
    /// <param name="item">The item.</param>
    public void Add(T item)
    {
        ulong hash = Hash(item);
        Span<uint> block = Block(hash);
        Vector256<uint> mask = Mask((uint)hash);
        Vector256<uint> updated = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(block)) | mask;
        updated.StoreUnsafe(ref MemoryMarshal.GetReference(block));
    }

    /// <summary>Returns <see langword="false"/> if <paramref name="item"/> was definitely never added, <see langword="true"/> if it probably was.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Whether the item might be in the set.</returns>
    public bool MightContain(T item)
    {
        ulong hash = Hash(item);
        Span<uint> block = Block(hash);
        Vector256<uint> mask = Mask((uint)hash);
        Vector256<uint> words = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(block));
        return (words & mask) == mask;
    }

    /// <summary>Adds every item of <paramref name="other"/> to this filter (bitwise OR, SIMD). Both must have the same size and hasher.</summary>
    /// <param name="other">A filter with the same <see cref="BlockCount"/>.</param>
    /// <exception cref="ArgumentException">The filters have different sizes.</exception>
    public void UnionWith(BloomFilter<T> other)
    {
        CheckCompatible(other);
        Combine(_words, other._words, union: true);
    }

    /// <summary>
    /// Keeps only bits set in both filters (bitwise AND, SIMD). The result answers <see langword="true"/> for items
    /// probably in both sets — with a somewhat higher false-positive rate than a filter built from the intersection.
    /// </summary>
    /// <param name="other">A filter with the same <see cref="BlockCount"/>.</param>
    /// <exception cref="ArgumentException">The filters have different sizes.</exception>
    public void IntersectWith(BloomFilter<T> other)
    {
        CheckCompatible(other);
        Combine(_words, other._words, union: false);
    }

    /// <summary>Estimates the false-positive rate from how full the filter is.</summary>
    /// <returns>A value between 0 and 1.</returns>
    public double EstimateFalsePositiveRate()
    {
        long setBits = 0;
        foreach (uint word in _words)
        {
            setBits += BitOperations.PopCount(word);
        }

        // A lookup tests one bit per word, and each word is about this full.
        double fill = setBits / (double)(_words.Length * 32);
        return Math.Pow(fill, WordsPerBlock);
    }

    /// <summary>Removes all items.</summary>
    public void Clear() => Array.Clear(_words);

    /// <summary>Serializes the filter (little-endian). Only meaningful in another process if <see cref="IsPortable"/>.</summary>
    /// <returns>The filter's bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[ByteCount];
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(_words.AsSpan()).CopyTo(bytes);
        }
        else
        {
            for (int i = 0; i < _words.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), _words[i]);
            }
        }

        return bytes;
    }

    /// <summary>Recreates a filter from <see cref="ToBytes"/> output.</summary>
    /// <param name="bytes">The serialized filter.</param>
    /// <param name="hasher">The same custom hasher the filter was built with, if any.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> isn't a whole number of 32-byte blocks.</exception>
#pragma warning disable CA1000 // Factory on the generic type it creates.
    public static BloomFilter<T> FromBytes(ReadOnlySpan<byte> bytes, Func<T, ulong>? hasher = null)
#pragma warning restore CA1000
    {
        if (bytes.IsEmpty || bytes.Length % BytesPerBlock != 0)
        {
            throw new ArgumentException("Expected a non-empty whole number of 32-byte blocks.", nameof(bytes));
        }

        var filter = new BloomFilter<T>(bytes.Length / BytesPerBlock, hasher);
        for (int i = 0; i < filter._words.Length; i++)
        {
            filter._words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
        }

        return filter;
    }

    // Bits per item for the split-block layout at a target FPR (Parquet's sizing formula).
    private static int BlocksFor(int expectedItems, double falsePositiveRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedItems, 1);
        if (falsePositiveRate is <= 0 or >= 1 || double.IsNaN(falsePositiveRate))
        {
            throw new ArgumentOutOfRangeException(nameof(falsePositiveRate), "Must be between 0 and 1 (exclusive).");
        }

        double bits = -WordsPerBlock * expectedItems / Math.Log(1 - Math.Pow(falsePositiveRate, 1.0 / WordsPerBlock));
        double blocks = Math.Ceiling(bits / (BytesPerBlock * 8));
        return (int)Math.Clamp(blocks, 1, int.MaxValue / BytesPerBlock);
    }

    private ulong Hash(T item)
    {
        if (_hasher is not null)
        {
            return _hasher(item);
        }

        if (typeof(T) == typeof(string))
        {
            return XxHash64.Hash(((string)(object)item!).AsSpan());
        }

        if (UsesStableHash)
        {
            // Primitives and enums have no padding, so their raw bytes are a stable representation.
            return XxHash64.Hash(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref item), Unsafe.SizeOf<T>()));
        }

        // Spread a 32-bit hash over 64 bits (a SplitMix64 finalizer).
        ulong h = (ulong)(item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item));
        h = (h ^ (h >> 30)) * 0xbf58476d1ce4e5b9UL;
        h = (h ^ (h >> 27)) * 0x94d049bb133111ebUL;
        return h ^ (h >> 31);
    }

    // The upper 32 bits pick the block (multiply-shift instead of modulo), the lower 32 bits pick the bits in it.
    private Span<uint> Block(ulong hash)
    {
        int block = (int)(((hash >> 32) * (ulong)_blockCount) >> 32);
        return _words.AsSpan(block * WordsPerBlock, WordsPerBlock);
    }

    private static Vector256<uint> Mask(uint key)
    {
        Vector256<uint> bitIndexes = (Vector256.Create(key) * s_salts) >> 27;
        if (Avx2.IsSupported)
        {
            return Avx2.ShiftLeftLogicalVariable(Vector256<uint>.One, bitIndexes);
        }

        Span<uint> lanes = stackalloc uint[WordsPerBlock];
        for (int i = 0; i < WordsPerBlock; i++)
        {
            lanes[i] = 1u << (int)bitIndexes[i];
        }

        return Vector256.Create<uint>(lanes);
    }

    private static void Combine(Span<uint> target, ReadOnlySpan<uint> source, bool union)
    {
        ref uint t = ref MemoryMarshal.GetReference(target);
        ref uint s = ref MemoryMarshal.GetReference(source);
        for (nuint i = 0; i < (nuint)target.Length; i += WordsPerBlock)
        {
            Vector256<uint> a = Vector256.LoadUnsafe(ref t, i);
            Vector256<uint> b = Vector256.LoadUnsafe(ref s, i);
            (union ? a | b : a & b).StoreUnsafe(ref t, i);
        }
    }

    private void CheckCompatible(BloomFilter<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._blockCount != _blockCount)
        {
            throw new ArgumentException("Filters must have the same size.", nameof(other));
        }
    }
}
