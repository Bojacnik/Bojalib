using Bojalib.Collections;
using Bojalib.Collections.Exceptions;
using AwesomeAssertions;

namespace Bojalib.Tests.Collections;

public class CustomQueueTest
{
    [Test]
    public void Dequeue_ReturnsItemsInFifoOrder()
    {
        var queue = new CustomQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        queue.Dequeue().Should().Be(1);
        queue.Dequeue().Should().Be(2);
        queue.Dequeue().Should().Be(3);
        queue.IsEmpty().Should().BeTrue();
    }

    [Test]
    public void Enqueue_AfterDrained_WorksAgain()
    {
        var queue = new CustomQueue<int>();
        queue.Enqueue(1);
        queue.Dequeue();

        queue.Enqueue(2);
        queue.Enqueue(3);

        queue.Should().Equal(2, 3);
    }

    [Test]
    public void Dequeue_LastItem_DoesNotKeepItAlive()
    {
        var queue = new CustomQueue<object>();
        WeakReference reference = EnqueueAndDequeue(queue);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.IsAlive.Should().BeFalse();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference EnqueueAndDequeue(CustomQueue<object> queue)
    {
        var item = new object();
        queue.Enqueue(item);
        queue.Dequeue();
        return new WeakReference(item);
    }

    [Test]
    public void Dequeue_Empty_ThrowsQueueEmptyException()
    {
        var queue = new CustomQueue<int>();

        Action act = () => queue.Dequeue();

        act.Should().Throw<QueueEmptyException>().Which.Should().BeAssignableTo<InvalidOperationException>();
    }
}
