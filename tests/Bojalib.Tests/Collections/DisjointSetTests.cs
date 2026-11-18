using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class DisjointSetTests
{
    [Test]
    public void RandomUnions_MatchNaiveComponentLabels()
    {
        Gen.Select(Gen.Int[1, 40], Gen.Select(Gen.Int[0, 1000], Gen.Int[0, 1000]).Array[0, 100]).Sample(t =>
        {
            (int n, (int A, int B)[] pairs) = t;
            var sets = new DisjointSet(n);
            int[] label = [.. Enumerable.Range(0, n)];

            foreach ((int ra, int rb) in pairs)
            {
                int a = ra % n;
                int b = rb % n;
                bool different = label[a] != label[b];
                sets.Union(a, b).Should().Be(different);
                if (different)
                {
                    int old = label[b];
                    for (int i = 0; i < n; i++)
                    {
                        if (label[i] == old) label[i] = label[a];
                    }
                }
            }

            sets.SetCount.Should().Be(label.Distinct().Count());
            for (int i = 0; i < n; i++)
            {
                sets.SizeOf(i).Should().Be(label.Count(l => l == label[i]));
                for (int j = 0; j < n; j++)
                {
                    sets.AreConnected(i, j).Should().Be(label[i] == label[j]);
                }
            }
        }, iter: TestScale.Iterations(300));
    }

    [Test]
    public void Add_GrowsAndValidates()
    {
        var sets = new DisjointSet();
        for (int i = 0; i < 10; i++)
        {
            sets.Add().Should().Be(i);
        }

        sets.Count.Should().Be(10);
        sets.Union(0, 9);
        sets.Find(9).Should().Be(sets.Find(0));
        sets.Invoking(s => s.Find(10)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new DisjointSet(-1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Generic_WorksOnArbitraryValues()
    {
        var sets = new DisjointSet<string>(StringComparer.OrdinalIgnoreCase);

        sets.Union("a", "b").Should().BeTrue();
        sets.Union("B", "c").Should().BeTrue();
        sets.Union("c", "A").Should().BeFalse();
        sets.Add("d").Should().BeTrue();
        sets.Add("D").Should().BeFalse();

        sets.AreConnected("a", "C").Should().BeTrue();
        sets.AreConnected("a", "d").Should().BeFalse();
        sets.AreConnected("a", "unknown").Should().BeFalse();
        sets.Find("c").Should().BeOneOf("a", "b", "c");
        sets.SizeOf("A").Should().Be(3);
        sets.Count.Should().Be(4);
        sets.SetCount.Should().Be(2);
        sets.Contains("d").Should().BeTrue();
        sets.Invoking(s => s.Find("zzz")).Should().Throw<KeyNotFoundException>();
    }
}
