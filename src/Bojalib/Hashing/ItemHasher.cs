using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bojalib.Hashing;

/// <summary>
/// The 64-bit item hash shared by the probabilistic structures: XXH64 for strings and built-in primitives/enums
/// (stable across processes, so serialized structures stay valid), a caller-supplied function if given, and
/// otherwise <see cref="EqualityComparer{T}.Default"/> spread to 64 bits (process-specific).
/// </summary>
internal static class ItemHasher<T>
{
    public static bool HasStableDefault => typeof(T) == typeof(string) || typeof(T).IsPrimitive || typeof(T).IsEnum;

    public static bool IsPortable(Func<T, ulong>? hasher) => hasher is not null || HasStableDefault;

    public static ulong Hash(T item, Func<T, ulong>? hasher)
    {
        if (hasher is not null)
        {
            return hasher(item);
        }

        if (typeof(T) == typeof(string))
        {
            return XxHash64.Hash(((string)(object)item!).AsSpan());
        }

        if (HasStableDefault)
        {
            // Primitives and enums have no padding, so their raw bytes are a stable representation.
            return XxHash64.Hash(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref item), Unsafe.SizeOf<T>()));
        }

        return Mix((ulong)(item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item)));
    }

    /// <summary>SplitMix64 finalizer: a bijective mix that spreads every input bit over the whole output.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Mix(ulong h)
    {
        h = (h ^ (h >> 30)) * 0xbf58476d1ce4e5b9UL;
        h = (h ^ (h >> 27)) * 0x94d049bb133111ebUL;
        return h ^ (h >> 31);
    }
}
