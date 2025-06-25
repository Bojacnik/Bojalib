using System;

namespace Bojalib.Collections;

public class Stack<T>
{
    public Stack()
    {
    }

    private class Data
    {
        public readonly Data Behind;
        public readonly T Value;

        public Data(T value, Data behind = null)
        {
            this.Value = value;
            Behind = behind;
        }
    }

    private Data _head;


    public void Push(T data)
    {
        _head = new Data(data, _head);
    }

    public T Pop()
    {
        if (_head == null) throw new Exception("Stack is empty!");

        Data tmp = _head;
        _head = tmp.Behind;
        return tmp.Value;
    }
}