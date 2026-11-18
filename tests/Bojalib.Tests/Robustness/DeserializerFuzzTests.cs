using System.Diagnostics;
using Bojalib.Collections;
using Bojalib.Probabilistic;

namespace Bojalib.Tests.Robustness;

/// <summary>
/// Feeds every <c>FromBytes</c> random mutations of valid serialized data (bit flips, byte overwrites, truncation,
/// extension, chunk duplication and swaps) and requires that it either returns a usable object or throws
/// <see cref="ArgumentException"/> — never another exception type, a crash, a hang or a huge allocation.
/// </summary>
public class DeserializerFuzzTests
{
    private const long MaxAllocationBytes = 256L * 1024 * 1024;

    private static byte[] Mutate(byte[] input, Random random)
    {
        var bytes = new List<byte>(input);
        int edits = random.Next(1, 6);
        for (int e = 0; e < edits; e++)
        {
            int position = bytes.Count == 0 ? 0 : random.Next(bytes.Count);
            switch (random.Next(7))
            {
                case 0 when bytes.Count > 0:
                    bytes[position] ^= (byte)(1 << random.Next(8));
                    break;
                case 1 when bytes.Count > 0:
                    bytes[position] = (byte)random.Next(256);
                    break;
                case 2 when bytes.Count > 0:
                    bytes.RemoveRange(position, random.Next(1, Math.Min(64, bytes.Count - position) + 1));
                    break;
                case 3:
                    bytes.InsertRange(position, Enumerable.Range(0, random.Next(1, 16)).Select(_ => (byte)random.Next(256)));
                    break;
                case 4 when bytes.Count > 0:
                    bytes.RemoveRange(position, bytes.Count - position); // truncate
                    break;
                case 5 when bytes.Count > 8:
                    // Interesting integers in header-sized positions: 0, -1, int.MaxValue, small counts.
                    int[] interesting = [0, -1, int.MaxValue, int.MinValue, 1, 4, 65535, 65536, 4096, 4097];
                    int at = random.Next(Math.Min(bytes.Count - 4, 64));
                    byte[] value = BitConverter.GetBytes(interesting[random.Next(interesting.Length)]);
                    for (int k = 0; k < 4; k++) bytes[at + k] = value[k];
                    break;
                default:
                    if (bytes.Count > 4)
                    {
                        int length = random.Next(1, Math.Min(32, bytes.Count) + 1);
                        int from = random.Next(bytes.Count - length + 1);
                        bytes.InsertRange(random.Next(bytes.Count), bytes.GetRange(from, length));
                    }

                    break;
            }
        }

        return [.. bytes];
    }

    private static void Fuzz(string name, byte[][] seeds, Func<byte[], object> parse, Action<object> exercise)
    {
        var random = new Random(name.GetHashCode(StringComparison.Ordinal) ^ 12345);
        int iterations = TestScale.Iterations(3000);
        int accepted = 0;
        for (int i = 0; i < iterations; i++)
        {
            byte[] input = Mutate(seeds[random.Next(seeds.Length)], random);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            try
            {
                object result = parse(input);
                exercise(result); // a parsed object must be usable, not a time bomb
                accepted++;
            }
            catch (ArgumentException)
            {
                // Expected for malformed input.
            }
            catch (Exception e)
            {
                Assert.Fail($"{name}: input #{i} ({Convert.ToHexString(input.AsSpan(0, Math.Min(64, input.Length)))}…) threw {e.GetType().Name}: {e.Message}");
            }

            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "{0} input #{1} must not hang", name, i);
            (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(MaxAllocationBytes, "{0} input #{1} must not allocate unboundedly", name, i);
        }

        TestContext.Out.WriteLine($"{name}: {accepted}/{iterations} mutated inputs parsed successfully, the rest rejected cleanly.");
    }

    [Test]
    public void RoaringBitmap_FromBytes()
    {
        var sparse = new RoaringBitmap([1, 2, 3, 70_000, 4_000_000_000]);
        var dense = new RoaringBitmap();
        dense.AddRange(0, 20_000);
        dense.AddRange(200_000, 205_000);
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "roaring");
        byte[][] seeds = [sparse.ToBytes(), dense.ToBytes(), .. Directory.GetFiles(fixtures, "*.bin").Select(File.ReadAllBytes)];

        Fuzz(nameof(RoaringBitmap), seeds, static b => RoaringBitmap.FromBytes(b), static o =>
        {
            var bitmap = (RoaringBitmap)o;
            long count = bitmap.Count;
            long enumerated = 0;
            foreach (uint _ in bitmap)
            {
                enumerated++;
            }

            enumerated.Should().Be(count);
            RoaringBitmap.FromBytes(bitmap.ToBytes()).Equals(bitmap).Should().BeTrue();
        });
    }

    [Test]
    public void BloomFilter_FromBytes()
    {
        var filter = new BloomFilter<int>(1000, 0.01);
        filter.Add(42);
        Fuzz(nameof(BloomFilter<int>), [filter.ToBytes()], static b => BloomFilter<int>.FromBytes(b), static o =>
        {
            var f = (BloomFilter<int>)o;
            _ = f.MightContain(42);
            f.Add(7);
            f.MightContain(7).Should().BeTrue();
        });
    }

    [Test]
    public void BinaryFuseFilter_FromBytes()
    {
        byte[][] seeds =
        [
            new BinaryFuseFilter<int>(Enumerable.Range(0, 500)).ToBytes(),
            new BinaryFuseFilter<int>(Enumerable.Range(0, 500), 16).ToBytes(),
            new BinaryFuseFilter<int>([1]).ToBytes(),
        ];
        Fuzz(nameof(BinaryFuseFilter<int>), seeds, static b => BinaryFuseFilter<int>.FromBytes(b), static o =>
        {
            var f = (BinaryFuseFilter<int>)o;
            for (int i = 0; i < 100; i++)
            {
                _ = f.MightContain(i);
            }
        });
    }

    [Test]
    public void HyperLogLog_FromBytes()
    {
        var sketch = new HyperLogLog<int>(10);
        for (int i = 0; i < 1000; i++) sketch.Add(i);
        Fuzz(nameof(HyperLogLog<int>), [sketch.ToBytes(), new HyperLogLog<int>(4).ToBytes()], static b => HyperLogLog<int>.FromBytes(b), static o =>
        {
            var s = (HyperLogLog<int>)o;
            s.Estimate().Should().BeGreaterThanOrEqualTo(0);
            s.Add(1);
        });
    }

    [Test]
    public void CountMinSketch_FromBytes()
    {
        var sketch = new CountMinSketch<int>(64, 4);
        sketch.Add(1, 10);
        Fuzz(nameof(CountMinSketch<int>), [sketch.ToBytes()], static b => CountMinSketch<int>.FromBytes(b), static o =>
        {
            var s = (CountMinSketch<int>)o;
            _ = s.Estimate(1);
            s.Add(2);
        });
    }

    [Test]
    public void TDigest_FromBytes()
    {
        var digest = new TDigest();
        for (int i = 0; i < 10_000; i++) digest.Add(i % 977);
        var tiny = new TDigest();
        tiny.Add(3);
        Fuzz(nameof(TDigest), [digest.ToBytes(), tiny.ToBytes()], static b => TDigest.FromBytes(b), static o =>
        {
            var d = (TDigest)o;
            if (d.Count > 0)
            {
                _ = d.Quantile(0.5);
                _ = d.Cdf(1);
            }

            d.Add(5);
        });
    }
}
