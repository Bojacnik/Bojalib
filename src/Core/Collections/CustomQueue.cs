using System.Collections;
using System.Collections.Generic;
using Bojalib.Collections.Exceptions;

namespace Bojalib.Core.Collections;

public class CustomQueue<TData> : IEnumerable<TData>
{
    private CustomQueueItem _front;
    private CustomQueueItem _end;

    private class CustomQueueItem
    {
        public TData Data;
        public CustomQueueItem Next;
    }

    public TData Dequeue()
    {
        if (_front == null)
        {
            throw new QueueEmptyException();
        }

        TData result = _front.Data;
        _front = _front.Next;
        return result;
    }

    public void Enqueue(TData item)
    {
        CustomQueueItem newItem = new CustomQueueItem
        {
            Data = item
        };

        _front ??= newItem;

        if (_end is not null)
        {
            _end.Next = newItem;
        }

        _end = newItem;
    }

    public bool IsEmpty()
    {
        return _front == null;
    }

    public IEnumerator<TData> GetEnumerator()
    {
        CustomQueueItem current = _front;
        while (current is not null)
        {
            yield return current.Data;
            current = current.Next;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}