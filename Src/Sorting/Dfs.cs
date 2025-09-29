using System.Collections.Generic;
using Bojalib.Collections;

namespace Bojalib.Sorting;

public static class Dfs<T>
{
    public static void Traverse(Node<T> g)
    {
        if (g != null) traverse(g);
    }

    private static List<T> traverse(Node<T> n)
    {
        List<T> output = new();
        foreach (Node<T> node in n.Children)
        {
            traverse(node);
        }

        return output;
    }
}