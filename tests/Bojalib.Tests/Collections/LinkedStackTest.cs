using Bojalib.Collections;
using Bojalib.Collections.Exceptions;
using AwesomeAssertions;

namespace Bojalib.Tests.Collections;

public class LinkedStackTest
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
    }

    [Test]
    public void Pop_Empty_ThrowsStackEmptyException()
    {
        var stack = new LinkedStack<int>();

        Action act = () => stack.Pop();

        act.Should().Throw<StackEmptyException>();
    }

    [Test]
    public void Pop_AfterDrained_ThrowsStackEmptyException()
    {
        var stack = new LinkedStack<string>();
        stack.Push("a");
        stack.Pop();

        Action act = () => stack.Pop();

        act.Should().Throw<StackEmptyException>();
    }
}
