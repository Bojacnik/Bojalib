using Bojalib.Collections;

namespace Bojalib.Tests.Collections;

public class LinkedQueueTests
{
    [Test]
    public void Dequeue_AfterEnqueues_ReturnsItemsInFifoOrder()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        queue.Dequeue().Should().Be(1);
        queue.Dequeue().Should().Be(2);
        queue.Dequeue().Should().Be(3);
        queue.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Count_TracksEnqueuesAndDequeues()
    {
        var queue = new LinkedQueue<string>();
        queue.Count.Should().Be(0);

        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Count.Should().Be(2);

        queue.Dequeue();
        queue.Count.Should().Be(1);
    }

    [Test]
    public void Peek_ReturnsFrontWithoutRemoving()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);

        queue.Peek().Should().Be(1);
        queue.Count.Should().Be(2);
    }

    [Test]
    public void TryDequeueAndTryPeek_Empty_ReturnFalse()
    {
        var queue = new LinkedQueue<int>();

        queue.TryDequeue(out _).Should().BeFalse();
        queue.TryPeek(out _).Should().BeFalse();
    }

    [Test]
    public void TryDequeueAndTryPeek_NonEmpty_ReturnFront()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(5);

        queue.TryPeek(out int peeked).Should().BeTrue();
        peeked.Should().Be(5);
        queue.TryDequeue(out int popped).Should().BeTrue();
        popped.Should().Be(5);
    }

    [Test]
    public void PopAndPeek_Empty_ThrowInvalidOperation()
    {
        var queue = new LinkedQueue<int>();

        queue.Invoking(q => q.Dequeue()).Should().Throw<InvalidOperationException>();
        queue.Invoking(q => q.Peek()).Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);

        queue.Clear();

        queue.Should().BeEmpty();
        queue.Count.Should().Be(0);
    }

    [Test]
    public void Enumerate_YieldsFrontToBack()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        queue.Should().Equal(1, 2, 3);
    }

    [Test]
    public void Enumerate_ModifiedDuringEnumeration_Throws()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);

        Action act = () =>
        {
            foreach (int item in queue)
            {
                queue.Enqueue(item);
            }
        };

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Enumerator_CurrentBeforeMoveNext_Throws()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        LinkedQueue<int>.Enumerator enumerator = queue.GetEnumerator();

        Action act = () => _ = enumerator.Current;

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Enumerator_Reset_StartsOver()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        System.Collections.IEnumerator enumerator = ((System.Collections.IEnumerable)queue).GetEnumerator();
        enumerator.MoveNext();
        enumerator.MoveNext();

        enumerator.Reset();

        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(1);
    }

    [Test]
    public void Enumerator_ResetAfterModification_Throws()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        LinkedQueue<int>.Enumerator enumerator = queue.GetEnumerator();
        queue.Enqueue(2);

        Action act = () => enumerator.Reset();

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Enqueue_AfterDrained_WorksAgain()
    {
        var queue = new LinkedQueue<int>();
        queue.Enqueue(1);
        queue.Dequeue();

        queue.Enqueue(2);
        queue.Enqueue(3);

        queue.Should().Equal(2, 3);
    }

    [Test]
    public void Dequeue_LastItem_DoesNotKeepItAlive()
    {
        var queue = new LinkedQueue<object>();
        WeakReference reference = EnqueueAndDequeue(queue);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.IsAlive.Should().BeFalse();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference EnqueueAndDequeue(LinkedQueue<object> queue)
    {
        var item = new object();
        queue.Enqueue(item);
        queue.Dequeue();
        return new WeakReference(item);
    }

    [Test]
    public void CollectionExpression_FirstItemAtFront()
    {
        LinkedQueue<int> queue = [1, 2, 3];

        queue.Dequeue().Should().Be(1);
        queue.Count.Should().Be(2);
    }
}
