using System.Numerics;
using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

public class RadixSortTests
{
    private static void Check<T>(Gen<T> gen) where T : unmanaged, IBinaryInteger<T>, IMinMaxValue<T> =>
        gen.Array[0, 3000].Sample(input =>
        {
            T[] expected = [.. input.Order()];
            T[] actual = [.. input];

            RadixSort.Sort<T>(actual);

            actual.Should().Equal(expected);
        }, iter: TestScale.Iterations(100));

    [Test]
    public void AllIntegerTypes_MatchSortedOrder()
    {
        Check(Gen.Byte);
        Check(Gen.SByte);
        Check(Gen.Short);
        Check(Gen.UShort);
        Check(Gen.Int);
        Check(Gen.UInt);
        Check(Gen.Long);
        Check(Gen.ULong);
        Check(Gen.Long.Select(Gen.Long, static (hi, lo) => new Int128((ulong)hi, (ulong)lo)));
        Check(Gen.Char);
    }

    [Test]
    public void NarrowValuesInWideType_StillSortsCorrectly()
    {
        // Upper bytes are identical, so those passes are skipped.
        Check(Gen.Long[-1000, 1000]);
    }

    [Test]
    public void ExtremeValues_SortCorrectly()
    {
        int[] array = [.. Enumerable.Range(0, 1000).Select(i => (i % 4) switch { 0 => int.MinValue, 1 => int.MaxValue, 2 => 0, _ => -1 })];
        int[] expected = [.. array.Order()];

        RadixSort.Sort<int>(array);

        array.Should().Equal(expected);
    }
}
