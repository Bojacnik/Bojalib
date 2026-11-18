using System.Runtime.CompilerServices;

namespace Bojalib.Sorting.Internal;

/// <summary>
/// Strict-weak-ordering "less than". Sort loops are generic over a struct implementing this, so the JIT
/// specialises each loop per comparer and inlines every comparison.
/// </summary>
internal interface ILess<T>
{
    bool Less(T x, T y);
}

/// <summary>
/// The default ordering (<see cref="Comparer{T}.Default"/>). For built-in numeric types this compiles down to a
/// single machine comparison: the <c>typeof</c> checks are constants per instantiation, so the JIT removes every
/// branch but one. Floating-point callers must move NaNs out of the way first (see <see cref="Primitives"/>).
/// </summary>
internal readonly struct DefaultLess<T> : ILess<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Less(T x, T y)
    {
        if (typeof(T) == typeof(int)) return Unsafe.As<T, int>(ref x) < Unsafe.As<T, int>(ref y);
        if (typeof(T) == typeof(uint)) return Unsafe.As<T, uint>(ref x) < Unsafe.As<T, uint>(ref y);
        if (typeof(T) == typeof(long)) return Unsafe.As<T, long>(ref x) < Unsafe.As<T, long>(ref y);
        if (typeof(T) == typeof(ulong)) return Unsafe.As<T, ulong>(ref x) < Unsafe.As<T, ulong>(ref y);
        if (typeof(T) == typeof(short)) return Unsafe.As<T, short>(ref x) < Unsafe.As<T, short>(ref y);
        if (typeof(T) == typeof(ushort)) return Unsafe.As<T, ushort>(ref x) < Unsafe.As<T, ushort>(ref y);
        if (typeof(T) == typeof(char)) return Unsafe.As<T, char>(ref x) < Unsafe.As<T, char>(ref y);
        if (typeof(T) == typeof(byte)) return Unsafe.As<T, byte>(ref x) < Unsafe.As<T, byte>(ref y);
        if (typeof(T) == typeof(sbyte)) return Unsafe.As<T, sbyte>(ref x) < Unsafe.As<T, sbyte>(ref y);
        if (typeof(T) == typeof(nint)) return Unsafe.As<T, nint>(ref x) < Unsafe.As<T, nint>(ref y);
        if (typeof(T) == typeof(nuint)) return Unsafe.As<T, nuint>(ref x) < Unsafe.As<T, nuint>(ref y);
        if (typeof(T) == typeof(float)) return Unsafe.As<T, float>(ref x) < Unsafe.As<T, float>(ref y);
        if (typeof(T) == typeof(double)) return Unsafe.As<T, double>(ref x) < Unsafe.As<T, double>(ref y);
        return Comparer<T>.Default.Compare(x, y) < 0;
    }
}

internal readonly struct ComparerLess<T, TComparer>(TComparer comparer) : ILess<T>
    where TComparer : IComparer<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Less(T x, T y) => comparer.Compare(x, y) < 0;
}

internal readonly struct ComparisonLess<T>(Comparison<T> comparison) : ILess<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Less(T x, T y) => comparison(x, y) < 0;
}

/// <summary>Orders key/item pairs by key only.</summary>
internal readonly struct KeyLess<TKey, TValue, TLess>(TLess less) : ILess<KeyValuePair<TKey, TValue>>
    where TLess : ILess<TKey>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Less(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y) => less.Less(x.Key, y.Key);
}
