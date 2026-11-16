using System.Buffers;
using System.Collections;

namespace Bojalib.Collections;

// Struct enumerables: `foreach` over them allocates nothing. They also implement IEnumerable<T> so LINQ works
// (which boxes them, as with any struct enumerable).
public sealed partial class TreeNode<T>
{
    /// <summary>Pre-order traversal of a subtree. See <see cref="DepthFirst"/>.</summary>
    public readonly struct DepthFirstEnumerable : IEnumerable<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;

        internal DepthFirstEnumerable(TreeNode<T> root) => _root = root;

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public DepthFirstEnumerator GetEnumerator() => new(_root);

        IEnumerator<TreeNode<T>> IEnumerable<TreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="DepthFirstEnumerable"/>.</summary>
    public struct DepthFirstEnumerator : IEnumerator<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;
        private TreeNode<T>? _current;
        private bool _started;

        internal DepthFirstEnumerator(TreeNode<T> root)
        {
            _root = root;
            _current = null;
            _started = false;
        }

        /// <inheritdoc />
        public readonly TreeNode<T> Current => _current!;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                _current = _root;
                return true;
            }

            if (_current is null)
            {
                return false;
            }

            _current = _current._children.Count > 0 ? _current._children[0] : NextSkippingChildren(_current, _root);
            return _current is not null;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _current = null;
            _started = false;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    /// <summary>Post-order traversal of a subtree. See <see cref="PostOrder"/>.</summary>
    public readonly struct PostOrderEnumerable : IEnumerable<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;

        internal PostOrderEnumerable(TreeNode<T> root) => _root = root;

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public PostOrderEnumerator GetEnumerator() => new(_root);

        IEnumerator<TreeNode<T>> IEnumerable<TreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="PostOrderEnumerable"/>.</summary>
    public struct PostOrderEnumerator : IEnumerator<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;
        private TreeNode<T>? _current;
        private bool _started;

        internal PostOrderEnumerator(TreeNode<T> root)
        {
            _root = root;
            _current = null;
            _started = false;
        }

        /// <inheritdoc />
        public readonly TreeNode<T> Current => _current!;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                _current = LeftmostLeaf(_root);
                return true;
            }

            if (_current is null || _current == _root)
            {
                _current = null;
                return false;
            }

            // After a node come its next sibling's subtree (leftmost leaf first), or else its parent.
            TreeNode<T> parent = _current.Parent!;
            int next = _current._indexInParent + 1;
            _current = next < parent._children.Count ? LeftmostLeaf(parent._children[next]) : parent;
            return true;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _current = null;
            _started = false;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    /// <summary>The leaves of a subtree, left to right. See <see cref="Leaves"/>.</summary>
    public readonly struct LeavesEnumerable : IEnumerable<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;

        internal LeavesEnumerable(TreeNode<T> root) => _root = root;

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public LeavesEnumerator GetEnumerator() => new(_root);

        IEnumerator<TreeNode<T>> IEnumerable<TreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="LeavesEnumerable"/>.</summary>
    public struct LeavesEnumerator : IEnumerator<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;
        private TreeNode<T>? _current;
        private bool _started;

        internal LeavesEnumerator(TreeNode<T> root)
        {
            _root = root;
            _current = null;
            _started = false;
        }

        /// <inheritdoc />
        public readonly TreeNode<T> Current => _current!;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                _current = LeftmostLeaf(_root);
                return true;
            }

            if (_current is null)
            {
                return false;
            }

            TreeNode<T>? next = NextSkippingChildren(_current, _root);
            _current = next is null ? null : LeftmostLeaf(next);
            return _current is not null;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _current = null;
            _started = false;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    /// <summary>A node's ancestors, nearest first. See <see cref="Ancestors"/>.</summary>
    public readonly struct AncestorsEnumerable : IEnumerable<TreeNode<T>>
    {
        private readonly TreeNode<T> _node;

        internal AncestorsEnumerable(TreeNode<T> node) => _node = node;

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public AncestorsEnumerator GetEnumerator() => new(_node);

        IEnumerator<TreeNode<T>> IEnumerable<TreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="AncestorsEnumerable"/>.</summary>
    public struct AncestorsEnumerator : IEnumerator<TreeNode<T>>
    {
        private readonly TreeNode<T> _node;
        private TreeNode<T>? _current;

        internal AncestorsEnumerator(TreeNode<T> node)
        {
            _node = node;
            _current = node;
        }

        /// <inheritdoc />
        public readonly TreeNode<T> Current => _current!;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            _current = _current?.Parent;
            return _current is not null;
        }

        /// <inheritdoc />
        public void Reset() => _current = _node;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    /// <summary>Level-order traversal of a subtree. See <see cref="BreadthFirst"/>.</summary>
    public readonly struct BreadthFirstEnumerable : IEnumerable<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;

        internal BreadthFirstEnumerable(TreeNode<T> root) => _root = root;

        /// <summary>Returns the enumerator. Dispose it (as <c>foreach</c> does) to return its queue to the pool.</summary>
        /// <returns>The enumerator.</returns>
        public BreadthFirstEnumerator GetEnumerator() => new(_root);

        IEnumerator<TreeNode<T>> IEnumerable<TreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerator for <see cref="BreadthFirstEnumerable"/>; its queue is a pooled ring buffer.</summary>
    public struct BreadthFirstEnumerator : IEnumerator<TreeNode<T>>
    {
        private readonly TreeNode<T> _root;
        private TreeNode<T>[]? _queue;
        private int _head;
        private int _count;
        private TreeNode<T>? _current;
        private bool _finished;

        internal BreadthFirstEnumerator(TreeNode<T> root)
        {
            _root = root;
            _queue = null;
            _head = 0;
            _count = 0;
            _current = null;
            _finished = false;
        }

        /// <inheritdoc />
        public readonly TreeNode<T> Current => _current!;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_finished)
            {
                return false;
            }

            if (_queue is null)
            {
                _queue = ArrayPool<TreeNode<T>>.Shared.Rent(16);
                Enqueue(_root);
            }

            if (_count == 0)
            {
                _finished = true;
                _current = null;
                Dispose(); // Hand the queue back as soon as possible.
                return false;
            }

            _current = _queue[_head];
            _head = (_head + 1) % _queue.Length;
            _count--;
            foreach (TreeNode<T> child in _current._children)
            {
                Enqueue(child);
            }

            return true;
        }

        /// <inheritdoc />
        public void Reset()
        {
            Dispose();
            _current = null;
            _finished = false;
        }

        /// <summary>Returns the queue to the pool.</summary>
        public void Dispose()
        {
            if (_queue is not null)
            {
                ArrayPool<TreeNode<T>>.Shared.Return(_queue, clearArray: true);
                _queue = null;
            }

            _head = 0;
            _count = 0;
        }

        private void Enqueue(TreeNode<T> node)
        {
            if (_count == _queue!.Length)
            {
                TreeNode<T>[] bigger = ArrayPool<TreeNode<T>>.Shared.Rent(_queue.Length * 2);
                for (int i = 0; i < _count; i++)
                {
                    bigger[i] = _queue[(_head + i) % _queue.Length];
                }

                ArrayPool<TreeNode<T>>.Shared.Return(_queue, clearArray: true);
                _queue = bigger;
                _head = 0;
            }

            _queue[(_head + _count) % _queue.Length] = node;
            _count++;
        }
    }
}
