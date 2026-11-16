using System.Runtime.Intrinsics.X86;
using Bojalib.Sorting.Internal;

namespace Bojalib.Tests.Sorting;

public class VectorizedPartitionTests
{
    private static IEnumerable<int> Lengths() => [.. Enumerable.Range(64, 80), 255, 256, 257, 1000, 4099];

    [Test]
    public void Partition512_AllTypes_PartitionsCorrectly()
    {
        if (!Avx512F.IsSupported)
        {
            Assert.Ignore("AVX-512 not available on this machine.");
        }

        RunAllTypes(use512: true);
    }

    [Test]
    public void Partition256_AllTypes_PartitionsCorrectly()
    {
        if (!Avx2.IsSupported)
        {
            Assert.Ignore("AVX2 not available on this machine.");
        }

        RunAllTypes(use512: false);
    }

    private static void RunAllTypes(bool use512)
    {
        var random = new Random(99);
        Check(use512, random, r => r.Next(-50, 50));
        Check(use512, random, r => (uint)r.Next(0, 100) * 0x0101_0101u);
        Check(use512, random, r => r.NextInt64(-50, 50) << 30);
        Check(use512, random, r => (ulong)r.NextInt64(0, 100) << 40);
        Check(use512, random, r => (float)(r.Next(-50, 50) / 4.0));
        Check(use512, random, r => r.Next(-50, 50) / 8.0);
    }

    private static void Check<T>(bool use512, Random random, Func<Random, T> next) where T : IComparable<T>
    {
        foreach (int length in Lengths())
        {
            foreach (bool equalGoesRight in (ReadOnlySpan<bool>)[true, false])
            {
                T[] array = [.. Enumerable.Range(0, length).Select(_ => next(random))];
                T pivot = array[random.Next(length)];
                T[] original = [.. array];

                int leftCount = use512
                    ? VectorizedPartition.Partition512(ref array[0], length, pivot, equalGoesRight)
                    : VectorizedPartition.Partition256(ref array[0], length, pivot, equalGoesRight);

                bool GoesRight(T x) => equalGoesRight ? x.CompareTo(pivot) >= 0 : x.CompareTo(pivot) > 0;
                array.Take(leftCount).Where(GoesRight).Should().BeEmpty("{0} length {1}", typeof(T).Name, length);
                array.Skip(leftCount).Where(x => !GoesRight(x)).Should().BeEmpty("{0} length {1}", typeof(T).Name, length);
                array.Order().Should().Equal(original.Order(), "elements must be preserved");
            }
        }
    }
}
