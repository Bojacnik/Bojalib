using Bojalib.Collections;

namespace Bojalib.Tests.Collections;

public class LinkedStackTests
{
    [Test]
    public void Pop_AfterPushes_ReturnsItemsInLifoOrder()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        stack.Pop().Should().Be(3);
        stack.Pop().Should().Be(2);
        stack.Pop().Should().Be(1);
        stack.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Count_TracksPushesAndPops()
    {
        var stack = new LinkedStack<string>();
        stack.Count.Should().Be(0);

        stack.Push("a");
        stack.Push("b");
        stack.Count.Should().Be(2);

        stack.Pop();
        stack.Count.Should().Be(1);
    }

    [Test]
    public void Peek_ReturnsTopWithoutRemoving()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        stack.Push(2);

        stack.Peek().Should().Be(2);
        stack.Count.Should().Be(2);
    }

    [Test]
    public void TryPopAndTryPeek_Empty_ReturnFalse()
    {
        var stack = new LinkedStack<int>();

        stack.TryPop(out _).Should().BeFalse();
        stack.TryPeek(out _).Should().BeFalse();
    }

    [Test]
    public void TryPopAndTryPeek_NonEmpty_ReturnTop()
    {
        var stack = new LinkedStack<int>();
        stack.Push(5);

        stack.TryPeek(out int peeked).Should().BeTrue();
        peeked.Should().Be(5);
        stack.TryPop(out int popped).Should().BeTrue();
        popped.Should().Be(5);
    }

    [Test]
    public void PopAndPeek_Empty_ThrowInvalidOperation()
    {
        var stack = new LinkedStack<int>();

        stack.Invoking(s => s.Pop()).Should().Throw<InvalidOperationException>();
        stack.Invoking(s => s.Peek()).Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        stack.Push(2);

        stack.Clear();

        stack.Should().BeEmpty();
        stack.Count.Should().Be(0);
    }

    [Test]
    public void Enumerate_YieldsTopToBottom()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        stack.Should().Equal(3, 2, 1);
    }

    [Test]
    public void Enumerate_ModifiedDuringEnumeration_Throws()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        stack.Push(2);

        Action act = () =>
        {
            foreach (int item in stack)
            {
                stack.Push(item);
            }
        };

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Enumerator_CurrentBeforeMoveNext_Throws()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        LinkedStack<int>.Enumerator enumerator = stack.GetEnumerator();

        Action act = () => _ = enumerator.Current;

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Enumerator_Reset_StartsOver()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        stack.Push(2);
        System.Collections.IEnumerator enumerator = ((System.Collections.IEnumerable)stack).GetEnumerator();
        enumerator.MoveNext();
        enumerator.MoveNext();

        enumerator.Reset();

        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(2);
    }

    [Test]
    public void Enumerator_ResetAfterModification_Throws()
    {
        var stack = new LinkedStack<int>();
        stack.Push(1);
        LinkedStack<int>.Enumerator enumerator = stack.GetEnumerator();
        stack.Push(2);

        Action act = () => enumerator.Reset();

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void CollectionExpression_LastItemOnTop()
    {
        LinkedStack<int> stack = [1, 2, 3];

        stack.Pop().Should().Be(3);
        stack.Count.Should().Be(2);
    }
}
