using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Bojalib.Collections;

/// <summary>A node in a general (n-ary) tree. Each node has a value, an ordered list of children and at most one parent.</summary>
/// <typeparam name="T">The value type.</typeparam>
[DebuggerDisplay("Value = {Value}, Children = {Children.Count}")]
public sealed class TreeNode<T>
{
    private readonly List<TreeNode<T>> _children = [];
    private ReadOnlyCollection<TreeNode<T>>? _childrenView;

    /// <summary>Creates a node with <paramref name="value"/> and the given children.</summary>
    /// <param name="value">The node's value.</param>
    /// <param name="children">Children to attach. They must not already have a parent.</param>
    /// <exception cref="ArgumentException">A child already has a parent, or would create a cycle.</exception>
    public TreeNode(T value, params ReadOnlySpan<TreeNode<T>> children)
    {
        Value = value;
        foreach (TreeNode<T> child in children)
        {
            AddChild(child);
        }
    }

    /// <summary>Gets or sets the node's value.</summary>
    public T Value { get; set; }

    /// <summary>Gets the parent node, or <see langword="null"/> for a root.</summary>
    public TreeNode<T>? Parent { get; private set; }

    /// <summary>Gets the children, in order. Use <see cref="AddChild(TreeNode{T})"/> and <see cref="RemoveChild"/> to change them.</summary>
    public IReadOnlyList<TreeNode<T>> Children => _childrenView ??= _children.AsReadOnly();

    /// <summary>Gets whether this node has no parent.</summary>
    public bool IsRoot => Parent is null;

    /// <summary>Gets whether this node has no children.</summary>
    public bool IsLeaf => _children.Count == 0;

    /// <summary>Creates a child node with <paramref name="value"/> and appends it.</summary>
    /// <param name="value">The child's value.</param>
    /// <returns>The new child.</returns>
    public TreeNode<T> AddChild(T value)
    {
        var child = new TreeNode<T>(value) { Parent = this };
        _children.Add(child);
        return child;
    }

    /// <summary>Appends <paramref name="child"/> as the last child.</summary>
    /// <param name="child">A node without a parent. It must not be this node or one of its ancestors.</param>
    /// <returns><paramref name="child"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="child"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="child"/> already has a parent, or would create a cycle.</exception>
    public TreeNode<T> AddChild(TreeNode<T> child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new ArgumentException("The node already has a parent. Remove it from that parent first.", nameof(child));
        }

        for (TreeNode<T>? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new ArgumentException("Adding the node would create a cycle.", nameof(child));
            }
        }

        child.Parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>Detaches <paramref name="child"/> from this node.</summary>
    /// <param name="child">The child to remove.</param>
    /// <returns><see langword="true"/> if it was a child of this node and was removed.</returns>
    public bool RemoveChild(TreeNode<T> child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.Parent, this))
        {
            return false;
        }

        _children.Remove(child);
        child.Parent = null;
        return true;
    }

    /// <summary>Enumerates this node and all of its descendants depth-first, parents before children (pre-order).</summary>
    /// <returns>The nodes. Enumeration is lazy.</returns>
    public IEnumerable<TreeNode<T>> DepthFirst()
    {
        var pending = new Stack<TreeNode<T>>();
        pending.Push(this);
        while (pending.TryPop(out TreeNode<T>? node))
        {
            yield return node;
            for (int i = node._children.Count - 1; i >= 0; i--)
            {
                pending.Push(node._children[i]);
            }
        }
    }

    /// <summary>Enumerates this node and all of its descendants level by level (breadth-first).</summary>
    /// <returns>The nodes. Enumeration is lazy.</returns>
    public IEnumerable<TreeNode<T>> BreadthFirst()
    {
        var pending = new Queue<TreeNode<T>>();
        pending.Enqueue(this);
        while (pending.TryDequeue(out TreeNode<T>? node))
        {
            yield return node;
            foreach (TreeNode<T> child in node._children)
            {
                pending.Enqueue(child);
            }
        }
    }

    /// <summary>Enumerates this node's ancestors, from its parent up to the root.</summary>
    /// <returns>The ancestors. Enumeration is lazy.</returns>
    public IEnumerable<TreeNode<T>> Ancestors()
    {
        for (TreeNode<T>? node = Parent; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    /// <summary>
    /// Creates a comparer that treats two subtrees as equal when they have equal values and equal children in the
    /// same order, recursively. Nodes themselves use reference equality, because they are mutable.
    /// </summary>
    /// <param name="valueComparer">Compares values; <see langword="null"/> for <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <returns>The comparer.</returns>
#pragma warning disable CA1000 // Static factory on a generic type is the intended API shape here (like Comparer<T>.Create).
    public static IEqualityComparer<TreeNode<T>> CreateStructuralComparer(IEqualityComparer<T>? valueComparer = null) =>
        new StructuralComparer(valueComparer ?? EqualityComparer<T>.Default);
#pragma warning restore CA1000

    private sealed class StructuralComparer(IEqualityComparer<T> valueComparer) : IEqualityComparer<TreeNode<T>>
    {
        public bool Equals(TreeNode<T>? x, TreeNode<T>? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            // Iterative so deep trees can't overflow the stack.
            var pending = new Stack<(TreeNode<T>, TreeNode<T>)>();
            pending.Push((x, y));
            while (pending.TryPop(out (TreeNode<T> A, TreeNode<T> B) pair))
            {
                if (ReferenceEquals(pair.A, pair.B))
                {
                    continue;
                }

                if (pair.A._children.Count != pair.B._children.Count || !valueComparer.Equals(pair.A.Value, pair.B.Value))
                {
                    return false;
                }

                for (int i = 0; i < pair.A._children.Count; i++)
                {
                    pending.Push((pair.A._children[i], pair.B._children[i]));
                }
            }

            return true;
        }

        public int GetHashCode(TreeNode<T> obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return HashCode.Combine(obj.Value is null ? 0 : valueComparer.GetHashCode(obj.Value), obj._children.Count);
        }
    }
}
