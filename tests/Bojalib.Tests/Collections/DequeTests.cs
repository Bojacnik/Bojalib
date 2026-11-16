using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class DequeTests
{
    [Test]
    public void RandomOperations_MatchLinkedListModel()
    {
        Gen.Select(Gen.Int[0, 6], Gen.Int[0, 5].Array[0, 400]).Sample(t =>
        {
            (int initialCapacity, int[] ops) = t;
            var deque = new Deque<int>(initialCapacity);
            var model = new LinkedList<int>();
            for (int i = 0; i < ops.Length; i++)
            {
                switch (ops[i])
                {
                    case 0: deque.PushBack(i); model.AddLast(i); break;
                    case 1: deque.PushFront(i); model.AddFirst(i); break;
                    case 2:
                        deque.TryPopFront(out int f).Should().Be(model.Count > 0);
                        if (model.Count > 0) { f.Should().Be(model.First!.Value); model.RemoveFirst(); }
                        break;
                    case 3:
                        deque.TryPopBack(out int b).Should().Be(model.Count > 0);
                        if (model.Count > 0) { b.Should().Be(model.Last!.Value); model.RemoveLast(); }
                        break;
                    case 4:
                        if (model.Count > 0) { deque[model.Count - 1] = -i; model.Last!.Value = -i; }
                        break;
                    default:
                        deque.PushBack(i);
                        model.AddLast(i);
                        break;
                }

                deque.Should().Equal(model);
                deque.Count.Should().Be(model.Count);
                if (model.Count > 0)
                {
                    deque.PeekFront().Should().Be(model.First!.Value);
                    deque.PeekBack().Should().Be(model.Last!.Value);
                    deque[0].Should().Be(model.First.Value);
                }
            }

            int[] copy = new int[deque.Count];
            deque.CopyTo(copy);
            copy.Should().Equal(model);
        }, iter: 500);
    }

    [Test]
    public void GetSpans_ExposeWrappedContents()
    {
        var deque = new Deque<int>(4);
        deque.PushBack(2);
        deque.PushBack(3);
        deque.PushFront(1); // wraps to the end of the array

        deque.GetSpans(out ReadOnlySpan<int> first, out ReadOnlySpan<int> second);

        first.ToArray().Concat(second.ToArray()).Should().Equal(1, 2, 3);
        second.Length.Should().BeGreaterThan(0);
    }

    [Test]
    public void ConstructorsCapacityAndCollectionExpression()
    {
        Deque<string> deque = ["a", "b"];
        deque.Should().Equal("a", "b");

        var fromEnumerable = new Deque<int>(Enumerable.Range(0, 5));
        fromEnumerable.PushFront(-1);
        fromEnumerable.Should().Equal(-1, 0, 1, 2, 3, 4);

        var sized = new Deque<int>();
        sized.EnsureCapacity(100);
        sized.Capacity.Should().BeGreaterThanOrEqualTo(100);
        sized.Invoking(d => d.EnsureCapacity(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Deque<int>(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Deque<int>((IEnumerable<int>)null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void EmptyAndModificationGuards()
    {
        var deque = new Deque<string>();

        deque.IsEmpty.Should().BeTrue();
        deque.Invoking(d => d.PopFront()).Should().Throw<InvalidOperationException>();
        deque.Invoking(d => d.PopBack()).Should().Throw<InvalidOperationException>();
        deque.Invoking(d => d.PeekFront()).Should().Throw<InvalidOperationException>();
        deque.Invoking(d => d.PeekBack()).Should().Throw<InvalidOperationException>();
        deque.TryPeekFront(out _).Should().BeFalse();
        deque.TryPeekBack(out _).Should().BeFalse();
        deque.Invoking(d => _ = d[0]).Should().Throw<ArgumentOutOfRangeException>();

        deque.PushBack("x");
        deque.PopBack().Should().Be("x");
        deque.PushBack("y");
        deque.PopFront().Should().Be("y");
        deque.PushBack("z");
        deque.Invoking(d => d.CopyTo(Span<string>.Empty)).Should().Throw<ArgumentException>();

        Deque<string>.Enumerator e = deque.GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        deque.PushBack("w");
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();

        deque.Clear();
        deque.Should().BeEmpty();
    }
}

public class CircularBufferTests
{
    [Test]
    public void RandomOperations_MatchSlidingWindowModel()
    {
        Gen.Select(Gen.Int[1, 7], Gen.Int[0, 6].Array[0, 400]).Sample(t =>
        {
            (int capacity, int[] ops) = t;
            var buffer = new CircularBuffer<int>(capacity);
            var model = new LinkedList<int>();
            for (int i = 0; i < ops.Length; i++)
            {
                switch (ops[i])
                {
                    case 0:
                        buffer.PushBack(i);
                        if (model.Count == capacity) model.RemoveFirst();
                        model.AddLast(i);
                        break;
                    case 1:
                        buffer.PushFront(i);
                        if (model.Count == capacity) model.RemoveLast();
                        model.AddFirst(i);
                        break;
                    case 2:
                        buffer.TryPopFront(out int f).Should().Be(model.Count > 0);
                        if (model.Count > 0) { f.Should().Be(model.First!.Value); model.RemoveFirst(); }
                        break;
                    case 3:
                        buffer.TryPopBack(out int b).Should().Be(model.Count > 0);
                        if (model.Count > 0) { b.Should().Be(model.Last!.Value); model.RemoveLast(); }
                        break;
                    case 4:
                        buffer.TryPushBack(i).Should().Be(model.Count < capacity);
                        if (model.Count < capacity) model.AddLast(i);
                        break;
                    case 5:
                        buffer.TryPushFront(i).Should().Be(model.Count < capacity);
                        if (model.Count < capacity) model.AddFirst(i);
                        break;
                    default:
                        if (model.Count > 0) { buffer[0] = -i; model.First!.Value = -i; }
                        break;
                }

                buffer.Should().Equal(model);
                buffer.IsFull.Should().Be(model.Count == capacity);
                buffer.Capacity.Should().Be(capacity);
                if (model.Count > 0)
                {
                    buffer.PeekFront().Should().Be(model.First!.Value);
                    buffer.PeekBack().Should().Be(model.Last!.Value);
                    buffer.TryPeekBack(out int last).Should().BeTrue();
                    last.Should().Be(model.Last.Value);
                }
            }

            buffer.GetSpans(out ReadOnlySpan<int> first, out ReadOnlySpan<int> second);
            first.ToArray().Concat(second.ToArray()).Should().Equal(model);
        }, iter: 500);
    }

    [Test]
    public void MovingAverage_KeepsExactCapacity()
    {
        var window = new CircularBuffer<double>(3);
        foreach (double sample in (double[])[1, 2, 3, 4, 5])
        {
            window.PushBack(sample);
        }

        window.Should().Equal(3, 4, 5);
        window.Average().Should().Be(4);
        window.Capacity.Should().Be(3);
    }

    [Test]
    public void CollectionExpressionAndGuards()
    {
        CircularBuffer<int> full = [1, 2, 3];
        full.IsFull.Should().BeTrue();
        full.PushBack(4);
        full.Should().Equal(2, 3, 4);

        CircularBuffer<int> empty = [];
        empty.Capacity.Should().Be(1);
        empty.IsEmpty.Should().BeTrue();
        empty.Invoking(b => b.PopFront()).Should().Throw<InvalidOperationException>();
        empty.Invoking(b => b.PopBack()).Should().Throw<InvalidOperationException>();
        empty.Invoking(b => b.PeekFront()).Should().Throw<InvalidOperationException>();
        empty.Invoking(b => b.PeekBack()).Should().Throw<InvalidOperationException>();
        empty.TryPeekFront(out _).Should().BeFalse();
        FluentActions.Invoking(() => new CircularBuffer<int>(0)).Should().Throw<ArgumentOutOfRangeException>();

        full.PopBack().Should().Be(4);
        full.PopFront().Should().Be(2);
        int[] copy = new int[1];
        full.CopyTo(copy);
        copy.Should().Equal(3);

        CircularBuffer<int>.Enumerator e = full.GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        full.PushBack(9);
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();
        full.Clear();
        full.Should().BeEmpty();
    }
}
