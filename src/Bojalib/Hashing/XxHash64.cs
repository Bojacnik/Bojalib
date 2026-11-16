using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Bojalib.Hashing;

/// <summary>
/// The XXH64 non-cryptographic hash (Yann Collet): very fast and stable across processes, machines and .NET versions —
/// unlike <see cref="object.GetHashCode"/>, which is randomized per process. Use it for hashes that are stored,
/// sent elsewhere, or used to build data structures that are saved (e.g. <see cref="Collections.BloomFilter{T}"/>).
/// </summary>
/// <remarks>Produces the same values as the reference implementation and <c>System.IO.Hashing.XxHash64</c>.</remarks>
public static class XxHash64
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    /// <summary>Hashes <paramref name="data"/>.</summary>
    /// <param name="data">The bytes to hash.</param>
    /// <param name="seed">An optional seed; different seeds give independent hash functions.</param>
    /// <returns>The 64-bit hash.</returns>
    public static ulong Hash(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        int length = data.Length;
        ulong hash;

        if (length >= 32)
        {
            ulong v1 = seed + Prime1 + Prime2;
            ulong v2 = seed + Prime2;
            ulong v3 = seed;
            ulong v4 = seed - Prime1;
            do
            {
                v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(data));
                v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(data[8..]));
                v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(data[16..]));
                v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(data[24..]));
                data = data[32..];
            }
            while (data.Length >= 32);

            hash = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) +
                   BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
            hash = MergeRound(hash, v1);
            hash = MergeRound(hash, v2);
            hash = MergeRound(hash, v3);
            hash = MergeRound(hash, v4);
        }
        else
        {
            hash = seed + Prime5;
        }

        hash += (ulong)length;

        while (data.Length >= 8)
        {
            hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(data));
            hash = (BitOperations.RotateLeft(hash, 27) * Prime1) + Prime4;
            data = data[8..];
        }

        if (data.Length >= 4)
        {
            hash ^= BinaryPrimitives.ReadUInt32LittleEndian(data) * Prime1;
            hash = (BitOperations.RotateLeft(hash, 23) * Prime2) + Prime3;
            data = data[4..];
        }

        foreach (byte b in data)
        {
            hash ^= b * Prime5;
            hash = BitOperations.RotateLeft(hash, 11) * Prime1;
        }

        // Avalanche.
        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;
        return hash;
    }

    /// <summary>Hashes the UTF-16 code units of <paramref name="text"/> (as little-endian bytes).</summary>
    /// <param name="text">The text.</param>
    /// <param name="seed">An optional seed.</param>
    /// <returns>The 64-bit hash.</returns>
    public static ulong Hash(ReadOnlySpan<char> text, ulong seed = 0)
    {
        if (BitConverter.IsLittleEndian)
        {
            return Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(text), seed);
        }

        byte[] bytes = new byte[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), text[i]);
        }

        return Hash(bytes, seed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong accumulator, ulong input) =>
        BitOperations.RotateLeft(accumulator + (input * Prime2), 31) * Prime1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MergeRound(ulong accumulator, ulong value) =>
        ((accumulator ^ Round(0, value)) * Prime1) + Prime4;
}
