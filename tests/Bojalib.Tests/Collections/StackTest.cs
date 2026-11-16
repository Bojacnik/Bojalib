using Bojalib.Collections.Exceptions;
using FluentAssertions;

namespace Bojalib.Tests.Collections;

public class StackTest
{
    [Test]
    public void Pop_AfterPushes_ReturnsItemsInLifoOrder()
    {
        var stack = new Bojalib.Collections.Stack<int>();

        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        stack.Pop().Should().Be(3);
        stack.Pop().Should().Be(2);
        stack.Pop().Should().Be(1);
    }

    [Test]
    public void Pop_Empty_ThrowsStackEmptyException()
    {
        var stack = new Bojalib.Collections.Stack<int>();

        Action act = () => stack.Pop();

        act.Should().Throw<StackEmptyException>();
    }

    [Test]
    public void Pop_AfterDrained_ThrowsStackEmptyException()
    {
        var stack = new Bojalib.Collections.Stack<string>();
        stack.Push("a");
        stack.Pop();

        Action act = () => stack.Pop();

        act.Should().Throw<StackEmptyException>();
    }
}
