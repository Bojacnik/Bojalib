using Bojalib.Collections;
using Bojalib.Hashing;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class XxHash64Tests
{
    [Test]
    public void KnownVectors()
    {
        XxHash64.Hash(""u8).Should().Be(0xEF46DB3751D8E999UL);
        XxHash64.Hash("a"u8).Should().Be(0xD24EC4F1A98C6E5BUL);
        XxHash64.Hash("abc"u8).Should().Be(0x44BC2CF5AD770999UL);
    }

    [Test]
    public void MatchesSystemIOHashing_ForAllLengthsAndSeeds()
    {
        Gen.Select(Gen.Byte.Array[0, 200], Gen.ULong).Sample(t =>
        {
            (byte[] data, ulong seed) = t;
            ulong expected = System.IO.Hashing.XxHash64.HashToUInt64(data, (long)seed);
            XxHash64.Hash(data, seed).Should().Be(expected);
        }, iter: 3000);
    }

    [Test]
    public void CharOverload_HashesUtf16Bytes()
    {
        XxHash64.Hash("hello".AsSpan()).Should().Be(XxHash64.Hash(System.Text.Encoding.Unicode.GetBytes("hello")));
    }
}

public class BloomFilterTests
{
    [Test]
    public void NeverReportsFalseNegatives()
    {
        Gen.Int.Array[0, 2000].Sample(items =>
        {
            var filter = new BloomFilter<int>(Math.Max(1, items.Length), 0.01);
            foreach (int item in items)
            {
                filter.Add(item);
            }

            items.Should().OnlyContain(i => filter.MightContain(i));
        }, iter: 100);
    }

    [Test]
    public void FalsePositiveRate_IsNearTarget()
    {
        var filter = new BloomFilter<string>(100_000, 0.01);
        for (int i = 0; i < 100_000; i++)
        {
            filter.Add($"member-{i}");
        }

        int falsePositives = Enumerable.Range(0, 100_000).Count(i => filter.MightContain($"other-{i}"));
        double rate = falsePositives / 100_000.0;

        rate.Should().BeLessThan(0.02);
        filter.EstimateFalsePositiveRate().Should().BeInRange(0.002, 0.02);
    }

    [Test]
    public void Serialization_RoundTripsForPortableTypes()
    {
        var filter = new BloomFilter<string>(1000, 0.01);
        filter.Add("hello");
        filter.IsPortable.Should().BeTrue();

        BloomFilter<string> copy = BloomFilter<string>.FromBytes(filter.ToBytes());

        copy.MightContain("hello").Should().BeTrue();
        copy.ByteCount.Should().Be(filter.ByteCount);
        copy.BlockCount.Should().Be(filter.BlockCount);
        FluentActions.Invoking(() => BloomFilter<string>.FromBytes(new byte[33])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BloomFilter<string>.FromBytes([])).Should().Throw<ArgumentException>();
    }

    [Test]
    public void UnionAndIntersection()
    {
        var a = new BloomFilter<long>(1000, 0.01);
        var b = new BloomFilter<long>(1000, 0.01);
        a.Add(1);
        b.Add(2);
        b.Add(1);

        var union = BloomFilter<long>.FromBytes(a.ToBytes());
        union.UnionWith(b);
        union.MightContain(1).Should().BeTrue();
        union.MightContain(2).Should().BeTrue();

        a.IntersectWith(b);
        a.MightContain(1).Should().BeTrue();

        a.Invoking(f => f.UnionWith(new BloomFilter<long>(1_000_000, 0.01))).Should().Throw<ArgumentException>();
        a.Invoking(f => f.IntersectWith(null!)).Should().Throw<ArgumentNullException>();
        a.Clear();
        a.MightContain(1).Should().BeFalse();
    }

    private sealed record Person(string Name);

    [Test]
    public void NonPrimitiveTypes_UseComparerOrCustomHasher()
    {
        var byEquality = new BloomFilter<Person>(100, 0.01);
        byEquality.Add(new Person("Ada"));
        byEquality.MightContain(new Person("Ada")).Should().BeTrue();
        byEquality.IsPortable.Should().BeFalse();
        byEquality.Add(null!);
        byEquality.MightContain(null!).Should().BeTrue();

        var custom = new BloomFilter<Person>(100, 0.01, p => XxHash64.Hash(p.Name.AsSpan()));
        custom.Add(new Person("Ada"));
        custom.IsPortable.Should().BeTrue();
        BloomFilter<Person>.FromBytes(custom.ToBytes(), p => XxHash64.Hash(p.Name.AsSpan())).MightContain(new Person("Ada")).Should().BeTrue();
    }

    [Test]
    public void ArgumentsAreValidated()
    {
        FluentActions.Invoking(() => new BloomFilter<int>(0, 0.01)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BloomFilter<int>(10, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BloomFilter<int>(10, 1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BloomFilter<int>(10, double.NaN)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
