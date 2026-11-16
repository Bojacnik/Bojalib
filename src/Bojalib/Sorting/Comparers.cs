namespace Bojalib.Sorting;

// Struct comparers let the JIT specialise the sort loops per comparer and inline Compare,
// instead of making an interface call for every comparison.

internal readonly struct DefaultComparer<T> : IComparer<T>
{
    public int Compare(T? x, T? y) => Comparer<T>.Default.Compare(x!, y!);
}

internal readonly struct WrappedComparer<T>(IComparer<T> comparer) : IComparer<T>
{
    public int Compare(T? x, T? y) => comparer.Compare(x, y);
}

internal readonly struct ComparisonComparer<T>(Comparison<T> comparison) : IComparer<T>
{
    public int Compare(T? x, T? y) => comparison(x!, y!);
}
