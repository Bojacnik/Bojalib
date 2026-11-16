using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class RingBufferTests
{
    [Test]
    public void RandomOperations_MatchLinkedListModel()
    {
        Gen.Select(Gen.Int[1, 9], Gen.Bool, Gen.Int[0, 5].Array[0, 300]).Sample(t =>
        {
            (int requested, bool overwrite, int[] ops) = t;
            var buffer = new RingBuffer<int>(requested, overwrite);
            var model = new LinkedList<int>();
            int capacity = buffer.Capacity;

            for (int i = 0; i < ops.Length; i++)
            {
                switch (ops[i])
                {
                    case 0:
                    case 1:
                        bool front = ops[i] == 1;
                        if (model.Count == capacity && !overwrite)
                        {
                            FluentActions.Invoking(() => { if (front) buffer.PushFront(i); else buffer.PushBack(i); })
                                .Should().Throw<InvalidOperationException>();
                            break;
                        }

                        if (front)
                        {
                            buffer.PushFront(i);
                            if (model.Count == capacity) model.RemoveLast();
                            model.AddFirst(i);
                        }
                        else
                        {
                            buffer.PushBack(i);
                            if (model.Count == capacity) model.RemoveFirst();
                            model.AddLast(i);
                        }

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
                    default:
                        if (model.Count > 0)
                        {
                            buffer[0] = -i;
                            model.First!.Value = -i;
                        }

                        break;
                }

                buffer.Should().Equal(model);
                buffer.IsFull.Should().Be(model.Count == capacity);
                int[] copy = new int[buffer.Count];
                buffer.CopyTo(copy);
                copy.Should().Equal(model);
                if (model.Count > 0)
                {
                    buffer.PeekFront().Should().Be(model.First!.Value);
                    buffer.PeekBack().Should().Be(model.Last!.Value);
                    buffer[buffer.Count - 1].Should().Be(model.Last.Value);
                }
            }
        }, iter: 500);
    }

    [Test]
    public void GetSpans_ExposesWrappedContentsWithoutCopying()
    {
        var buffer = new RingBuffer<int>(4);
        buffer.PushBack(1);
        buffer.PushBack(2);
        buffer.PushBack(3);
        buffer.PopFront();
        buffer.PopFront();
        buffer.PushBack(4);
        buffer.PushBack(5);

        buffer.GetSpans(out ReadOnlySpan<int> first, out ReadOnlySpan<int> second);

        first.ToArray().Should().Equal(3, 4);
        second.ToArray().Should().Equal(5);
    }

    [Test]
    public void CapacityRoundsUpAndCollectionExpressionWorks()
    {
        new RingBuffer<int>(5).Capacity.Should().Be(8);
        RingBuffer<string> buffer = ["a", "b", "c"];

        buffer.Should().Equal("a", "b", "c");
        buffer.Capacity.Should().Be(4);
        RingBuffer<int> empty = [];
        empty.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Guards()
    {
        var buffer = new RingBuffer<string>(2);

        buffer.Invoking(b => b.PopFront()).Should().Throw<InvalidOperationException>();
        buffer.Invoking(b => b.PopBack()).Should().Throw<InvalidOperationException>();
        buffer.Invoking(b => b.PeekFront()).Should().Throw<InvalidOperationException>();
        buffer.Invoking(b => b.PeekBack()).Should().Throw<InvalidOperationException>();
        buffer.Invoking(b => _ = b[0]).Should().Throw<ArgumentOutOfRangeException>();
        buffer.Invoking(b => b[0] = "x").Should().Throw<ArgumentOutOfRangeException>();
        buffer.Invoking(b => b.CopyTo(Span<string>.Empty)).Should().NotThrow();
        FluentActions.Invoking(() => new RingBuffer<int>(0)).Should().Throw<ArgumentOutOfRangeException>();

        buffer.PushBack("x");
        buffer.PopBack().Should().Be("x");
        buffer.PushBack("y");
        buffer.PopFront().Should().Be("y");
        buffer.PushBack("z");
        buffer.Invoking(b => b.CopyTo(Span<string>.Empty)).Should().Throw<ArgumentException>();
        buffer.Clear();
        buffer.Should().BeEmpty();
        buffer.Overwrite.Should().BeFalse();

        buffer.PushBack("q");
        RingBuffer<string>.Enumerator e = buffer.GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        buffer.PushBack("r");
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();
    }
}
