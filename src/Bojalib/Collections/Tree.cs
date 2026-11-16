using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>
/// Traversals and queries for trees made of <em>your own</em> node types — UI elements, XML, file systems, ASTs,
/// org charts: anything where each node can list its children. Pass the root and a function returning a node's
/// children.
/// </summary>
/// <remarks>
/// <para>
/// When the children function returns an <see cref="IReadOnlyList{T}"/> (a <see cref="List{T}"/> or array, say),
/// children are read by index with no enumerator allocated; other sequences are enumerated normally. The traversal
/// stack or queue is borrowed from <see cref="ArrayPool{T}"/> and returned when enumeration ends, so walking a tree
/// allocates nothing after warm-up and can't overflow the call stack however deep the tree is.
/// </para>
/// <para>
/// The structure must be a tree: a cycle would make traversal run forever. The children function may return
/// <see langword="null"/> for "no children". Don't modify the tree while traversing it.
/// </para>
/// </remarks>
public static class Tree
{
    /// <summary>Enumerates <paramref name="root"/> and all its descendants depth-first, parents before children (pre-order).</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <returns>The nodes. The enumerator also exposes each node's depth and path from the root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="children"/> is <see langword="null"/>.</exception>
    public static DepthFirstEnumerable<T> DepthFirst<T>(T root, Func<T, IEnumerable<T>?> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new(root, children);
    }

    /// <summary>Enumerates <paramref name="root"/> and all its descendants depth-first, children before parents (post-order).</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <returns>The nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="children"/> is <see langword="null"/>.</exception>
    public static PostOrderEnumerable<T> PostOrder<T>(T root, Func<T, IEnumerable<T>?> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new(root, children, leavesOnly: false);
    }

    /// <summary>Enumerates the leaves (nodes without children) of the tree, left to right.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <returns>The leaves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="children"/> is <see langword="null"/>.</exception>
    public static PostOrderEnumerable<T> Leaves<T>(T root, Func<T, IEnumerable<T>?> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new(root, children, leavesOnly: true);
    }

    /// <summary>Enumerates <paramref name="root"/> and all its descendants level by level (breadth-first).</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <returns>The nodes. The enumerator also exposes each node's depth.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="children"/> is <see langword="null"/>.</exception>
    public static BreadthFirstEnumerable<T> BreadthFirst<T>(T root, Func<T, IEnumerable<T>?> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new(root, children);
    }

    /// <summary>Finds the first node, in depth-first order, that matches <paramref name="match"/>.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <param name="match">The condition.</param>
    /// <param name="found">The matching node.</param>
    /// <returns><see langword="true"/> if a node matched.</returns>
    public static bool TryFind<T>(T root, Func<T, IEnumerable<T>?> children, Predicate<T> match, [MaybeNullWhen(false)] out T found)
    {
        ArgumentNullException.ThrowIfNull(match);
        foreach (T node in DepthFirst(root, children))
        {
            if (match(node))
            {
                found = node;
                return true;
            }
        }

        found = default;
        return false;
    }

    /// <summary>Finds the first node, in depth-first order, that matches <paramref name="match"/>, and the path to it.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <param name="match">The condition.</param>
    /// <param name="path">The nodes from <paramref name="root"/> to the match, inclusive.</param>
    /// <returns><see langword="true"/> if a node matched.</returns>
    public static bool TryFindPath<T>(T root, Func<T, IEnumerable<T>?> children, Predicate<T> match, [MaybeNullWhen(false)] out T[] path)
    {
        ArgumentNullException.ThrowIfNull(match);
        using DepthFirstEnumerator<T> nodes = DepthFirst(root, children).GetEnumerator();
        while (nodes.MoveNext())
        {
            if (match(nodes.Current))
            {
                path = nodes.GetPath();
                return true;
            }
        }

        path = null;
        return false;
    }

    /// <summary>Counts the nodes in the tree, including the root.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <returns>The number of nodes.</returns>
    public static int Count<T>(T root, Func<T, IEnumerable<T>?> children)
    {
        int count = 0;
        foreach (T _ in DepthFirst(root, children))
        {
            count++;
        }

        return count;
    }

    /// <summary>Returns the number of edges on the longest path from the root down to a leaf (0 for a lone root).</summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="children">Returns a node's children (or <see langword="null"/>).</param>
    /// <returns>The height.</returns>
    public static int Height<T>(T root, Func<T, IEnumerable<T>?> children)
    {
        int height = 0;
        using DepthFirstEnumerator<T> nodes = DepthFirst(root, children).GetEnumerator();
        while (nodes.MoveNext())
        {
            height = Math.Max(height, nodes.CurrentDepth);
        }

        return height;
    }

    /// <summary>Pre-order traversal. See <see cref="DepthFirst{T}"/>.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    public readonly struct DepthFirstEnumerable<T> : IEnumerable<T>
    {
        private readonly T _root;
        private readonly Func<T, IEnumerable<T>?> _children;

        internal DepthFirstEnumerable(T root, Func<T, IEnumerable<T>?> children)
        {
            _root = root;
            _children = children;
        }

        /// <summary>Returns the enumerator. Dispose it (as <c>foreach</c> does) to return its stack to the pool.</summary>
        /// <returns>The enumerator.</returns>
        public DepthFirstEnumerator<T> GetEnumerator() => new(_root, _children);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="DepthFirstEnumerable{T}"/>.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    public struct DepthFirstEnumerator<T> : IEnumerator<T>
    {
        private readonly T _root;
        private readonly Func<T, IEnumerable<T>?> _children;
        private FrameStack<T> _stack;
        private T _current;
        private int _depth;
        private int _state; // 0 = not started, 1 = running, 2 = finished

        internal DepthFirstEnumerator(T root, Func<T, IEnumerable<T>?> children)
        {
            _root = root;
            _children = children;
            _stack = default;
            _current = default!;
            _depth = 0;
            _state = 0;
        }

        /// <inheritdoc />
        public readonly T Current => _current;

        /// <summary>Gets the depth of <see cref="Current"/>: 0 for the root, 1 for its children, and so on.</summary>
        public readonly int CurrentDepth => _depth;

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_state == 0)
            {
                _state = 1;
                _stack.Push(_root, _children, 0);
                _current = _root;
                _depth = 0;
                return true;
            }

            while (_state == 1 && _stack.Count > 0)
            {
                if (FrameStack<T>.TryNextChild(ref _stack.Top, out T child))
                {
                    int depth = _stack.Top.Depth + 1;
                    _stack.Push(child, _children, depth);
                    _current = child;
                    _depth = depth;
                    return true;
                }

                _stack.Pop();
            }

            Finish();
            return false;
        }

        /// <summary>Returns the nodes from the root to <see cref="Current"/>, inclusive.</summary>
        /// <returns>A new array.</returns>
        public readonly T[] GetPath() => _stack.Nodes();

        /// <inheritdoc />
        public void Reset()
        {
            _stack.Dispose();
            _state = 0;
        }

        /// <summary>Returns the stack to the pool.</summary>
        public void Dispose() => _stack.Dispose();

        private void Finish()
        {
            _stack.Dispose();
            _state = 2;
            _current = default!;
        }
    }

    /// <summary>Post-order traversal, or only the leaves. See <see cref="PostOrder{T}"/> and <see cref="Leaves{T}"/>.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    public readonly struct PostOrderEnumerable<T> : IEnumerable<T>
    {
        private readonly T _root;
        private readonly Func<T, IEnumerable<T>?> _children;
        private readonly bool _leavesOnly;

        internal PostOrderEnumerable(T root, Func<T, IEnumerable<T>?> children, bool leavesOnly)
        {
            _root = root;
            _children = children;
            _leavesOnly = leavesOnly;
        }

        /// <summary>Returns the enumerator. Dispose it (as <c>foreach</c> does) to return its stack to the pool.</summary>
        /// <returns>The enumerator.</returns>
        public PostOrderEnumerator<T> GetEnumerator() => new(_root, _children, _leavesOnly);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="PostOrderEnumerable{T}"/>.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    public struct PostOrderEnumerator<T> : IEnumerator<T>
    {
        private readonly T _root;
        private readonly Func<T, IEnumerable<T>?> _children;
        private readonly bool _leavesOnly;
        private FrameStack<T> _stack;
        private T _current;
        private int _state;

        internal PostOrderEnumerator(T root, Func<T, IEnumerable<T>?> children, bool leavesOnly)
        {
            _root = root;
            _children = children;
            _leavesOnly = leavesOnly;
            _stack = default;
            _current = default!;
            _state = 0;
        }

        /// <inheritdoc />
        public readonly T Current => _current;

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_state == 0)
            {
                _state = 1;
                _stack.Push(_root, _children, 0);
            }

            while (_state == 1 && _stack.Count > 0)
            {
                // Descend until the top node has no more children, then emit it.
                if (FrameStack<T>.TryNextChild(ref _stack.Top, out T child))
                {
                    _stack.Push(child, _children, _stack.Top.Depth + 1);
                    continue;
                }

                ref Frame<T> top = ref _stack.Top;
                bool emit = !_leavesOnly || !top.HadChild;
                T node = top.Node;
                _stack.Pop();
                if (emit)
                {
                    _current = node;
                    return true;
                }
            }

            _stack.Dispose();
            _state = 2;
            _current = default!;
            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _stack.Dispose();
            _state = 0;
        }

        /// <summary>Returns the stack to the pool.</summary>
        public void Dispose() => _stack.Dispose();
    }

    /// <summary>Level-order traversal. See <see cref="BreadthFirst{T}"/>.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    public readonly struct BreadthFirstEnumerable<T> : IEnumerable<T>
    {
        private readonly T _root;
        private readonly Func<T, IEnumerable<T>?> _children;

        internal BreadthFirstEnumerable(T root, Func<T, IEnumerable<T>?> children)
        {
            _root = root;
            _children = children;
        }

        /// <summary>Returns the enumerator. Dispose it (as <c>foreach</c> does) to return its queue to the pool.</summary>
        /// <returns>The enumerator.</returns>
        public BreadthFirstEnumerator<T> GetEnumerator() => new(_root, _children);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="BreadthFirstEnumerable{T}"/>; its queue is a pooled ring buffer.</summary>
    /// <typeparam name="T">The node type.</typeparam>
    public struct BreadthFirstEnumerator<T> : IEnumerator<T>
    {
        private readonly T _root;
        private readonly Func<T, IEnumerable<T>?> _children;
        private (T Node, int Depth)[]? _queue;
        private int _head;
        private int _count;
        private T _current;
        private int _depth;
        private int _state;

        internal BreadthFirstEnumerator(T root, Func<T, IEnumerable<T>?> children)
        {
            _root = root;
            _children = children;
            _queue = null;
            _head = 0;
            _count = 0;
            _current = default!;
            _depth = 0;
            _state = 0;
        }

        /// <inheritdoc />
        public readonly T Current => _current;

        /// <summary>Gets the depth of <see cref="Current"/>: 0 for the root, 1 for its children, and so on.</summary>
        public readonly int CurrentDepth => _depth;

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_state == 2)
            {
                return false;
            }

            if (_state == 0)
            {
                _state = 1;
                _queue = ArrayPool<(T, int)>.Shared.Rent(16);
                Enqueue(_root, 0);
            }

            if (_count == 0)
            {
                Dispose();
                _state = 2;
                _current = default!;
                return false;
            }

            (_current, _depth) = _queue![_head];
            _queue[_head] = default;
            _head = _head + 1 == _queue.Length ? 0 : _head + 1;
            _count--;

            IEnumerable<T>? children = _children(_current);
            if (children is IReadOnlyList<T> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Enqueue(list[i], _depth + 1);
                }
            }
            else if (children is not null)
            {
                foreach (T child in children)
                {
                    Enqueue(child, _depth + 1);
                }
            }

            return true;
        }

        /// <inheritdoc />
        public void Reset()
        {
            Dispose();
            _state = 0;
        }

        /// <summary>Returns the queue to the pool.</summary>
        public void Dispose()
        {
            if (_queue is not null)
            {
                ArrayPool<(T, int)>.Shared.Return(_queue, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
                _queue = null;
            }

            _head = 0;
            _count = 0;
        }

        private void Enqueue(T node, int depth)
        {
            if (_count == _queue!.Length)
            {
                (T, int)[] bigger = ArrayPool<(T, int)>.Shared.Rent(_queue.Length * 2);
                for (int i = 0; i < _count; i++)
                {
                    int index = _head + i;
                    bigger[i] = _queue[index >= _queue.Length ? index - _queue.Length : index];
                }

                ArrayPool<(T, int)>.Shared.Return(_queue, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
                _queue = bigger;
                _head = 0;
            }

            int tail = _head + _count;
            _queue[tail >= _queue.Length ? tail - _queue.Length : tail] = (node, depth);
            _count++;
        }
    }

    /// <summary>One level of a depth-first walk: a node and how far through its children we are.</summary>
    internal struct Frame<T>
    {
        public T Node;
        public IReadOnlyList<T>? List;
        public IEnumerator<T>? Enumerator;
        public int Index;
        public int Depth;
        public bool HadChild;
    }

    /// <summary>A pooled stack of frames. Lives as a mutable struct field of its enumerator.</summary>
    internal struct FrameStack<T>
    {
        private Frame<T>[]? _items;
        private int _count;

        public readonly int Count => _count;

        public readonly ref Frame<T> Top => ref _items![_count - 1];

        public static bool TryNextChild(ref Frame<T> frame, out T child)
        {
            if (frame.List is not null)
            {
                if (frame.Index < frame.List.Count)
                {
                    child = frame.List[frame.Index++];
                    frame.HadChild = true;
                    return true;
                }
            }
            else if (frame.Enumerator is not null && frame.Enumerator.MoveNext())
            {
                child = frame.Enumerator.Current;
                frame.HadChild = true;
                return true;
            }

            child = default!;
            return false;
        }

        public void Push(T node, Func<T, IEnumerable<T>?> children, int depth)
        {
            if (_items is null)
            {
                _items = ArrayPool<Frame<T>>.Shared.Rent(16);
            }
            else if (_count == _items.Length)
            {
                Frame<T>[] bigger = ArrayPool<Frame<T>>.Shared.Rent(_items.Length * 2);
                Array.Copy(_items, bigger, _count);
                ArrayPool<Frame<T>>.Shared.Return(_items, clearArray: true);
                _items = bigger;
            }

            ref Frame<T> frame = ref _items[_count++];
            frame = default;
            frame.Node = node;
            frame.Depth = depth;
            IEnumerable<T>? childNodes = children(node);
            if (childNodes is IReadOnlyList<T> list)
            {
                frame.List = list;
            }
            else if (childNodes is not null)
            {
                frame.Enumerator = childNodes.GetEnumerator();
            }
        }

        public void Pop()
        {
            ref Frame<T> top = ref _items![--_count];
            top.Enumerator?.Dispose();
            top = default;
        }

        public readonly T[] Nodes()
        {
            var nodes = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                nodes[i] = _items![i].Node;
            }

            return nodes;
        }

        public void Dispose()
        {
            while (_count > 0)
            {
                Pop();
            }

            if (_items is not null)
            {
                ArrayPool<Frame<T>>.Shared.Return(_items, clearArray: true);
                _items = null;
            }
        }
    }
}
