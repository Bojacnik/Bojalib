using System.Collections;

namespace Bojalib.Core.Collections.Generic;

public class Node<TData> : IEqualityComparer<Node<TData>>, IComparable<Node<TData>>, IEnumerable<Node<TData>>
    where TData : class, IEquatable<TData>, IComparable<TData>
{
    public required TData Data { get; init; }
    public required List<Node<TData>> Children { get; init; }

    public Node()
    {
    }

    public Node(TData data, params Node<TData>[] children)
    {
        Data = data;
        Children = [];
        Children.AddRange(children);
    }

    public bool Equals(Node<TData>? x, Node<TData>? y)
    {
        return x != null && y != null && x.Data.Equals(y.Data) && x.Children.Equals(y.Children);
    }

    public int GetHashCode(Node<TData> obj)
    {
        return HashCode.Combine(obj.Data, obj.Children);
    }

    public int CompareTo(Node<TData>? other)
    {
        return Data.CompareTo(other?.Data);
    }

    public IEnumerator<Node<TData>> GetEnumerator()
    {
        yield return this;
        foreach (Node<TData> child in Children)
        {
            yield return child;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}