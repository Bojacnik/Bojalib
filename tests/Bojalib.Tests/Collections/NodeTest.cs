using Bojalib.Collections;
using FluentAssertions;

namespace Bojalib.Tests.Collections;

public class NodeTest
{
    [Test]
    public void Constructor_WithDataAndChildren_SetsMembers()
    {
        var leftChild = new Node<string>("b");
        var rightChild = new Node<string>("c");

        var root = new Node<string>("a", leftChild, rightChild);

        root.Data.Should().Be("a");
        root.Children.Should().Equal(leftChild, rightChild);
        ((IEnumerable<Node<string>>)root).Should().Equal(root, leftChild, rightChild);
    }

    [Test]
    public void CompareTo_ComparesByData()
    {
        new Node<string>("a").CompareTo(new Node<string>("b")).Should().BeNegative();
    }
}
