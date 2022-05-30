using System.Collections.Generic;

namespace Bojalib
{
    namespace Collections
    {
        public class Node<T>
        {
            public T Data;
            public List<Node<T>> Children;

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

        public class IntGraph : Graph<int>
        {
            public IntGraph(Node<int> Root)
            {
                this.Root = Root;
            }

            public IntGraph CreateSomeGraph()
            {
                Node<int> testNode = new Node<int>(10,
                    new Node<int>(3),
                    new Node<int>(3,
                        new Node<int>(4),
                        new Node<int>(5),
                        new Node<int>(6)),
                    new Node<int>(12,
                        new Node<int>(5)));

                return new IntGraph(testNode);
            }
        }
    }
}
