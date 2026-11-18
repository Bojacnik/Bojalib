using Bojalib.Probabilistic;

namespace Bojalib.Tests.Probabilistic;

public class TDigestTests
{
    private static readonly double[] Quantiles = [0.001, 0.01, 0.1, 0.25, 0.5, 0.75, 0.9, 0.99, 0.999];

    public static IEnumerable<string> Distributions() => ["uniform", "normal", "exponential", "lognormal", "sorted", "duplicates"];

    private static double[] Generate(string distribution, int n, int seed)
    {
        var random = new Random(seed);
        double Normal() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        return distribution switch
        {
            "uniform" => [.. Enumerable.Range(0, n).Select(_ => random.NextDouble())],
            "normal" => [.. Enumerable.Range(0, n).Select(_ => Normal())],
            "exponential" => [.. Enumerable.Range(0, n).Select(_ => -Math.Log(1 - random.NextDouble()))],
            "lognormal" => [.. Enumerable.Range(0, n).Select(_ => Math.Exp(Normal() * 2))],
            "sorted" => [.. Enumerable.Range(0, n).Select(static i => (double)i)],
            "duplicates" => [.. Enumerable.Range(0, n).Select(_ => (double)random.Next(20))],
            _ => throw new ArgumentOutOfRangeException(nameof(distribution)),
        };
    }

    // Rank error: where the estimate actually falls in the sorted data, versus the requested quantile.
    private static double RankError(double[] sorted, double estimate, double q)
    {
        int below = Array.BinarySearch(sorted, estimate);
        if (below < 0)
        {
            below = ~below;
        }
        else
        {
            // Inside a run of equal values any rank in the run is correct.
            int first = below;
            int last = below;
            while (first > 0 && sorted[first - 1] == estimate) first--;
            while (last < sorted.Length - 1 && sorted[last + 1] == estimate) last++;
            double target = q * sorted.Length;
            return target >= first && target <= last + 1 ? 0 : Math.Min(Math.Abs(first - target), Math.Abs(last + 1 - target)) / sorted.Length;
        }

        return Math.Abs((below / (double)sorted.Length) - q);
    }

    private static double Tolerance(double q) => Math.Min(q, 1 - q) switch
    {
        <= 0.001 => 0.0005,
        <= 0.01 => 0.001,
        _ => 0.005,
    };

    [TestCaseSource(nameof(Distributions))]
    public void Quantiles_HaveSmallRankError_ParticularlyAtTheTails(string distribution)
    {
        double[] data = Generate(distribution, 500_000, seed: 1);
        var digest = new TDigest();
        foreach (double value in data)
        {
            digest.Add(value);
        }

        double[] sorted = [.. data.Order()];
        foreach (double q in Quantiles)
        {
            RankError(sorted, digest.Quantile(q), q).Should().BeLessThanOrEqualTo(Tolerance(q), "q = {0}", q);
        }

        digest.Quantile(0).Should().Be(sorted[0]);
        digest.Quantile(1).Should().Be(sorted[^1]);
        digest.Min.Should().Be(sorted[0]);
        digest.Max.Should().Be(sorted[^1]);
        digest.Count.Should().Be(data.Length);
        digest.CentroidCount.Should().BeLessThanOrEqualTo((int)(digest.Compression * Math.PI / 2) + 10);
    }

    [TestCaseSource(nameof(Distributions))]
    public void Cdf_IsConsistentWithData(string distribution)
    {
        double[] data = Generate(distribution, 200_000, seed: 2);
        var digest = new TDigest();
        foreach (double value in data)
        {
            digest.Add(value);
        }

        double[] sorted = [.. data.Order()];
        foreach (double q in (double[])[0.01, 0.1, 0.5, 0.9, 0.99])
        {
            double x = sorted[(int)(q * sorted.Length)];
            double lowRank = Array.BinarySearch(sorted, x) is var i && i >= 0 ? sorted.Count(v => v < x) / (double)sorted.Length : q;
            double highRank = sorted.Count(v => v <= x) / (double)sorted.Length;
            digest.Cdf(x).Should().BeInRange(lowRank - 0.01, highRank + 0.01, "q = {0}", q);
        }

        digest.Cdf(sorted[0] - 1).Should().Be(0);
        digest.Cdf(sorted[^1]).Should().Be(1);
    }

    [Test]
    public void Merge_OfManyDigests_IsAsAccurateAsOne()
    {
        double[] data = Generate("lognormal", 500_000, seed: 3);
        var merged = new TDigest();
        for (int part = 0; part < 10; part++)
        {
            var digest = new TDigest();
            foreach (double value in data.AsSpan(part * 50_000, 50_000))
            {
                digest.Add(value);
            }

            merged.Merge(digest);
        }

        double[] sorted = [.. data.Order()];
        foreach (double q in Quantiles)
        {
            RankError(sorted, merged.Quantile(q), q).Should().BeLessThanOrEqualTo(Tolerance(q) * 2, "q = {0}", q);
        }

        merged.Count.Should().Be(data.Length);
        merged.Merge(new TDigest());
        merged.Count.Should().Be(data.Length);
    }

    [Test]
    public void SmallInputsWeightsSerializationAndValidation()
    {
        var digest = new TDigest(compression: 50);
        digest.Invoking(d => d.Quantile(0.5)).Should().Throw<InvalidOperationException>();
        digest.Invoking(d => d.Cdf(1)).Should().Throw<InvalidOperationException>();
        double.IsNaN(digest.Min).Should().BeTrue();

        digest.Add(5);
        digest.Quantile(0.5).Should().Be(5);
        digest.Add(1, weight: 3);
        digest.Count.Should().Be(4);
        digest.Quantile(0).Should().Be(1);
        digest.Quantile(1).Should().Be(5);
        digest.Quantile(0.25).Should().BeInRange(1, 5);

        TDigest copy = TDigest.FromBytes(digest.ToBytes());
        copy.Quantile(0.25).Should().Be(digest.Quantile(0.25));
        copy.Count.Should().Be(4);
        copy.Compression.Should().Be(50);

        digest.Clear();
        digest.Count.Should().Be(0);
        digest.Invoking(d => d.Add(double.NaN)).Should().Throw<ArgumentOutOfRangeException>();
        digest.Invoking(d => d.Add(double.PositiveInfinity)).Should().Throw<ArgumentOutOfRangeException>();
        digest.Invoking(d => d.Add(1, 0)).Should().Throw<ArgumentOutOfRangeException>();
        copy.Invoking(d => d.Quantile(1.5)).Should().Throw<ArgumentOutOfRangeException>();
        copy.Invoking(d => d.Merge(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new TDigest(5)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => TDigest.FromBytes(new byte[10])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TDigest.FromBytes(new byte[60])).Should().Throw<ArgumentException>();
    }
}

public class TopKTests
{
    [Test]
    public void SkewedStream_FindsTrueTopItemsWithinErrorBounds()
    {
        var random = new Random(4);
        var truth = new Dictionary<int, long>();
        var top = new TopK<int>(capacity: 200);
        for (int i = 0; i < 500_000; i++)
        {
            // Zipf-like: item k appears with probability ~ 1/k.
            int item = (int)Math.Floor(Math.Exp(random.NextDouble() * Math.Log(100_000)));
            top.Add(item);
            truth[item] = truth.GetValueOrDefault(item) + 1;
        }

        int[] trueTop10 = [.. truth.OrderByDescending(static kv => kv.Value).Take(10).Select(static kv => kv.Key)];
        top.GetTop(10).Select(static e => e.Item).Should().BeEquivalentTo(trueTop10);

        foreach (TopK<int>.Entry entry in top.GetTop(top.TrackedCount))
        {
            long actual = truth[entry.Item];
            actual.Should().BeInRange(entry.Count - entry.Error, entry.Count);
        }

        long threshold = top.TotalCount / top.Capacity;
        foreach ((int item, long count) in truth.Where(kv => kv.Value > threshold))
        {
            top.TryGetCount(item, out _, out _).Should().BeTrue("item {0} occurs {1} times", item, count);
        }

        top.TotalCount.Should().Be(500_000);
    }

    [Test]
    public void SmallCases_WeightsAndValidation()
    {
        var top = new TopK<string>(2, StringComparer.OrdinalIgnoreCase);
        top.Add("a", 5);
        top.Add("A", 2);
        top.Add("b");
        top.Add("c"); // evicts b (count 1): c gets count 2, error 1

        top.TryGetCount("a", out long a, out long aError).Should().BeTrue();
        (a, aError).Should().Be((7L, 0L));
        top.TryGetCount("c", out long c, out long cError).Should().BeTrue();
        (c, cError).Should().Be((2L, 1L));
        top.TryGetCount("b", out _, out _).Should().BeFalse();
        top.GetTop(5).Select(static e => e.Item).Should().Equal("a", "c");
        top.GetTop(0).Should().BeEmpty();
        top.TrackedCount.Should().Be(2);

        top.Clear();
        top.TrackedCount.Should().Be(0);
        top.TotalCount.Should().Be(0);
        top.Invoking(t => t.Add("x", 0)).Should().Throw<ArgumentOutOfRangeException>();
        top.Invoking(t => t.GetTop(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new TopK<int>(0)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
