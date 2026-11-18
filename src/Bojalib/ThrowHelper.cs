using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Bojalib;

// Throwing from a separate non-inlined method keeps the hot paths small enough to inline.
internal static class ThrowHelper
{
    /// <summary>
    /// Null check for generic values that never boxes: <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>
    /// boxes value types in Debug builds, which would break the allocation-free guarantees there.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNull<T>(T value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (!typeof(T).IsValueType && value is null)
        {
            ThrowArgumentNull(name);
        }
    }

    [DoesNotReturn]
    private static void ThrowArgumentNull(string? name) => throw new ArgumentNullException(name);

    [DoesNotReturn]
    public static void ThrowEmptyCollection(string name) =>
        throw new InvalidOperationException($"The {name} is empty.");

    [DoesNotReturn]
    public static void ThrowCollectionModified() =>
        throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

    [DoesNotReturn]
    public static void ThrowEnumeratorNotStarted() =>
        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
}
