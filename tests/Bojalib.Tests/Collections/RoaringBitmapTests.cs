using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class RoaringBitmapTests
{
    // Values clustered in a few chunks, so containers cross the 4096 array/bitmap threshold both ways.
    private static readonly Gen<uint> Clustered = Gen.Select(Gen.Int[0, 3], Gen.Int[0, 9000])
        .Select(static t => ((uint)t.Item1 << 16) | (uint)t.Item2);

    [Test]
    public void RandomOperations_MatchSortedSet()
    {
        Gen.Select(Clustered, Gen.Int[0, 5]).Array[0, 15_000].Sample(ops =>
        {
            var bitmap = new RoaringBitmap();
            var model = new SortedSet<uint>();
            foreach ((uint value, int op) in ops)
            {
                if (op == 0)
                {
                    bitmap.Remove(value).Should().Be(model.Remove(value));
                }
                else
                {
                    bitmap.Add(value).Should().Be(model.Add(value));
                }
            }

            bitmap.Count.Should().Be(model.Count);
            bitmap.Should().Equal(model);
            bitmap.ToArray().Should().Equal(model);
            if (model.Count > 0)
            {
                bitmap.Min.Should().Be(model.Min);
                bitmap.Max.Should().Be(model.Max);
                uint probe = model.ElementAt(model.Count / 2);
                bitmap.Rank(probe).Should().Be(model.Count(v => v <= probe));
                bitmap.Select(model.Count / 2).Should().Be(probe);
                bitmap.Contains(probe).Should().BeTrue();
            }
        }, iter: TestScale.Iterations(60));
    }

    [Test]
    public void SetOperations_MatchLinq()
    {
        Gen.Select(Clustered.Array[0, 12_000], Clustered.Array[0, 12_000]).Sample(t =>
        {
            (uint[] a, uint[] b) = t;
            var left = new RoaringBitmap(a);
            var right = new RoaringBitmap(b);
            var setA = new HashSet<uint>(a);
            var setB = new HashSet<uint>(b);

            (left | right).Should().Equal(setA.Union(setB).Order());
            (left & right).Should().Equal(setA.Intersect(setB).Order());
            (left ^ right).Should().Equal(setA.Except(setB).Union(setB.Except(setA)).Order());
            (left - right).Should().Equal(setA.Except(setB).Order());
            RoaringBitmap.BitwiseOr(left, right).Equals(left | right).Should().BeTrue();
            RoaringBitmap.BitwiseAnd(left, right).Equals(left & right).Should().BeTrue();
            RoaringBitmap.Xor(left, right).Equals(left ^ right).Should().BeTrue();
            RoaringBitmap.Subtract(left, right).Equals(left - right).Should().BeTrue();
            (left - right).Count.Should().Be(setA.Except(setB).Count());

            // The inputs aren't modified.
            left.Should().Equal(setA.Order());
        }, iter: TestScale.Iterations(60));
    }

    // Each chunk is filled at its own density, so the bitmaps mix array (sparse) and bitmap (dense) containers.
    private static SortedSet<uint> DenseSample(Random random, params double[] densities)
    {
        var values = new SortedSet<uint>();
        for (int chunk = 0; chunk < densities.Length; chunk++)
        {
            for (uint low = 0; low < 65536; low++)
            {
                if (random.NextDouble() < densities[chunk])
                {
                    values.Add(((uint)chunk << 16) | low);
                }
            }
        }

        return values;
    }

    [Test]
    public void DenseAndSparseContainers_AllOperationsMatchModel()
    {
        var random = new Random(11);
        double[][] mixes = [[0.02, 0.1, 0.5, 0.9], [0.9, 0.02, 0.2, 0.0], [0.0, 0.5, 0.03, 0.6]];
        foreach (double[] mixA in mixes)
        {
            foreach (double[] mixB in mixes)
            {
                SortedSet<uint> a = DenseSample(random, mixA);
                SortedSet<uint> b = DenseSample(random, mixB);
                var left = new RoaringBitmap(a);
                var right = new RoaringBitmap(b);

                (left | right).Should().Equal(a.Union(b).Order());
                (left & right).Should().Equal(a.Intersect(b).Order());
                (left ^ right).Should().Equal(a.Except(b).Union(b.Except(a)).Order());
                (left - right).Should().Equal(a.Except(b).Order());
                (right - left).Should().Equal(b.Except(a).Order());
            }
        }
    }

    [Test]
    public void DenseContainers_RankSelectMinMaxAndShrinkingBackToArrays()
    {
        var random = new Random(12);
        SortedSet<uint> model = DenseSample(random, 0.5, 0.0, 0.08, 0.95);
        var bitmap = new RoaringBitmap(model);
        uint[] sorted = [.. model];

        bitmap.Min.Should().Be(sorted[0]);
        bitmap.Max.Should().Be(sorted[^1]);
        foreach (int index in (int[])[0, 1, 17, 4095, 4096, 20_000, sorted.Length / 2, sorted.Length - 1])
        {
            bitmap.Select(index).Should().Be(sorted[index]);
            bitmap.Rank(sorted[index]).Should().Be(index + 1);
        }

        bitmap.Rank(sorted[0] - 1 + 1).Should().Be(1);
        bitmap.Equals(new RoaringBitmap(model)).Should().BeTrue();
        bitmap.ToBytes().Length.Should().Be(bitmap.SerializedSize);
        RoaringBitmap.FromBytes(bitmap.ToBytes()).Should().Equal(sorted);

        // Remove most values: dense containers turn back into arrays and empty ones disappear.
        foreach (uint value in sorted)
        {
            if (random.Next(10) != 0)
            {
                bitmap.Remove(value).Should().BeTrue();
                model.Remove(value);
            }
        }

        bitmap.Should().Equal(model);
        bitmap.Count.Should().Be(model.Count);
        bitmap.Max.Should().Be(model.Max);
        bitmap.Equals(new RoaringBitmap(model.Take(model.Count - 1))).Should().BeFalse();
        var differentValues = new RoaringBitmap(model.Select(static v => v ^ 1));
        bitmap.Equals(differentValues).Should().Be(false);

        foreach (uint value in model.ToArray())
        {
            bitmap.Remove(value);
        }

        bitmap.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void CorruptInput_IsRejected()
    {
        byte[] good = new RoaringBitmap([1, 70_000, 200_000]).ToBytes();

        byte[] tooManyContainers = [.. good];
        BitConverter.TryWriteBytes(tooManyContainers.AsSpan(4), 70_000);
        FluentActions.Invoking(() => RoaringBitmap.FromBytes(tooManyContainers)).Should().Throw<ArgumentException>();

        byte[] keysOutOfOrder = [.. good];
        keysOutOfOrder[8 + 4] = 0; // second container's key becomes 0, not greater than the first
        FluentActions.Invoking(() => RoaringBitmap.FromBytes(keysOutOfOrder)).Should().Throw<ArgumentException>();

        byte[] unsortedArray = new RoaringBitmap([1, 2]).ToBytes();
        (unsortedArray[^4], unsortedArray[^2]) = (unsortedArray[^2], unsortedArray[^4]);
        FluentActions.Invoking(() => RoaringBitmap.FromBytes(unsortedArray)).Should().Throw<ArgumentException>();

        // Hand-made run-container file: one container (key 0) holding a run that runs past the end of the chunk.
        byte[] badRun =
        [
            0x3B, 0x30, 0x00, 0x00, // cookie 12347, one container
            0x01,                   // container 0 is a run container
            0x00, 0x00, 0x09, 0x00, // key 0, cardinality - 1 = 9
            0x01, 0x00,             // one run
            0xFE, 0xFF, 0x09, 0x00, // start 65534, length - 1 = 9: past 65535
        ];
        FluentActions.Invoking(() => RoaringBitmap.FromBytes(badRun)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void AddRange_SetsEveryValueAcrossChunks()
    {
        var bitmap = new RoaringBitmap();
        bitmap.AddRange(65_000, 140_000);
        bitmap.AddRange(10, 20);
        bitmap.AddRange(uint.MaxValue - 5, uint.MaxValue);

        bitmap.Count.Should().Be(75_001 + 11 + 6);
        bitmap.Contains(65_000).Should().BeTrue();
        bitmap.Contains(140_000).Should().BeTrue();
        bitmap.Contains(140_001).Should().BeFalse();
        bitmap.Max.Should().Be(uint.MaxValue);
        bitmap.Rank(140_000).Should().Be(11 + 75_001);
        bitmap.Invoking(b => b.AddRange(5, 4)).Should().Throw<ArgumentException>();
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "roaring", name + ".bin");

    // Same values as generate.py builds with the reference implementation (CRoaring).
    private static IEnumerable<uint> FixtureValues(string name)
    {
        IEnumerable<uint> Range(uint start, uint end)
        {
            for (uint v = start; v < end; v++)
            {
                yield return v;
            }
        }

        return name switch
        {
            "empty" => [],
            "small" => [1, 2, 3, 1000, 65535, 65536, 70000, 1u << 20, 4_000_000_000, uint.MaxValue],
            "dense_chunk" => Range(0, 10_000).Append(200_000u),
            "multiples_of_3" => Range(0, 200_000).Select(static i => i * 3),
            "runs_few_containers" => Range(10, 1000).Concat(Range(5000, 5003)).Concat(Range(70_000, 140_000)),
            "runs_many_containers" => Enumerable.Range(0, 6).SelectMany(k => Range(((uint)k * 65536) + 10, ((uint)k * 65536) + 5000)),
            "runs_mixed" => Range(10, 20)
                .Concat(Range(65536 * 2, (65536 * 2) + 30_000))
                .Concat(Range(65536 * 5, 65536 * 6).Where(static v => v % 2 == 0))
                .Concat([(65536u * 9) + 7, (65536u * 9) + 9000]),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [TestCase("empty")]
    [TestCase("small")]
    [TestCase("dense_chunk")]
    [TestCase("multiples_of_3")]
    public void Serialization_MatchesReferenceImplementationByteForByte(string name)
    {
        byte[] reference = File.ReadAllBytes(Fixture(name));
        var bitmap = new RoaringBitmap(FixtureValues(name));

        bitmap.ToBytes().Should().Equal(reference);
        bitmap.SerializedSize.Should().Be(reference.Length);
        RoaringBitmap.FromBytes(reference).Should().Equal(FixtureValues(name));
    }

    [TestCase("runs_few_containers")]
    [TestCase("runs_many_containers")]
    [TestCase("runs_mixed")]
    public void Deserialization_ReadsReferenceRunContainers(string name)
    {
        RoaringBitmap bitmap = RoaringBitmap.FromBytes(File.ReadAllBytes(Fixture(name)));

        bitmap.Should().Equal(FixtureValues(name).Order());

        // And what we write back reads the same.
        RoaringBitmap.FromBytes(bitmap.ToBytes()).Equals(bitmap).Should().BeTrue();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SetOperations_MatchReferenceImplementationByteForByte(int testCase)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "roaring", "setops");
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(directory, $"case{testCase}_{name}.bin"));
        RoaringBitmap a = RoaringBitmap.FromBytes(Read("a"));
        RoaringBitmap b = RoaringBitmap.FromBytes(Read("b"));

        (a | b).ToBytes().Should().Equal(Read("or"));
        (a & b).ToBytes().Should().Equal(Read("and"));
        (a ^ b).ToBytes().Should().Equal(Read("xor"));
        (a - b).ToBytes().Should().Equal(Read("andnot"));
        a.ToBytes().Should().Equal(Read("a"));
    }

    [Test]
    public void InvalidInput_IsRejected()
    {
        FluentActions.Invoking(() => RoaringBitmap.FromBytes([1, 2, 3, 4, 0, 0, 0, 0])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => RoaringBitmap.FromBytes([0x3A, 0x30, 0, 0])).Should().Throw<ArgumentException>();
        byte[] truncated = new RoaringBitmap([1, 2, 3]).ToBytes()[..^2];
        FluentActions.Invoking(() => RoaringBitmap.FromBytes(truncated)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void EqualityEnumerationAndBasics()
    {
        RoaringBitmap a = [5, 1, 70_000];
        var b = new RoaringBitmap([1, 5, 70_000]);

        a.Equals(b).Should().BeTrue();
        a.Equals((object)b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Equals(new RoaringBitmap([1, 5])).Should().BeFalse();
        IReadOnlyCollection<uint> asCollection = a;
        asCollection.Count.Should().Be(3);
        RoaringBitmap? none = null;
        b.Equals(none).Should().BeFalse();

        RoaringBitmap.Enumerator e = a.GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.Current.Should().Be(1);
        a.Add(9);
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();

        a.Remove(12345).Should().BeFalse();
        a.Clear();
        a.IsEmpty.Should().BeTrue();
        a.Invoking(x => _ = x.Min).Should().Throw<InvalidOperationException>();
        a.Invoking(x => _ = x.Max).Should().Throw<InvalidOperationException>();
        a.Invoking(x => x.Select(0)).Should().Throw<ArgumentOutOfRangeException>();
        a.Invoking(x => x.Select(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new RoaringBitmap(null!)).Should().Throw<ArgumentNullException>();
    }
}
