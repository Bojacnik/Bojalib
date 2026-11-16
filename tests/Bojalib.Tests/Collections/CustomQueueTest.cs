using Bojalib.Collections;
using Bojalib.Collections.Exceptions;
using FluentAssertions;

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
    public void Dequeue_Empty_ThrowsQueueEmptyException()
    {
        var queue = new CustomQueue<int>();

        Action act = () => queue.Dequeue();

        act.Should().Throw<QueueEmptyException>();
    }
}
