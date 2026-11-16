using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Bojalib.Collections.Internal;

/// <summary>
/// Position search in a small sorted array of built-in integers: because the keys are sorted, "how many are less
/// than x" is the insertion point, and SIMD counts that over a 64-key B-tree node in a handful of compare + popcount
/// instructions, with no unpredictable branches.
/// </summary>
internal static class VectorSearch
{
    public static bool IsSupported<T>()
    {
        bool integer = typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) ||
                       typeof(T) == typeof(ulong) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort) ||
                       typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(char) ||
                       typeof(T) == typeof(nint) || typeof(T) == typeof(nuint);
        return integer && Vector128.IsHardwareAccelerated;
    }

    /// <summary>Number of the first <paramref name="count"/> keys that are less than (or equal to) <paramref name="value"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count<T>(T[] keys, int start, int count, T value, bool orEqual)
    {
        ref T r = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(keys), start);
        int result = 0;
        int i = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<T> needle = Vector256.Create(value);
            for (; i + Vector256<T>.Count <= count; i += Vector256<T>.Count)
            {
                Vector256<T> chunk = Vector256.LoadUnsafe(ref r, (nuint)i);
                Vector256<T> hit = orEqual ? Vector256.LessThanOrEqual(chunk, needle) : Vector256.LessThan(chunk, needle);
                result += BitOperations.PopCount(hit.ExtractMostSignificantBits());
            }
        }

        Vector128<T> needle128 = Vector128.Create(value);
        for (; i + Vector128<T>.Count <= count; i += Vector128<T>.Count)
        {
            Vector128<T> chunk = Vector128.LoadUnsafe(ref r, (nuint)i);
            Vector128<T> hit = orEqual ? Vector128.LessThanOrEqual(chunk, needle128) : Vector128.LessThan(chunk, needle128);
            result += BitOperations.PopCount(hit.ExtractMostSignificantBits());
        }

        Comparer<T> comparer = Comparer<T>.Default;
        for (; i < count; i++)
        {
            int c = comparer.Compare(Unsafe.Add(ref r, i), value);
            result += (orEqual ? c <= 0 : c < 0) ? 1 : 0;
        }

        return result;
    }
}
