using System.Collections.Generic;
using Bojalib.Collections;

namespace Bojalib.Sorting;

public static class Dfs<T>
{
    public static void Traverse(Graph<T> g)
    {
        if (g.Root != null) traverse(g.Root);
    }

    private static List<T> traverse(Node<T> n)
    {
        List<T> output = new List<T>();
        foreach (Node<T> node in n.Children)
        {
            traverse(node);
        }

        return output;
    }
}