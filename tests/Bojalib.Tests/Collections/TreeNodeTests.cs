using Bojalib.Collections;

namespace Bojalib.Tests.Collections;

public class TreeNodeTests
{
    private static TreeNode<string> SampleTree() =>
        new("a",
            new TreeNode<string>("b", new TreeNode<string>("c"), new TreeNode<string>("d")),
            new TreeNode<string>("e", new TreeNode<string>("f")));

    [Test]
    public void Constructor_AttachesChildren()
    {
        var b = new TreeNode<string>("b");
        var c = new TreeNode<string>("c");

        var root = new TreeNode<string>("a", b, c);

        root.Value.Should().Be("a");
        root.Children.Should().Equal(b, c);
        b.Parent.Should().BeSameAs(root);
        root.IsRoot.Should().BeTrue();
        b.IsLeaf.Should().BeTrue();
        root.IsLeaf.Should().BeFalse();
    }

    [Test]
    public void AddChild_Value_CreatesAndAttachesNode()
    {
        var root = new TreeNode<int>(1);

        TreeNode<int> child = root.AddChild(2);

        child.Value.Should().Be(2);
        child.Parent.Should().BeSameAs(root);
        root.Children.Should().ContainSingle().Which.Should().BeSameAs(child);
    }

    [Test]
    public void AddChild_NodeThatHasAParent_Throws()
    {
        var first = new TreeNode<int>(1);
        TreeNode<int> child = first.AddChild(2);
        var second = new TreeNode<int>(3);

        second.Invoking(n => n.AddChild(child)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void AddChild_SelfOrAncestor_ThrowsInsteadOfCreatingCycle()
    {
        var root = new TreeNode<int>(1);
        TreeNode<int> child = root.AddChild(2);
        TreeNode<int> grandchild = child.AddChild(3);

        root.Invoking(n => n.AddChild(root)).Should().Throw<ArgumentException>();
        root.RemoveChild(child);
        grandchild.Invoking(n => n.AddChild(child)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void RemoveChild_DetachesOnlyOwnChildren()
    {
        var root = new TreeNode<int>(1);
        TreeNode<int> child = root.AddChild(2);
        var stranger = new TreeNode<int>(3);

        root.RemoveChild(stranger).Should().BeFalse();
        root.RemoveChild(child).Should().BeTrue();

        child.Parent.Should().BeNull();
        root.Children.Should().BeEmpty();
    }

    [Test]
    public void Children_CannotBeModifiedDirectly()
    {
        var root = new TreeNode<int>(1);

        Action act = () => ((IList<TreeNode<int>>)root.Children).Add(new TreeNode<int>(2));

        act.Should().Throw<NotSupportedException>();
    }

    [Test]
    public void DepthFirst_YieldsPreOrder()
    {
        SampleTree().DepthFirst().Select(static n => n.Value).Should().Equal("a", "b", "c", "d", "e", "f");
    }

    [Test]
    public void BreadthFirst_YieldsLevelOrder()
    {
        SampleTree().BreadthFirst().Select(static n => n.Value).Should().Equal("a", "b", "e", "c", "d", "f");
    }

    [Test]
    public void Ancestors_YieldsParentUpToRoot()
    {
        TreeNode<string> root = SampleTree();
        TreeNode<string> d = root.Children[0].Children[1];

        d.Ancestors().Select(static n => n.Value).Should().Equal("b", "a");
        root.Ancestors().Should().BeEmpty();
    }

    [Test]
    public void StructuralComparer_EqualTrees_AreEqualWithSameHash()
    {
        IEqualityComparer<TreeNode<string>> comparer = TreeNode<string>.CreateStructuralComparer();
        TreeNode<string> x = SampleTree();
        TreeNode<string> y = SampleTree();

        comparer.Equals(x, y).Should().BeTrue();
        comparer.GetHashCode(x).Should().Be(comparer.GetHashCode(y));
        x.Equals(y).Should().BeFalse("nodes themselves use reference equality");
    }

    [Test]
    public void StructuralComparer_DifferentTrees_AreNotEqual()
    {
        IEqualityComparer<TreeNode<string>> comparer = TreeNode<string>.CreateStructuralComparer();
        TreeNode<string> x = SampleTree();
        TreeNode<string> differentValue = SampleTree();
        differentValue.Children[1].Children[0].Value = "X";
        TreeNode<string> extraChild = SampleTree();
        extraChild.Children[0].AddChild("g");

        comparer.Equals(x, differentValue).Should().BeFalse();
        comparer.Equals(x, extraChild).Should().BeFalse();
        comparer.Equals(x, null).Should().BeFalse();
        comparer.Equals(null, null).Should().BeTrue();
    }

    [Test]
    public void StructuralComparer_CustomValueComparer_IsUsed()
    {
        IEqualityComparer<TreeNode<string>> comparer = TreeNode<string>.CreateStructuralComparer(StringComparer.OrdinalIgnoreCase);

        comparer.Equals(new TreeNode<string>("A"), new TreeNode<string>("a")).Should().BeTrue();
    }

    [Test]
    public void VeryDeepTree_TraversalAndComparisonDoNotOverflowStack()
    {
        static TreeNode<int> Chain(int depth)
        {
            var root = new TreeNode<int>(0);
            TreeNode<int> node = root;
            for (int i = 1; i < depth; i++)
            {
                node = node.AddChild(i);
            }

            return root;
        }

        TreeNode<int> x = Chain(200_000);
        TreeNode<int> y = Chain(200_000);

        x.DepthFirst().Should().HaveCount(200_000);
        TreeNode<int>.CreateStructuralComparer().Equals(x, y).Should().BeTrue();
    }
}
