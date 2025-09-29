using System;

namespace Bojalib.Collections
{
    namespace Collections
    {
        public class LinkedQueue<T>
        {
            private Member _front;
            private Member _end;

            private class Member
            {
                public T Data;
                public Member Left;
                public Member Right;
            }

            public T Pop()
            {
                if (_end == null)
                {
                    throw new NullReferenceException("Queue is empty!");
                }

                if (_front == _end)
                {
                    Member temp = _end;
                    _end = null;
                    _front = null;
                    return temp.Data;
                }
                else
                {
                    Member temp = _end;
                    _end = temp.Left;
                    _end.Right = null;
                    return temp.Data;
                }
            }

            public void Push(T neco)
            {
                Member tmp = new()
                {
                    Data = neco,
                    Left = null,
                    Right = _front
                };

                if (_front == null)
                {
                    _end = tmp;
                }
                else
                {
                    _front.Left = tmp;
                }

                _front = tmp;
            }

            public bool IsEmpty()
            {
                return _front == null;
            }
        }
    }
}