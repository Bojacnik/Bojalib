using Bojalib.Core.Collections.Generic.Exceptions;

namespace Bojalib.Core.Collections.Generic;

public class Stack<TData>
{
    private class StackItem
    {
        public readonly StackItem? Behind;
        public readonly TData Value;

        public StackItem(TData value, StackItem? behind = null)
        {
            Value = value;
            Behind = behind;
        }
    }

    private StackItem? _head;


    public void Push(TData data)
    {
        if (_head != null) 
            _head = new StackItem(data, _head);
    }

    public TData Pop()
    {
        if (_head == null) throw new StackEmptyException();

        StackItem tmp = _head;
        _head = tmp.Behind;
        return tmp.Value;
    }
}