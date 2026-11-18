using Bojalib.Probabilistic;
using CsCheck;

namespace Bojalib.Tests.Probabilistic;

public class BinaryFuseFilterTests
{
    [Test]
    public void NeverReportsFalseNegatives_IncludingDuplicatesAndTinySets()
    {
        Gen.Select(Gen.Long.Array[0, 3000], Gen.Bool).Sample(t =>
        {
            (long[] items, bool wide) = t;
            var filter = new BinaryFuseFilter<long>(items.Concat(items.Take(10)), wide ? 16 : 8);

            filter.Count.Should().Be(items.Distinct().Count());
            items.Should().OnlyContain(i => filter.MightContain(i));
        }, iter: 300);
    }

    [TestCase(8, 0.0020, 0.0060)]
    [TestCase(16, 0.0, 0.0002)]
    public void FalsePositiveRate_MatchesFingerprintSize(int bits, double low, double high)
    {
        var filter = new BinaryFuseFilter<string>(Enumerable.Range(0, 200_000).Select(static i => $"member-{i}"), bits);

        int falsePositives = Enumerable.Range(0, 200_000).Count(i => filter.MightContain($"other-{i}"));

        (falsePositives / 200_000.0).Should().BeInRange(low, high);
        filter.FalsePositiveRate.Should().Be(1.0 / (1 << bits));
        filter.FingerprintBits.Should().Be(bits);
    }

    [Test]
    public void Size_IsAboutNineBitsPerItemAt8Bits()
    {
        var filter = new BinaryFuseFilter<int>(Enumerable.Range(0, 1_000_000));

        double bitsPerItem = filter.ByteCount * 8.0 / filter.Count;

        bitsPerItem.Should().BeInRange(8.5, 9.5);
    }

    [Test]
    public void Serialization_RoundTrips()
    {
        foreach (int bits in (int[])[8, 16])
        {
            var filter = new BinaryFuseFilter<string>(["alpha", "beta", "gamma"], bits);
            filter.IsPortable.Should().BeTrue();

            BinaryFuseFilter<string> copy = BinaryFuseFilter<string>.FromBytes(filter.ToBytes());

            copy.MightContain("beta").Should().BeTrue();
            copy.Count.Should().Be(3);
            copy.FingerprintBits.Should().Be(bits);
            copy.ByteCount.Should().Be(filter.ByteCount);
        }
    }

    private sealed record Person(string Name);

    [Test]
    public void EmptySetCustomHasherAndValidation()
    {
        var empty = new BinaryFuseFilter<int>([]);
        empty.MightContain(1).Should().BeFalse();
        empty.Count.Should().Be(0);

        var people = new BinaryFuseFilter<Person>([new("Ada"), new("Alan")], hasher: p => Bojalib.Hashing.XxHash64.Hash(p.Name.AsSpan()));
        people.MightContain(new Person("Ada")).Should().BeTrue();
        people.IsPortable.Should().BeTrue();
        new BinaryFuseFilter<Person>([new("Ada")]).IsPortable.Should().BeFalse();

        FluentActions.Invoking(() => new BinaryFuseFilter<int>(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BinaryFuseFilter<int>([1], 12)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => BinaryFuseFilter<int>.FromBytes(new byte[10])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BinaryFuseFilter<int>.FromBytes(new byte[64])).Should().Throw<ArgumentException>();
    }
}

public class HyperLogLogTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(1_000)]
    [TestCase(30_000)]
    [TestCase(300_000)]
    [TestCase(2_000_000)]
    public void Estimate_IsWithinFourStandardErrors(int distinct)
    {
        var sketch = new HyperLogLog<int>();
        for (int i = 0; i < distinct; i++)
        {
            sketch.Add(i);
            sketch.Add(i); // duplicates must not count
        }

        long estimate = sketch.Estimate();

        double tolerance = Math.Max(2, 4 * sketch.StandardError * distinct);
        ((double)estimate).Should().BeApproximately(distinct, tolerance);
    }

    [Test]
    public void LowPrecision_StillUnbiasedAcrossRange()
    {
        foreach (int distinct in (int[])[100, 5_000, 200_000])
        {
            var sketch = new HyperLogLog<string>(precision: 8);
            for (int i = 0; i < distinct; i++)
            {
                sketch.Add($"user-{i}");
            }

            ((double)sketch.Estimate()).Should().BeApproximately(distinct, 4 * sketch.StandardError * distinct, "n = {0}", distinct);
        }
    }

    [Test]
    public void Union_CountsDistinctItemsOfBoth()
    {
        var a = new HyperLogLog<int>();
        var b = new HyperLogLog<int>();
        for (int i = 0; i < 60_000; i++)
        {
            a.Add(i);          // 0 .. 59,999
            b.Add(i + 40_000); // 40,000 .. 99,999
        }

        a.UnionWith(b);

        ((double)a.Estimate()).Should().BeApproximately(100_000, 4 * a.StandardError * 100_000);
        a.Invoking(s => s.UnionWith(new HyperLogLog<int>(10))).Should().Throw<ArgumentException>();
        a.Invoking(s => s.UnionWith(null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddSerializationAndValidation()
    {
        var sketch = new HyperLogLog<string>(precision: 10);
        sketch.Add("x").Should().BeTrue();
        sketch.Add("x").Should().BeFalse();
        sketch.ByteCount.Should().Be(1024);
        sketch.Precision.Should().Be(10);

        HyperLogLog<string> copy = HyperLogLog<string>.FromBytes(sketch.ToBytes());
        copy.Estimate().Should().Be(sketch.Estimate());

        sketch.Clear();
        sketch.Estimate().Should().Be(0);
        FluentActions.Invoking(() => new HyperLogLog<int>(3)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new HyperLogLog<int>(19)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => HyperLogLog<int>.FromBytes([5, 1, 2])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => HyperLogLog<int>.FromBytes([])).Should().Throw<ArgumentException>();

        // Regression (found by fuzzing): a register value larger than any real rank used to crash Estimate().
        byte[] corrupt = new HyperLogLog<int>(4).ToBytes();
        corrupt[3] = 200;
        FluentActions.Invoking(() => HyperLogLog<int>.FromBytes(corrupt)).Should().Throw<ArgumentException>();
    }
}

public class CountMinSketchTests
{
    private static (int Item, int Count)[] ZipfStream(int items, int total, int seed)
    {
        var random = new Random(seed);
        double[] weights = [.. Enumerable.Range(1, items).Select(static r => 1.0 / r)];
        double sum = weights.Sum();
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < total; i++)
        {
            double x = random.NextDouble() * sum;
            int item = 0;
            while (x > weights[item] && item < items - 1)
            {
                x -= weights[item];
                item++;
            }

            counts[item] = counts.GetValueOrDefault(item) + 1;
        }

        return [.. counts.Select(static kv => (kv.Key, kv.Value))];
    }

    [Test]
    public void NeverUndercounts_AndStaysWithinErrorBound()
    {
        (int Item, int Count)[] truth = ZipfStream(items: 5_000, total: 200_000, seed: 1);
        var plain = new CountMinSketch<int>(epsilon: 0.001, delta: 0.01);
        var conservative = new CountMinSketch<int>(epsilon: 0.001, delta: 0.01, conservativeUpdate: true);
        foreach ((int item, int count) in truth)
        {
            plain.Add(item, count);
            for (int i = 0; i < count; i++)
            {
                conservative.Add(item);
            }
        }

        long bound = (long)Math.Ceiling(0.001 * plain.TotalCount);
        int beyondBound = 0;
        foreach ((int item, int count) in truth)
        {
            long estimate = plain.Estimate(item);
            estimate.Should().BeGreaterThanOrEqualTo(count);
            conservative.Estimate(item).Should().BeInRange(count, estimate);
            if (estimate - count > bound)
            {
                beyondBound++;
            }
        }

        (beyondBound / (double)truth.Length).Should().BeLessThanOrEqualTo(0.01);
        plain.TotalCount.Should().Be(200_000);
    }

    [Test]
    public void UnionSerializationAndValidation()
    {
        var a = new CountMinSketch<string>(width: 100, depth: 4);
        var b = new CountMinSketch<string>(width: 100, depth: 4);
        a.Add("x", 3);
        b.Add("x", 4);
        b.Add("y");

        a.UnionWith(b);
        a.Estimate("x").Should().BeGreaterThanOrEqualTo(7);
        a.TotalCount.Should().Be(8);

        CountMinSketch<string> copy = CountMinSketch<string>.FromBytes(a.ToBytes());
        copy.Estimate("x").Should().Be(a.Estimate("x"));
        copy.Width.Should().Be(100);
        copy.Depth.Should().Be(4);
        copy.ConservativeUpdate.Should().BeFalse();

        a.Clear();
        a.Estimate("x").Should().Be(0);
        a.TotalCount.Should().Be(0);
        a.Invoking(s => s.Add("x", -1)).Should().Throw<ArgumentOutOfRangeException>();
        a.Invoking(s => s.UnionWith(new CountMinSketch<string>(10, 4))).Should().Throw<ArgumentException>();
        a.Invoking(s => s.UnionWith(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new CountMinSketch<int>(0.0, 0.1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new CountMinSketch<int>(0.1, 1.0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new CountMinSketch<int>(0, 1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CountMinSketch<int>.FromBytes(new byte[5])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CountMinSketch<int>.FromBytes(new byte[30])).Should().Throw<ArgumentException>();
    }
}
