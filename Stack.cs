using System;

namespace Bojalib
{
    namespace Collections
    {
        public class Stack<T>
        {
            Data head;

            private class Data
            {
                public Data behind;
                public T data;

                public Data(T data, Data behind = null)
                {
                    this.data = data;
                    this.behind = behind;
                }
            }

            public Stack()
            {

            }

            public void Push(T data)
            {
                head = new Data(data, head);
            }

            public T Pop()
            {
                if (head == null) throw new Exception("Stack is empty!"); //Scenario 1

                Data tmp = head; //Scenario 2
                head = tmp.behind;
                return tmp.data;
            }

        }
    }
}
