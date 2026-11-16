using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>Enables collection expressions for <see cref="LinkedStack{T}"/>: items are pushed in order, so the last item ends up on top.</summary>
public static class LinkedStack
{
    /// <summary>Creates a linked stack from <paramref name="items"/>; items are pushed in order, so the last item ends up on top.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The new collection.</returns>
    public static LinkedStack<T> Create<T>(ReadOnlySpan<T> items)
    {
        var collection = new LinkedStack<T>();
        foreach (T item in items)
        {
            collection.Push(item);
        }

        return collection;
    }
}

/// <summary>
/// Last-in-first-out collection backed by a singly linked list. Every operation is O(1) and never copies,
/// unlike <see cref="Stack{T}"/> which occasionally resizes its array.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[CollectionBuilder(typeof(LinkedStack), nameof(LinkedStack.Create))]
public sealed class LinkedStack<T> : IReadOnlyCollection<T>
{
    private Node? _head;
    private int _count;
    private int _version;

    /// <summary>Gets the number of elements in the stack.</summary>
    public int Count => _count;

    /// <summary>Gets whether the stack has no elements.</summary>
    public bool IsEmpty => _head is null;

    /// <summary>Adds <paramref name="item"/> to the top of the stack.</summary>
    /// <param name="item">The item to add.</param>
    public void Push(T item)
    {
        _head = new Node(item, _head);
        _count++;
        _version++;
    }

    /// <summary>Removes and returns the element at the top of the stack.</summary>
    /// <returns>The removed element.</returns>
    /// <exception cref="InvalidOperationException">The stack is empty.</exception>
    public T Pop()
    {
        if (!TryPop(out T? item))
        {
            ThrowHelper.ThrowEmptyCollection("stack");
        }

        return item;
    }

    /// <summary>Removes the element at the top of the stack, if there is one.</summary>
    /// <param name="item">The removed element, or <c>default</c> if the stack was empty.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryPop([MaybeNullWhen(false)] out T item)
    {
        Node? head = _head;
        if (head is null)
        {
            item = default;
            return false;
        }

        item = head.Value;
        _head = head.Next;
        _count--;
        _version++;
        return true;
    }

    /// <summary>Returns the element at the top of the stack without removing it.</summary>
    /// <returns>The top element.</returns>
    /// <exception cref="InvalidOperationException">The stack is empty.</exception>
    public T Peek()
    {
        if (_head is null)
        {
            ThrowHelper.ThrowEmptyCollection("stack");
        }

        return _head.Value;
    }

    /// <summary>Gets the element at the top of the stack without removing it, if there is one.</summary>
    /// <param name="item">The top element, or <c>default</c> if the stack is empty.</param>
    /// <returns><see langword="true"/> if the stack has an element.</returns>
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
        _count = 0;
        _version++;
    }

    /// <summary>Returns an enumerator that walks the stack from top to bottom.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates a <see cref="LinkedStack{T}"/> from top to bottom.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly LinkedStack<T> _stack;
        private readonly int _version;
        private Node? _next;
        private Node? _current;

        internal Enumerator(LinkedStack<T> stack)
        {
            _stack = stack;
            _version = stack._version;
            _next = stack._head;
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
            if (_version != _stack._version)
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
            if (_version != _stack._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            _next = _stack._head;
            _current = null;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    private sealed class Node(T value, Node? next)
    {
        public readonly T Value = value;
        public readonly Node? Next = next;
    }
}
