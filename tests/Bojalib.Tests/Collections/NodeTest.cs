using Bojalib.Collections;
using AwesomeAssertions;

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
    public void Enumerate_YieldsAllDescendantsDepthFirst()
    {
        var root = new Node<string>("a",
            new Node<string>("b", new Node<string>("c"), new Node<string>("d")),
            new Node<string>("e", new Node<string>("f")));

        root.Select(node => node.Data).Should().Equal("a", "b", "c", "d", "e", "f");
    }

    [Test]
    public void Equals_StructurallyEqualTrees_ReturnsTrue()
    {
        var x = new Node<string>("a", new Node<string>("b", new Node<string>("c")));
        var y = new Node<string>("a", new Node<string>("b", new Node<string>("c")));

        x.Equals(x, y).Should().BeTrue();
        x.GetHashCode(x).Should().Be(x.GetHashCode(y));
    }

    [Test]
    public void Equals_DifferentGrandchild_ReturnsFalse()
    {
        var x = new Node<string>("a", new Node<string>("b", new Node<string>("c")));
        var y = new Node<string>("a", new Node<string>("b", new Node<string>("X")));

        x.Equals(x, y).Should().BeFalse();
    }

    [Test]
    public void CompareTo_ComparesByData()
    {
        new Node<string>("a").CompareTo(new Node<string>("b")).Should().BeNegative();
    }
}
