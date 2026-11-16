using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Bojalib.Collections;

/// <summary>A node in a general (n-ary) tree. Each node has a value, an ordered list of children and at most one parent.</summary>
/// <remarks>
/// Traversals (<see cref="DepthFirst"/>, <see cref="PostOrder"/>, <see cref="Leaves"/>, <see cref="Ancestors"/>)
/// walk parent links and need no stack, so they allocate nothing and can't overflow on deep trees;
/// <see cref="BreadthFirst"/> borrows its queue from a shared pool. Don't add or remove nodes while a traversal of
/// the same tree is running.
/// </remarks>
/// <typeparam name="T">The value type.</typeparam>
[DebuggerDisplay("Value = {Value}, Children = {Children.Count}")]
public sealed partial class TreeNode<T>
{
    private readonly List<TreeNode<T>> _children = [];
    private ReadOnlyCollection<TreeNode<T>>? _childrenView;

    // Position in Parent._children, so the next sibling is found in O(1) without searching.
    private int _indexInParent;

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

    /// <summary>Gets the root of the tree this node belongs to (this node if it has no parent).</summary>
    public TreeNode<T> Root
    {
        get
        {
            TreeNode<T> node = this;
            while (node.Parent is not null)
            {
                node = node.Parent;
            }

            return node;
        }
    }

    /// <summary>Gets the number of edges from the root to this node (0 for a root).</summary>
    public int Depth
    {
        get
        {
            int depth = 0;
            for (TreeNode<T>? node = Parent; node is not null; node = node.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    /// <summary>Gets the number of edges on the longest path from this node down to a leaf (0 for a leaf).</summary>
    public int Height
    {
        get
        {
            // Same parent-link walk as DepthFirst, tracking depth relative to this node.
            int height = 0;
            int depth = 0;
            TreeNode<T> node = this;
            while (true)
            {
                if (node._children.Count > 0)
                {
                    node = node._children[0];
                    height = Math.Max(height, ++depth);
                    continue;
                }

                while (true)
                {
                    if (node == this)
                    {
                        return height;
                    }

                    TreeNode<T> parent = node.Parent!;
                    int next = node._indexInParent + 1;
                    if (next < parent._children.Count)
                    {
                        node = parent._children[next];
                        break;
                    }

                    node = parent;
                    depth--;
                }
            }
        }
    }

    /// <summary>Creates a child node with <paramref name="value"/> and appends it.</summary>
    /// <param name="value">The child's value.</param>
    /// <returns>The new child.</returns>
    public TreeNode<T> AddChild(T value)
    {
        var child = new TreeNode<T>(value);
        Attach(child);
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

        Attach(child);
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

        _children.RemoveAt(child._indexInParent);
        for (int i = child._indexInParent; i < _children.Count; i++)
        {
            _children[i]._indexInParent = i;
        }

        child.Parent = null;
        child._indexInParent = 0;
        return true;
    }

    /// <summary>Returns the first node in this subtree, in depth-first order, whose value matches <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>The node, or <see langword="null"/> if none matches.</returns>
    public TreeNode<T>? Find(Predicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (TreeNode<T> node in DepthFirst())
        {
            if (predicate(node.Value))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Returns the nodes from the root down to this node, inclusive.</summary>
    /// <returns>A new array; the root is first and this node is last.</returns>
    public TreeNode<T>[] GetPath()
    {
        var path = new TreeNode<T>[Depth + 1];
        TreeNode<T>? node = this;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            path[i] = node!;
            node = node!.Parent;
        }

        return path;
    }

    /// <summary>Enumerates this node and all of its descendants depth-first, parents before children (pre-order).</summary>
    /// <returns>An allocation-free enumerable.</returns>
    public DepthFirstEnumerable DepthFirst() => new(this);

    /// <summary>Enumerates this node and all of its descendants depth-first, children before parents (post-order).</summary>
    /// <returns>An allocation-free enumerable.</returns>
    public PostOrderEnumerable PostOrder() => new(this);

    /// <summary>Enumerates this node and all of its descendants level by level (breadth-first).</summary>
    /// <returns>An enumerable whose queue is borrowed from <see cref="System.Buffers.ArrayPool{T}.Shared"/>.</returns>
    public BreadthFirstEnumerable BreadthFirst() => new(this);

    /// <summary>Enumerates the leaves in this subtree, left to right.</summary>
    /// <returns>An allocation-free enumerable.</returns>
    public LeavesEnumerable Leaves() => new(this);

    /// <summary>Enumerates this node's ancestors, from its parent up to the root.</summary>
    /// <returns>An allocation-free enumerable.</returns>
    public AncestorsEnumerable Ancestors() => new(this);

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

    private void Attach(TreeNode<T> child)
    {
        child.Parent = this;
        child._indexInParent = _children.Count;
        _children.Add(child);
    }

    /// <summary>The node after <paramref name="node"/> in pre-order within <paramref name="root"/>'s subtree, skipping <paramref name="node"/>'s children.</summary>
    private static TreeNode<T>? NextSkippingChildren(TreeNode<T> node, TreeNode<T> root)
    {
        while (node != root)
        {
            TreeNode<T> parent = node.Parent!;
            int next = node._indexInParent + 1;
            if (next < parent._children.Count)
            {
                return parent._children[next];
            }

            node = parent;
        }

        return null;
    }

    private static TreeNode<T> LeftmostLeaf(TreeNode<T> node)
    {
        while (node._children.Count > 0)
        {
            node = node._children[0];
        }

        return node;
    }

    private sealed class StructuralComparer(IEqualityComparer<T> valueComparer) : IEqualityComparer<TreeNode<T>>
    {
        public bool Equals(TreeNode<T>? x, TreeNode<T>? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            // Walk both trees in lockstep; iterative, so deep trees can't overflow the stack.
            TreeNode<T>? a = x;
            TreeNode<T>? b = y;
            while (a is not null && b is not null)
            {
                if (a._children.Count != b._children.Count || !valueComparer.Equals(a.Value, b.Value))
                {
                    return false;
                }

                if (a._children.Count > 0)
                {
                    a = a._children[0];
                    b = b._children[0];
                }
                else
                {
                    // Same shape so far, so both reach the same position.
                    a = NextSkippingChildren(a, x);
                    b = NextSkippingChildren(b, y);
                }
            }

            return a is null && b is null;
        }

        public int GetHashCode(TreeNode<T> obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return HashCode.Combine(obj.Value is null ? 0 : valueComparer.GetHashCode(obj.Value), obj._children.Count);
        }
    }
}
