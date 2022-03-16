using System;

namespace Bojalib
{
    namespace Collections
    {
        public class Queue<T>
        {
            private Member Front;
            private Member End;

            class Member
            {
                public T Data;
                public Member Left;
                public Member Right;
            }

            public T Pop()
            {
                if (End == null)
                {
                    throw new NullReferenceException("Queue is empty!");
                }
                else if (Front == End)
                {

                    Member temp = End;
                    End = null;
                    Front = null;
                    return temp.Data;
                }
                else
                {
                    Member temp = End;
                    End = temp.Left;
                    End.Right = null;
                    return temp.Data;
                }
            }

            public void Push(T neco)
            {
                Member tmp = new Member();
                tmp.Data = neco;
                tmp.Left = null;
                tmp.Right = this.Front;

                if (Front == null)
                {
                    End = tmp;
                }
                else
                {
                    Front.Left = tmp;
                }
                Front = tmp;

            }

            public bool IsEmpty()
            {
                if (this.Front == null)
                {
                    return true;
                }
                else
                    return false;
            }
        }
    }
}
