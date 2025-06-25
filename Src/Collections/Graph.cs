using System.Collections.Generic;

namespace Bojalib.Collections;

public class Node<T>
{
    public T Data;
    public readonly List<Node<T>> Children;

    public Node(T data, Node<T> child1 = null, Node<T> child2 = null, Node<T> child3 = null)
    {
        this.Data = data;
        this.Children = new List<Node<T>>();
        if (child1 != null)
        {
            Children.Add(child1);
        }

        if (child2 != null)
        {
            Children.Add(child2);
        }

        if (child3 != null)
        {
            Children.Add(child3);
        }
    }
}

public class Graph<T>
{
    public Node<T> Root;
}