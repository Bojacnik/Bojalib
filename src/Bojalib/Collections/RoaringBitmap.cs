using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bojalib.Collections.Internal;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="RoaringBitmap"/> instances; also enables collection expressions.</summary>
public static class RoaringBitmapBuilder
{
    /// <summary>Creates a bitmap holding <paramref name="values"/>.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The bitmap.</returns>
    public static RoaringBitmap Create(ReadOnlySpan<uint> values)
    {
        var bitmap = new RoaringBitmap();
        foreach (uint value in values)
        {
            bitmap.Add(value);
        }

        return bitmap;
    }
}

/// <summary>
/// A compressed set of 32-bit unsigned integers (a Roaring bitmap, Lemire et al.): the structure behind the indexes of
/// Lucene, Spark, Druid, ClickHouse and many databases. Sets of IDs take a fraction of the memory of a
/// <see cref="HashSet{T}"/>, and union, intersection and difference of large sets run 256 bits per instruction.
/// </summary>
/// <remarks>
/// <para>
/// Values are grouped by their upper 16 bits into chunks of 65,536. A chunk with at most 4,096 values is a sorted
/// array of 16-bit values; a denser one is an 8 KB bitmap. Chunks switch representation automatically.
/// </para>
/// <para>
/// <see cref="ToBytes"/> / <see cref="FromBytes"/> use the standard portable Roaring format, so bitmaps can be
/// exchanged with the Java, Go, C/C++, Rust and Python implementations. Bitmaps written with run-length containers by
/// those libraries are read too (and stored as arrays/bitmaps). Not thread-safe.
/// </para>
/// <para>
/// Strengths are memory and whole-set operations; a single <see cref="Contains"/> is a binary search plus a container
/// probe, somewhat slower than a <see cref="HashSet{T}"/> lookup.
/// </para>
/// </remarks>
[DebuggerDisplay("Count = {Count}")]
[CollectionBuilder(typeof(RoaringBitmapBuilder), nameof(RoaringBitmapBuilder.Create))]
public sealed class RoaringBitmap : IReadOnlyCollection<uint>, IEquatable<RoaringBitmap>
{
    private const uint CookieNoRuns = 12346;
    private const uint CookieWithRuns = 12347;
    private const int NoOffsetThreshold = 4;

    private ushort[] _keys;
    private RoaringContainer[] _containers;
    private int _size; // number of chunks in use
    private int _version;

    /// <summary>Creates an empty bitmap.</summary>
    public RoaringBitmap()
    {
        _keys = new ushort[4];
        _containers = new RoaringContainer[4];
    }

    /// <summary>Creates a bitmap holding <paramref name="values"/>.</summary>
    /// <param name="values">The values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public RoaringBitmap(IEnumerable<uint> values)
        : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (uint value in values)
        {
            Add(value);
        }
    }

    private RoaringBitmap(ushort[] keys, RoaringContainer[] containers, int size)
    {
        _keys = keys;
        _containers = containers;
        _size = size;
    }

    /// <summary>Gets the number of values (up to 2^32, hence <see langword="long"/>).</summary>
    public long Count
    {
        get
        {
            long count = 0;
            for (int i = 0; i < _size; i++)
            {
                count += _containers[i].Cardinality;
            }

            return count;
        }
    }

    int IReadOnlyCollection<uint>.Count => checked((int)Count);

    /// <summary>Gets whether the bitmap is empty.</summary>
    public bool IsEmpty => _size == 0;

    /// <summary>Gets the smallest value.</summary>
    /// <exception cref="InvalidOperationException">The bitmap is empty.</exception>
    public uint Min => _size == 0 ? ThrowEmpty() : Combine(_keys[0], _containers[0].Min);

    /// <summary>Gets the largest value.</summary>
    /// <exception cref="InvalidOperationException">The bitmap is empty.</exception>
    public uint Max => _size == 0 ? ThrowEmpty() : Combine(_keys[_size - 1], _containers[_size - 1].Max);

    /// <summary>Gets the number of bytes <see cref="ToBytes"/> produces.</summary>
    public int SerializedSize
    {
        get
        {
            int bytes = 8 + (8 * _size);
            for (int i = 0; i < _size; i++)
            {
                bytes += _containers[i].SerializedBytes;
            }

            return bytes;
        }
    }

    /// <summary>Union: the values in either bitmap.</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">Another bitmap.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap operator |(RoaringBitmap left, RoaringBitmap right) => Combine(left, right, RoaringOps.Operation.Or);

    /// <summary>Intersection: the values in both bitmaps.</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">Another bitmap.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap operator &(RoaringBitmap left, RoaringBitmap right) => Combine(left, right, RoaringOps.Operation.And);

    /// <summary>Symmetric difference: the values in exactly one of the bitmaps.</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">Another bitmap.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap operator ^(RoaringBitmap left, RoaringBitmap right) => Combine(left, right, RoaringOps.Operation.Xor);

    /// <summary>Difference: the values in <paramref name="left"/> that are not in <paramref name="right"/>.</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">The values to remove.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap operator -(RoaringBitmap left, RoaringBitmap right) => Combine(left, right, RoaringOps.Operation.AndNot);

    /// <summary>Union (same as the <c>|</c> operator).</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">Another bitmap.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap BitwiseOr(RoaringBitmap left, RoaringBitmap right) => left | right;

    /// <summary>Intersection (same as the <c>&amp;</c> operator).</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">Another bitmap.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap BitwiseAnd(RoaringBitmap left, RoaringBitmap right) => left & right;

    /// <summary>Symmetric difference (same as the <c>^</c> operator).</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">Another bitmap.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap Xor(RoaringBitmap left, RoaringBitmap right) => left ^ right;

    /// <summary>Difference (same as the <c>-</c> operator).</summary>
    /// <param name="left">A bitmap.</param>
    /// <param name="right">The values to remove.</param>
    /// <returns>A new bitmap.</returns>
    public static RoaringBitmap Subtract(RoaringBitmap left, RoaringBitmap right) => left - right;

    /// <summary>Adds <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if it wasn't already present.</returns>
    public bool Add(uint value)
    {
        ushort key = (ushort)(value >> 16);
        int index = IndexOfKey(key);
        if (index < 0)
        {
            index = ~index;
            InsertContainer(index, key, new ArrayContainer());
        }

        _containers[index] = _containers[index].Add((ushort)value, out bool added);
        if (added)
        {
            _version++;
        }

        return added;
    }

    /// <summary>Adds every value in [<paramref name="start"/>, <paramref name="end"/>], setting whole bitmap words at a time.</summary>
    /// <param name="start">The first value.</param>
    /// <param name="end">The last value (inclusive).</param>
    /// <exception cref="ArgumentException"><paramref name="end"/> is less than <paramref name="start"/>.</exception>
    public void AddRange(uint start, uint end)
    {
        if (end < start)
        {
            throw new ArgumentException("The range ends before it starts.", nameof(end));
        }

        for (uint chunk = start >> 16; chunk <= end >> 16; chunk++)
        {
            int low = chunk == start >> 16 ? (ushort)start : 0;
            int high = chunk == end >> 16 ? (ushort)end : 0xFFFF;
            int index = IndexOfKey((ushort)chunk);
            if (index < 0)
            {
                index = ~index;
                InsertContainer(index, (ushort)chunk, new ArrayContainer());
            }

            if (high - low + 1 <= RoaringContainer.ArrayMaxSize && _containers[index] is ArrayContainer)
            {
                for (int v = low; v <= high; v++)
                {
                    _containers[index] = _containers[index].Add((ushort)v, out _);
                }
            }
            else
            {
                BitmapContainer bitmap = _containers[index].ToBitmap();
                bitmap.SetRange(low, high);
                bitmap.RecountCardinality();
                _containers[index] = RoaringContainer.Normalize(bitmap)!;
            }

            if (chunk == uint.MaxValue >> 16)
            {
                break; // avoid wrapping past the last chunk
            }
        }

        _version++;
    }

    /// <summary>Removes <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if it was present.</returns>
    public bool Remove(uint value)
    {
        int index = IndexOfKey((ushort)(value >> 16));
        if (index < 0)
        {
            return false;
        }

        _containers[index] = _containers[index].Remove((ushort)value, out bool removed);
        if (removed)
        {
            if (_containers[index].Cardinality == 0)
            {
                RemoveContainer(index);
            }

            _version++;
        }

        return removed;
    }

    /// <summary>Returns whether <paramref name="value"/> is present.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(uint value)
    {
        int index = IndexOfKey((ushort)(value >> 16));
        return index >= 0 && _containers[index].Contains((ushort)value);
    }

    /// <summary>Returns how many values are &lt;= <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rank.</returns>
    public long Rank(uint value)
    {
        ushort key = (ushort)(value >> 16);
        long rank = 0;
        for (int i = 0; i < _size && _keys[i] <= key; i++)
        {
            rank += _keys[i] < key ? _containers[i].Cardinality : _containers[i].Rank((ushort)value);
        }

        return rank;
    }

    /// <summary>Returns the value at position <paramref name="index"/> in ascending order.</summary>
    /// <param name="index">The position, 0 for the smallest.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public uint Select(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        for (int i = 0; i < _size; i++)
        {
            int cardinality = _containers[i].Cardinality;
            if (index < cardinality)
            {
                return Combine(_keys[i], _containers[i].Select((int)index));
            }

            index -= cardinality;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>Removes all values.</summary>
    public void Clear()
    {
        Array.Clear(_containers, 0, _size);
        _size = 0;
        _version++;
    }

    /// <summary>Returns the values as an array, ascending.</summary>
    /// <returns>A new array.</returns>
    public uint[] ToArray()
    {
        uint[] values = new uint[Count];
        int n = 0;
        foreach (uint value in this)
        {
            values[n++] = value;
        }

        return values;
    }

    /// <summary>Returns whether both bitmaps hold exactly the same values.</summary>
    /// <param name="other">Another bitmap.</param>
    /// <returns><see langword="true"/> if equal.</returns>
    public bool Equals(RoaringBitmap? other)
    {
        if (other is null || other._size != _size)
        {
            return false;
        }

        for (int i = 0; i < _size; i++)
        {
            if (_keys[i] != other._keys[i] || _containers[i].Cardinality != other._containers[i].Cardinality)
            {
                return false;
            }

            if (RoaringOps.Apply(_containers[i], other._containers[i], RoaringOps.Operation.Xor) is not null)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RoaringBitmap);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        for (int i = 0; i < _size; i++)
        {
            hash.Add(_keys[i]);
            hash.Add(_containers[i].Min);
            hash.Add(_containers[i].Max);
        }

        return hash.ToHashCode();
    }

    /// <summary>Serializes in the portable Roaring format (readable by other Roaring implementations).</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[SerializedSize];
        Span<byte> span = bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(span, CookieNoRuns);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], _size);

        int offset = 8 + (8 * _size);
        for (int i = 0; i < _size; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[(8 + (i * 4))..], _keys[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(10 + (i * 4))..], (ushort)(_containers[i].Cardinality - 1));
            BinaryPrimitives.WriteInt32LittleEndian(span[(8 + (4 * _size) + (i * 4))..], offset);
            _containers[i].Serialize(span[offset..]);
            offset += _containers[i].SerializedBytes;
        }

        return bytes;
    }

    /// <summary>Reads a bitmap in the portable Roaring format, with or without run containers.</summary>
    /// <param name="bytes">The serialized bitmap.</param>
    /// <returns>The bitmap.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a valid serialized Roaring bitmap.</exception>
    public static RoaringBitmap FromBytes(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return Read(bytes);
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new ArgumentException("Not a valid serialized Roaring bitmap (truncated).", nameof(bytes), e);
        }
    }

    /// <summary>Enumerates the values in ascending order.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<uint> IEnumerable<uint>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static RoaringBitmap Read(ReadOnlySpan<byte> bytes)
    {
        uint cookie = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        int size;
        int position;
        ReadOnlySpan<byte> runFlags = default;
        bool hasOffsets;
        if ((cookie & 0xFFFF) == CookieWithRuns)
        {
            size = (int)(cookie >> 16) + 1;
            int flagBytes = (size + 7) / 8;
            runFlags = bytes.Slice(4, flagBytes);
            position = 4 + flagBytes;
            hasOffsets = size >= NoOffsetThreshold;
        }
        else if (cookie == CookieNoRuns)
        {
            size = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
            position = 8;
            hasOffsets = true;
        }
        else
        {
            throw new ArgumentException("Not a serialized Roaring bitmap (unknown cookie).", nameof(bytes));
        }

        if (size < 0 || size > 65536)
        {
            throw new ArgumentException("Not a valid serialized Roaring bitmap (container count).", nameof(bytes));
        }

        var keys = new ushort[Math.Max(4, size)];
        var containers = new RoaringContainer[Math.Max(4, size)];
        ReadOnlySpan<byte> header = bytes.Slice(position, 4 * size);
        position += 4 * size;
        if (hasOffsets)
        {
            position += 4 * size; // offsets aren't needed for sequential reading
        }

        for (int i = 0; i < size; i++)
        {
            keys[i] = BinaryPrimitives.ReadUInt16LittleEndian(header[(i * 4)..]);
            int cardinality = BinaryPrimitives.ReadUInt16LittleEndian(header[((i * 4) + 2)..]) + 1;
            if (i > 0 && keys[i] <= keys[i - 1])
            {
                throw new ArgumentException("Not a valid serialized Roaring bitmap (keys out of order).", nameof(bytes));
            }

            bool isRun = !runFlags.IsEmpty && (runFlags[i / 8] & (1 << (i % 8))) != 0;
            if (isRun)
            {
                int runs = BinaryPrimitives.ReadUInt16LittleEndian(bytes[position..]);
                position += 2;
                var bitmap = new BitmapContainer();
                for (int r = 0; r < runs; r++)
                {
                    int start = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(position + (r * 4))..]);
                    int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(position + (r * 4) + 2)..]);
                    if (start + length > 0xFFFF)
                    {
                        throw new ArgumentException("Not a valid serialized Roaring bitmap (run out of range).", nameof(bytes));
                    }

                    bitmap.SetRange(start, start + length);
                }

                position += runs * 4;
                bitmap.RecountCardinality();
                containers[i] = RoaringContainer.Normalize(bitmap)
                    ?? throw new ArgumentException("Not a valid serialized Roaring bitmap (empty run container).", nameof(bytes));
            }
            else if (cardinality > RoaringContainer.ArrayMaxSize)
            {
                var bitmap = new BitmapContainer();
                for (int w = 0; w < BitmapContainer.WordCount; w++)
                {
                    bitmap.Words[w] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(position + (w * 8))..]);
                }

                position += BitmapContainer.WordCount * 8;
                bitmap.RecountCardinality();
                containers[i] = bitmap;
            }
            else
            {
                var values = new ushort[Math.Max(4, cardinality)];
                for (int v = 0; v < cardinality; v++)
                {
                    values[v] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(position + (v * 2))..]);
                    if (v > 0 && values[v] <= values[v - 1])
                    {
                        throw new ArgumentException("Not a valid serialized Roaring bitmap (array not sorted).", nameof(bytes));
                    }
                }

                position += cardinality * 2;
                containers[i] = new ArrayContainer(values, cardinality);
            }
        }

        return new RoaringBitmap(keys, containers, size);
    }

    // Walks both key lists like a merge, combining the containers that share a key.
    private static RoaringBitmap Combine(RoaringBitmap left, RoaringBitmap right, RoaringOps.Operation op)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var result = new RoaringBitmap();
        int i = 0;
        int j = 0;
        while (i < left._size && j < right._size)
        {
            ushort a = left._keys[i];
            ushort b = right._keys[j];
            if (a == b)
            {
                RoaringContainer? combined = RoaringOps.Apply(left._containers[i], right._containers[j], op);
                if (combined is not null)
                {
                    result.Append(a, combined);
                }

                i++;
                j++;
            }
            else if (a < b)
            {
                if (op != RoaringOps.Operation.And)
                {
                    result.Append(a, left._containers[i].Clone());
                }

                i++;
            }
            else
            {
                if (op is RoaringOps.Operation.Or or RoaringOps.Operation.Xor)
                {
                    result.Append(b, right._containers[j].Clone());
                }

                j++;
            }
        }

        if (op != RoaringOps.Operation.And)
        {
            for (; i < left._size; i++)
            {
                result.Append(left._keys[i], left._containers[i].Clone());
            }

            if (op is RoaringOps.Operation.Or or RoaringOps.Operation.Xor)
            {
                for (; j < right._size; j++)
                {
                    result.Append(right._keys[j], right._containers[j].Clone());
                }
            }
        }

        return result;
    }

    private void Append(ushort key, RoaringContainer container) => InsertContainer(_size, key, container);

    private int IndexOfKey(ushort key) => _keys.AsSpan(0, _size).BinarySearch(key);

    private void InsertContainer(int index, ushort key, RoaringContainer container)
    {
        if (_size == _keys.Length)
        {
            Array.Resize(ref _keys, _size * 2);
            Array.Resize(ref _containers, _size * 2);
        }

        Array.Copy(_keys, index, _keys, index + 1, _size - index);
        Array.Copy(_containers, index, _containers, index + 1, _size - index);
        _keys[index] = key;
        _containers[index] = container;
        _size++;
    }

    private void RemoveContainer(int index)
    {
        _size--;
        Array.Copy(_keys, index + 1, _keys, index, _size - index);
        Array.Copy(_containers, index + 1, _containers, index, _size - index);
        _containers[_size] = null!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Combine(ushort key, ushort low) => ((uint)key << 16) | low;

    private static uint ThrowEmpty()
    {
        ThrowHelper.ThrowEmptyCollection("bitmap");
        return 0;
    }

    /// <summary>Enumerates a <see cref="RoaringBitmap"/> in ascending order.</summary>
    public struct Enumerator : IEnumerator<uint>
    {
        private readonly RoaringBitmap _bitmap;
        private readonly int _version;
        private int _container;
        private int _position; // array index, or bitmap word index
        private ulong _word;   // remaining bits of the current bitmap word
        private uint _current;

        internal Enumerator(RoaringBitmap bitmap)
        {
            _bitmap = bitmap;
            _version = bitmap._version;
            _container = 0;
            _position = -1;
            _word = 0;
            _current = 0;
        }

        /// <inheritdoc />
        public readonly uint Current => _current;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _bitmap._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            while (_container < _bitmap._size)
            {
                ushort key = _bitmap._keys[_container];
                RoaringContainer container = _bitmap._containers[_container];
                if (container is ArrayContainer array)
                {
                    if (++_position < array.Count)
                    {
                        _current = Combine(key, array.Values[_position]);
                        return true;
                    }
                }
                else
                {
                    var bitmap = (BitmapContainer)container;
                    while (_word == 0 && ++_position < BitmapContainer.WordCount)
                    {
                        _word = bitmap.Words[_position];
                    }

                    if (_word != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(_word);
                        _word &= _word - 1;
                        _current = Combine(key, (ushort)((_position * 64) + bit));
                        return true;
                    }
                }

                _container++;
                _position = -1;
                _word = 0;
            }

            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _container = 0;
            _position = -1;
            _word = 0;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
