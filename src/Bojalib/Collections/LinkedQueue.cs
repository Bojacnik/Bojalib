using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Bojalib.Collections;

/// <summary>
/// First-in-first-out collection backed by a singly linked list. Every operation is O(1) and never copies,
/// unlike <see cref="Queue{T}"/> which occasionally resizes its array.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class LinkedQueue<T> : IReadOnlyCollection<T>
{
    private Node? _head;
    private Node? _tail;
    private int _count;
    private int _version;

    /// <summary>Gets the number of elements in the queue.</summary>
    public int Count => _count;

    /// <summary>Gets whether the queue has no elements.</summary>
    public bool IsEmpty => _head is null;

    /// <summary>Adds <paramref name="item"/> to the back of the queue.</summary>
    /// <param name="item">The item to add.</param>
    public void Enqueue(T item)
    {
        var node = new Node(item);
        if (_tail is null)
        {
            _head = node;
        }
        else
        {
            _tail.Next = node;
        }

        _tail = node;
        _count++;
        _version++;
    }

    /// <summary>Removes and returns the element at the front of the queue.</summary>
    /// <returns>The removed element.</returns>
    /// <exception cref="InvalidOperationException">The queue is empty.</exception>
    public T Dequeue()
    {
        if (!TryDequeue(out T? item))
        {
            ThrowHelper.ThrowEmptyCollection("queue");
        }

        return item;
    }

    /// <summary>Removes the element at the front of the queue, if there is one.</summary>
    /// <param name="item">The removed element, or <c>default</c> if the queue was empty.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryDequeue([MaybeNullWhen(false)] out T item)
    {
        Node? head = _head;
        if (head is null)
        {
            item = default;
            return false;
        }

        item = head.Value;
        _head = head.Next;
        if (_head is null)
        {
            // Don't keep the last removed element alive through _tail.
            _tail = null;
        }

        _count--;
        _version++;
        return true;
    }

    /// <summary>Returns the element at the front of the queue without removing it.</summary>
    /// <returns>The front element.</returns>
    /// <exception cref="InvalidOperationException">The queue is empty.</exception>
    public T Peek()
    {
        if (_head is null)
        {
            ThrowHelper.ThrowEmptyCollection("queue");
        }

        return _head.Value;
    }

    /// <summary>Gets the element at the front of the queue without removing it, if there is one.</summary>
    /// <param name="item">The front element, or <c>default</c> if the queue is empty.</param>
    /// <returns><see langword="true"/> if the queue has an element.</returns>
    public bool TryPeek([MaybeNullWhen(false)] out T item)
    {
        if (_head is null)
        {
            item = default;
            return false;
        }

        item = _head.Value;
        return true;
    }

    /// <summary>Removes all elements.</summary>
    public void Clear()
    {
        _head = null;
        _tail = null;
        _count = 0;
        _version++;
    }

    /// <summary>Returns an enumerator that walks the queue from front to back.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates a <see cref="LinkedQueue{T}"/> from front to back.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly LinkedQueue<T> _queue;
        private readonly int _version;
        private Node? _next;
        private Node? _current;

        internal Enumerator(LinkedQueue<T> queue)
        {
            _queue = queue;
            _version = queue._version;
            _next = queue._head;
            _current = null;
        }

        /// <inheritdoc />
        public readonly T Current
        {
            get
            {
                if (_current is null)
                {
                    ThrowHelper.ThrowEnumeratorNotStarted();
                }

                return _current.Value;
            }
        }

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _queue._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            _current = _next;
            _next = _next?.Next;
            return _current is not null;
        }

        /// <inheritdoc />
        public void Reset()
        {
            if (_version != _queue._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            _next = _queue._head;
            _current = null;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    private sealed class Node(T value)
    {
        public readonly T Value = value;
        public Node? Next;
    }
}
