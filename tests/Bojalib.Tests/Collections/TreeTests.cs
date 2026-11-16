using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class TreeTests
{
    /// <summary>A plain user-defined node type, like one Tree is meant to work with.</summary>
    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;

        public List<Node> Children { get; } = [.. children];

        public override string ToString() => Name;
    }

    private static Node Sample() =>
        new("a",
            new Node("b", new Node("c"), new Node("d")),
            new Node("e", new Node("f")));

    private static IEnumerable<string> Names(IEnumerable<Node> nodes) => nodes.Select(static n => n.Name);

    [Test]
    public void Traversals_OnListChildren()
    {
        Node root = Sample();

        Names(Tree.DepthFirst(root, static n => n.Children)).Should().Equal("a", "b", "c", "d", "e", "f");
        Names(Tree.PostOrder(root, static n => n.Children)).Should().Equal("c", "d", "b", "f", "e", "a");
        Names(Tree.BreadthFirst(root, static n => n.Children)).Should().Equal("a", "b", "e", "c", "d", "f");
        Names(Tree.Leaves(root, static n => n.Children)).Should().Equal("c", "d", "f");
    }

    [Test]
    public void Traversals_OnLazySequencesAndNullChildren()
    {
        // Children produced by an iterator (not a list) take the enumerator path.
        static IEnumerable<int>? Children(int n) => n < 4 ? Iterate(n) : null;
        static IEnumerable<int> Iterate(int n)
        {
            yield return (2 * n) + 1;
            yield return (2 * n) + 2;
        }

        // Nodes 0..3 have children 2n+1 and 2n+2; everything else has none.
        Tree.DepthFirst(0, Children).Should().Equal(0, 1, 3, 7, 8, 4, 2, 5, 6);
        Tree.BreadthFirst(0, Children).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8);
        Tree.Leaves(0, Children).Should().Equal(7, 8, 4, 5, 6);
        Tree.Count(0, Children).Should().Be(9);
        Tree.Height(0, Children).Should().Be(3);
    }

    [Test]
    public void FindPathCountHeight()
    {
        Node root = Sample();

        Tree.TryFind(root, static n => n.Children, static n => n.Name is "d" or "f", out var found).Should().BeTrue();
        found!.Name.Should().Be("d");
        Tree.TryFind<Node>(root, static n => n.Children, static n => n.Name == "zzz", out _).Should().BeFalse();

        Tree.TryFindPath(root, static n => n.Children, static n => n.Name == "f", out var path).Should().BeTrue();
        Names(path!).Should().Equal("a", "e", "f");
        Tree.TryFindPath(root, static n => n.Children, static n => false, out _).Should().BeFalse();

        Tree.Count(root, static n => n.Children).Should().Be(6);
        Tree.Height(root, static n => n.Children).Should().Be(2);
        Tree.Height(new Node("solo"), static n => n.Children).Should().Be(0);
    }

    [Test]
    public void Enumerators_ExposeDepthAndCanReset()
    {
        Node root = Sample();

        Tree.DepthFirstEnumerator<Node> dfs = Tree.DepthFirst(root, static n => n.Children).GetEnumerator();
        var depths = new List<int>();
        while (dfs.MoveNext())
        {
            depths.Add(dfs.CurrentDepth);
        }

        depths.Should().Equal(0, 1, 2, 2, 1, 2);
        dfs.MoveNext().Should().BeFalse();
        dfs.Reset();
        dfs.MoveNext().Should().BeTrue();
        dfs.Current.Name.Should().Be("a");
        dfs.Dispose();

        Tree.BreadthFirstEnumerator<Node> bfs = Tree.BreadthFirst(root, static n => n.Children).GetEnumerator();
        bfs.MoveNext();
        bfs.MoveNext();
        bfs.CurrentDepth.Should().Be(1);
        bfs.Reset();
        bfs.MoveNext().Should().BeTrue();
        bfs.Current.Name.Should().Be("a");
        while (bfs.MoveNext())
        {
        }

        bfs.MoveNext().Should().BeFalse();
        bfs.Dispose();

        Tree.PostOrderEnumerator<Node> post = Tree.PostOrder(root, static n => n.Children).GetEnumerator();
        post.MoveNext();
        post.Reset();
        post.MoveNext().Should().BeTrue();
        post.Current.Name.Should().Be("c");
        post.Dispose();
    }

    [Test]
    public void VeryDeepAndWideTrees_DoNotOverflowAndGrowPools()
    {
        Node deep = new("0");
        Node tip = deep;
        for (int i = 1; i < 200_000; i++)
        {
            var next = new Node(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tip.Children.Add(next);
            tip = next;
        }

        Tree.Count(deep, static n => n.Children).Should().Be(200_000);
        Tree.Height(deep, static n => n.Children).Should().Be(199_999);
        Tree.PostOrder(deep, static n => n.Children).First().Should().BeSameAs(tip);

        Node wide = new("root", [.. Enumerable.Range(0, 5000).Select(static i => new Node("c", new Node("g")))]);
        Tree.BreadthFirst(wide, static n => n.Children).Should().HaveCount(1 + 5000 + 5000);
    }

    [Test]
    public void RandomTrees_MatchRecursiveReference()
    {
        Gen.Select(Gen.Int[1, 150], Gen.Bool).SelectMany(t => Gen.Int[0, 1000].Array[t.Item1].Select(r => (t.Item1, t.Item2, r))).Sample(t =>
        {
            (int n, bool lazy, int[] randoms) = t;
            var children = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                children[i] = [];
            }

            for (int i = 1; i < n; i++)
            {
                children[randoms[i] % i].Add(i);
            }

            Func<int, IEnumerable<int>?> childrenOf = lazy ? i => children[i].Select(static x => x) : i => children[i];

            IEnumerable<int> Pre(int x) => new[] { x }.Concat(children[x].SelectMany(Pre));
            IEnumerable<int> Post(int x) => children[x].SelectMany(Post).Append(x);
            int Height(int x) => children[x].Count == 0 ? 0 : 1 + children[x].Max(Height);

            Tree.DepthFirst(0, childrenOf).Should().Equal(Pre(0));
            Tree.PostOrder(0, childrenOf).Should().Equal(Post(0));
            Tree.Leaves(0, childrenOf).Should().Equal(Pre(0).Where(x => children[x].Count == 0));
            Tree.BreadthFirst(0, childrenOf).Should().BeEquivalentTo(Pre(0));
            Tree.Height(0, childrenOf).Should().Be(Height(0));
            Tree.Count(0, childrenOf).Should().Be(n);
        }, iter: 300);
    }

    [Test]
    public void ArgumentsAreValidated()
    {
        FluentActions.Invoking(() => Tree.DepthFirst<int>(0, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Tree.PostOrder<int>(0, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Tree.Leaves<int>(0, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Tree.BreadthFirst<int>(0, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Tree.TryFind(0, static _ => null, null!, out _)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Tree.TryFindPath(0, static _ => null, null!, out _)).Should().Throw<ArgumentNullException>();
    }
}
