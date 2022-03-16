using Bojalib.Collections;
using System;

namespace Bojalib
{
    namespace Sorting
    {
        public static class Dfs<T>
        {
            public static void Traverse(Graph<T> g)
            {
                if (g.Root != null) traverse(g.Root);
            }

            private static void traverse(Node<T> n)
            {
                Console.WriteLine(n.Data);
                foreach (Node<T> node in n.Children)
                    traverse(node);
            }
        }
    }
}
