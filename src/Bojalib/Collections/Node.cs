using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Bojalib.Collections;

public class Node<TData> : IEqualityComparer<Node<TData>>, IComparable<Node<TData>>, IEnumerable<Node<TData>>
    where TData : class, IEquatable<TData>, IComparable<TData>
{
    public required TData Data { get; init; }
    public required List<Node<TData>> Children { get; init; }

    public Node()
    {
    }

    [SetsRequiredMembers]
    public Node(TData data, params Node<TData>[] children)
    {
        Data = data;
        Children = [];
        Children.AddRange(children);
    }

    /// <summary>Structural equality: same data and equal children, in order, recursively.</summary>
    public bool Equals(Node<TData>? x, Node<TData>? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        return x is not null && y is not null
            && x.Data.Equals(y.Data)
            && x.Children.SequenceEqual(y.Children, this);
    }

    public int GetHashCode(Node<TData> obj)
    {
        return HashCode.Combine(obj.Data, obj.Children.Count);
    }

    public int CompareTo(Node<TData>? other)
    {
        return Data.CompareTo(other?.Data);
    }

    /// <summary>Enumerates this node and all of its descendants, depth-first (pre-order).</summary>
    public IEnumerator<Node<TData>> GetEnumerator()
    {
        var pending = new Stack<Node<TData>>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            Node<TData> node = pending.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(node.Children[i]);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}