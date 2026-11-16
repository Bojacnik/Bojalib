using Bojalib.Collections;
using CsCheck;

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

    [Test]
    public void PostOrder_YieldsChildrenBeforeParents()
    {
        SampleTree().PostOrder().Select(static n => n.Value).Should().Equal("c", "d", "b", "f", "e", "a");
    }

    [Test]
    public void Leaves_YieldsLeavesLeftToRight()
    {
        SampleTree().Leaves().Select(static n => n.Value).Should().Equal("c", "d", "f");
        new TreeNode<string>("solo").Leaves().Select(static n => n.Value).Should().Equal("solo");
    }

    [Test]
    public void Traversals_OfSubtree_StayInsideSubtree()
    {
        TreeNode<string> b = SampleTree().Children[0];

        b.DepthFirst().Select(static n => n.Value).Should().Equal("b", "c", "d");
        b.PostOrder().Select(static n => n.Value).Should().Equal("c", "d", "b");
        b.BreadthFirst().Select(static n => n.Value).Should().Equal("b", "c", "d");
        b.Leaves().Select(static n => n.Value).Should().Equal("c", "d");
        b.Height.Should().Be(1);
    }

    [Test]
    public void RootDepthHeightAndPath()
    {
        TreeNode<string> root = SampleTree();
        TreeNode<string> f = root.Children[1].Children[0];

        f.Root.Should().BeSameAs(root);
        f.Depth.Should().Be(2);
        root.Depth.Should().Be(0);
        root.Height.Should().Be(2);
        f.Height.Should().Be(0);
        f.GetPath().Select(static n => n.Value).Should().Equal("a", "e", "f");
    }

    [Test]
    public void Find_ReturnsFirstMatchInDepthFirstOrder()
    {
        TreeNode<string> root = SampleTree();

        root.Find(static v => v is "d" or "f")!.Value.Should().Be("d");
        root.Find(static v => v == "zzz").Should().BeNull();
    }

    [Test]
    public void RemoveChild_KeepsSiblingTraversalCorrect()
    {
        TreeNode<int> root = new(0);
        TreeNode<int> one = root.AddChild(1);
        root.AddChild(2);
        root.AddChild(3);

        root.RemoveChild(one);
        root.AddChild(4);

        root.DepthFirst().Select(static n => n.Value).Should().Equal(0, 2, 3, 4);
        root.PostOrder().Select(static n => n.Value).Should().Equal(2, 3, 4, 0);
    }

    [Test]
    public void BreadthFirst_LargeWideTree_GrowsPooledQueue()
    {
        var root = new TreeNode<int>(0);
        for (int i = 0; i < 100; i++)
        {
            TreeNode<int> child = root.AddChild(i);
            for (int j = 0; j < 50; j++)
            {
                child.AddChild(j);
            }
        }

        root.BreadthFirst().Should().HaveCount(1 + 100 + 5000);
    }

    [Test]
    public void Enumerators_CanBeResetAndRerun()
    {
        TreeNode<string> root = SampleTree();
        TreeNode<string>.BreadthFirstEnumerator bfs = root.BreadthFirst().GetEnumerator();
        while (bfs.MoveNext())
        {
        }

        bfs.MoveNext().Should().BeFalse();
        bfs.Reset();
        bfs.MoveNext().Should().BeTrue();
        bfs.Current.Value.Should().Be("a");
        bfs.Dispose();

        TreeNode<string>.DepthFirstEnumerator dfs = root.DepthFirst().GetEnumerator();
        dfs.MoveNext();
        dfs.Reset();
        dfs.MoveNext().Should().BeTrue();
        dfs.Current.Value.Should().Be("a");

        TreeNode<string>.AncestorsEnumerator up = root.Children[0].Children[0].Ancestors().GetEnumerator();
        up.MoveNext();
        up.Reset();
        up.MoveNext().Should().BeTrue();
        up.Current.Value.Should().Be("b");
    }

    [Test]
    public void RandomTrees_TraversalsMatchRecursiveReference()
    {
        // A tree is generated as a parent index per node: node i's parent is some node before it.
        Gen.Int[1, 200].SelectMany(n => Gen.Int[0, 1000].Array[n].Select(r => (n, r))).Sample(t =>
        {
            (int n, int[] randoms) = t;
            var nodes = new TreeNode<int>[n];
            nodes[0] = new TreeNode<int>(0);
            for (int i = 1; i < n; i++)
            {
                nodes[i] = nodes[randoms[i] % i].AddChild(i);
            }

            static IEnumerable<int> Pre(TreeNode<int> x) => new[] { x.Value }.Concat(x.Children.SelectMany(Pre));
            static IEnumerable<int> Post(TreeNode<int> x) => x.Children.SelectMany(Post).Append(x.Value);
            static int Height(TreeNode<int> x) => x.Children.Count == 0 ? 0 : 1 + x.Children.Max(Height);

            TreeNode<int> root = nodes[0];
            root.DepthFirst().Select(static x => x.Value).Should().Equal(Pre(root));
            root.PostOrder().Select(static x => x.Value).Should().Equal(Post(root));
            root.Leaves().Select(static x => x.Value).Should().Equal(Pre(root).Where(v => nodes[v].IsLeaf));
            root.BreadthFirst().Select(static x => x.Value).Should().BeEquivalentTo(Pre(root));
            root.BreadthFirst().Select(static x => x.Depth).Should().BeInAscendingOrder();
            root.Height.Should().Be(Height(root));
            TreeNode<int>.CreateStructuralComparer().Equals(root, root).Should().BeTrue();
        }, iter: 300);
    }
}
