using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bojalib.Collections;

/// <summary>
/// Union-find over the integers 0 .. Count-1: merges sets and answers "are these connected?" in effectively O(1)
/// (inverse-Ackermann amortized) time, using union by size and path halving. Two <see langword="int"/> arrays, no
/// per-element objects.
/// </summary>
[DebuggerDisplay("Count = {Count}, SetCount = {SetCount}")]
public sealed class DisjointSet
{
    private int[] _parent;
    private int[] _size;
    private int _count;

    /// <summary>Creates <paramref name="count"/> singleton sets {0}, {1}, …</summary>
    /// <param name="count">The number of elements.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public DisjointSet(int count = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _parent = new int[Math.Max(count, 4)];
        _size = new int[_parent.Length];
        for (int i = 0; i < count; i++)
        {
            _parent[i] = i;
            _size[i] = 1;
        }

        _count = count;
        SetCount = count;
    }

    /// <summary>Gets the number of elements.</summary>
    public int Count => _count;

    /// <summary>Gets the number of disjoint sets.</summary>
    public int SetCount { get; private set; }

    /// <summary>Adds a new element in its own set.</summary>
    /// <returns>The new element's number (the previous <see cref="Count"/>).</returns>
    public int Add()
    {
        if (_count == _parent.Length)
        {
            Array.Resize(ref _parent, _parent.Length * 2);
            Array.Resize(ref _size, _size.Length * 2);
        }

        _parent[_count] = _count;
        _size[_count] = 1;
        SetCount++;
        return _count++;
    }

    /// <summary>Returns the representative of <paramref name="element"/>'s set; elements in the same set share it.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The representative element.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="element"/> is not in [0, Count).</exception>
    public int Find(int element)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)element, (uint)_count, nameof(element));
        Span<int> parent = _parent.AsSpan(0, _count);

        // Path halving: point every other node on the path at its grandparent. One pass, no recursion.
        while (parent[element] != element)
        {
            parent[element] = parent[parent[element]];
            element = parent[element];
        }

        return element;
    }

    /// <summary>Merges the sets containing <paramref name="a"/> and <paramref name="b"/>.</summary>
    /// <param name="a">An element.</param>
    /// <param name="b">Another element.</param>
    /// <returns><see langword="true"/> if they were in different sets.</returns>
    public bool Union(int a, int b)
    {
        int rootA = Find(a);
        int rootB = Find(b);
        if (rootA == rootB)
        {
            return false;
        }

        // Attach the smaller tree under the larger one, keeping trees shallow.
        if (_size[rootA] < _size[rootB])
        {
            (rootA, rootB) = (rootB, rootA);
        }

        _parent[rootB] = rootA;
        _size[rootA] += _size[rootB];
        SetCount--;
        return true;
    }

    /// <summary>Returns whether <paramref name="a"/> and <paramref name="b"/> are in the same set.</summary>
    /// <param name="a">An element.</param>
    /// <param name="b">Another element.</param>
    /// <returns><see langword="true"/> if connected.</returns>
    public bool AreConnected(int a, int b) => Find(a) == Find(b);

    /// <summary>Returns the number of elements in <paramref name="element"/>'s set.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The set's size.</returns>
    public int SizeOf(int element) => _size[Find(element)];
}

/// <summary>
/// Union-find over arbitrary values: a <see cref="DisjointSet"/> plus a value → index map. Values are added
/// automatically the first time they're used in <see cref="Union"/>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}, SetCount = {SetCount}")]
public sealed class DisjointSet<T>
    where T : notnull
{
    private readonly DisjointSet _sets = new();
    private readonly Dictionary<T, int> _indices;
    private readonly List<T> _values = [];

    /// <summary>Creates an empty structure.</summary>
    /// <param name="comparer">Compares elements; <see langword="null"/> for the default.</param>
    public DisjointSet(IEqualityComparer<T>? comparer = null) => _indices = new Dictionary<T, int>(comparer);

    /// <summary>Gets the number of elements.</summary>
    public int Count => _sets.Count;

    /// <summary>Gets the number of disjoint sets.</summary>
    public int SetCount => _sets.SetCount;

    /// <summary>Adds <paramref name="item"/> in its own set, if it isn't present.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if added.</returns>
    public bool Add(T item)
    {
        ref int index = ref CollectionsMarshal.GetValueRefOrAddDefault(_indices, item, out bool exists);
        if (exists)
        {
            return false;
        }

        index = _sets.Add();
        _values.Add(item);
        return true;
    }

    /// <summary>Returns whether <paramref name="item"/> has been added.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(T item) => _indices.ContainsKey(item);

    /// <summary>Returns the representative of <paramref name="item"/>'s set.</summary>
    /// <param name="item">The element.</param>
    /// <returns>The representative.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="item"/> hasn't been added.</exception>
    public T Find(T item) => _values[_sets.Find(IndexOf(item))];

    /// <summary>Merges the sets containing <paramref name="a"/> and <paramref name="b"/>, adding either if new.</summary>
    /// <param name="a">An element.</param>
    /// <param name="b">Another element.</param>
    /// <returns><see langword="true"/> if they were in different sets.</returns>
    public bool Union(T a, T b)
    {
        Add(a);
        Add(b);
        return _sets.Union(_indices[a], _indices[b]);
    }

    /// <summary>Returns whether <paramref name="a"/> and <paramref name="b"/> are in the same set (false if either is unknown).</summary>
    /// <param name="a">An element.</param>
    /// <param name="b">Another element.</param>
    /// <returns><see langword="true"/> if connected.</returns>
    public bool AreConnected(T a, T b) =>
        _indices.TryGetValue(a, out int ia) && _indices.TryGetValue(b, out int ib) && _sets.AreConnected(ia, ib);

    /// <summary>Returns the number of elements in <paramref name="item"/>'s set.</summary>
    /// <param name="item">The element.</param>
    /// <returns>The set's size.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="item"/> hasn't been added.</exception>
    public int SizeOf(T item) => _sets.SizeOf(IndexOf(item));

    private int IndexOf(T item) =>
        _indices.TryGetValue(item, out int index) ? index : throw new KeyNotFoundException($"'{item}' has not been added.");
}
