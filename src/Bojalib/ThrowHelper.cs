using System.Diagnostics.CodeAnalysis;

namespace Bojalib;

// Throwing from a separate non-inlined method keeps the hot paths small enough to inline.
internal static class ThrowHelper
{
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
