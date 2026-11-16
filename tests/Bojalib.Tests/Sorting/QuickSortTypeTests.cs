using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

/// <summary>QuickSort has a separate fast path per built-in type; each is checked against the runtime's sort.</summary>
public class QuickSortTypeTests
{
    private static void AssertSortsLikeRuntime<T>(T[] input)
    {
        T[] expected = [.. input];
        Array.Sort(expected);
        T[] actual = [.. input];

        QuickSort.Sort(actual, (IComparer<T>?)null);

        actual.Should().Equal(expected);
    }

    [Test]
    public void Int32_MatchesRuntimeSort() =>
        Gen.Int.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 300);

    [Test]
    public void Int32_FewDistinctValues_MatchesRuntimeSort() =>
        Gen.Int[-3, 3].Array[0, 5000].Sample(AssertSortsLikeRuntime, iter: 200);

    [Test]
    public void UInt32_MatchesRuntimeSort() => Gen.UInt.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 200);

    [Test]
    public void Int64_MatchesRuntimeSort() => Gen.Long.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 200);

    [Test]
    public void UInt64_MatchesRuntimeSort() => Gen.ULong.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 200);

    [Test]
    public void Double_WithNaNAndSpecialValues_MatchesRuntimeSort() =>
        Gen.OneOf(Gen.Double, Gen.Const(double.NaN), Gen.Const(double.PositiveInfinity), Gen.Const(double.NegativeInfinity), Gen.Const(-0.0))
            .Array[0, 3000].Sample(input =>
            {
                double[] expected = [.. input];
                Array.Sort(expected);
                double[] actual = [.. input];
                QuickSort.Sort(actual);

                // -0.0 and 0.0 compare equal, so compare by value ordering rather than bit patterns.
                actual.Should().Equal(expected, static (a, b) => a.Equals(b) || (a == 0 && b == 0));
            }, iter: 200);

    [Test]
    public void Single_WithNaN_MatchesRuntimeSort() =>
        Gen.OneOf(Gen.Float, Gen.Const(float.NaN)).Array[0, 3000].Sample(input =>
        {
            float[] expected = [.. input];
            Array.Sort(expected);
            float[] actual = [.. input];
            QuickSort.Sort(actual);
            actual.Should().Equal(expected, static (a, b) => a.Equals(b) || (a == 0 && b == 0));
        }, iter: 200);

    [Test]
    public void Bytes_MatchRuntimeSort()
    {
        Gen.Byte.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 100);
        Gen.SByte.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 100);
    }

    [Test]
    public void SixteenBit_SmallAndLarge_MatchRuntimeSort()
    {
        Gen.Short.Array[0, 3000].Sample(AssertSortsLikeRuntime, iter: 50);
        Gen.UShort.Array[16_000, 40_000].Sample(AssertSortsLikeRuntime, iter: 5);
        Gen.Short.Array[16_000, 40_000].Sample(AssertSortsLikeRuntime, iter: 5);
        Gen.Char.Array[16_000, 40_000].Sample(AssertSortsLikeRuntime, iter: 5);
    }

    [Test]
    public void Strings_MatchRuntimeSort() =>
        Gen.String.Array[0, 500].Sample(input =>
        {
            string[] expected = [.. input];
            Array.Sort(expected, StringComparer.Ordinal);
            string[] actual = [.. input];
            QuickSort.Sort(actual, StringComparer.Ordinal);
            actual.Should().Equal(expected);
        }, iter: 100);

    [Test]
    public void DefaultComparerInstance_TakesFastPathAndSorts()
    {
        int[] array = [.. Enumerable.Range(0, 1000).Reverse()];

        QuickSort.Sort(array, Comparer<int>.Default);

        array.Should().BeInAscendingOrder();
    }

    [Test]
    public void KeysAndItems_SortTogether()
    {
        Gen.Int[0, 100].Array[0, 2000].Sample(keys =>
        {
            int[] items = [.. keys.Select(k => k * 10)];
            int[] sortedKeys = [.. keys];

            QuickSort.Sort(sortedKeys.AsSpan(), items.AsSpan());

            sortedKeys.Should().BeInAscendingOrder();
            items.Should().Equal(sortedKeys.Select(k => k * 10));
        }, iter: 100);
    }

    [Test]
    public void KeysAndItems_FloatKeysAndCustomComparer()
    {
        double[] keys = [3.5, double.NaN, -1, 2];
        string[] items = ["c", "nan", "a", "b"];

        QuickSort.Sort(keys.AsSpan(), items.AsSpan());
        items.Should().Equal("nan", "a", "b", "c");

        QuickSort.Sort(keys.AsSpan(), items.AsSpan(), Comparer<double>.Create(static (a, b) => b.CompareTo(a)));
        items.Should().Equal("c", "b", "a", "nan");
    }

    private static void CheckPackedKeySort<TKey>(Gen<TKey> keyGen)
        where TKey : notnull
    {
        keyGen.Array[0, 3000].Sample(input =>
        {
            TKey[] keys = [.. input];
            int[] items = [.. Enumerable.Range(0, keys.Length)];

            QuickSort.Sort(keys.AsSpan(), items.AsSpan());

            // Pairs stay together, keys come out in Comparer<TKey>.Default order, and equal keys keep their
            // original order (the packed path is stable).
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].Should().Be(input[items[i]]);
            }

            keys.Should().BeInAscendingOrder(Comparer<TKey>.Default);
            for (int i = 1; i < keys.Length; i++)
            {
                if (Comparer<TKey>.Default.Compare(keys[i - 1], keys[i]) == 0 && keys[i - 1].Equals(keys[i]))
                {
                    items[i - 1].Should().BeLessThan(items[i]);
                }
            }
        }, iter: 150);
    }

    [Test]
    public void KeysAndItems_PackedSimdPath_Int32UInt32Single()
    {
        CheckPackedKeySort(Gen.Int);
        CheckPackedKeySort(Gen.Int[-5, 5]);
        CheckPackedKeySort(Gen.UInt);
        CheckPackedKeySort(Gen.OneOf(Gen.Float, Gen.Const(float.NaN), Gen.Const(-0f), Gen.Const(0f), Gen.Const(float.NegativeInfinity)));
    }

    [Test]
    public void KeysAndItems_DifferentLengths_Throws()
    {
        Action act = () => QuickSort.Sort(new int[2].AsSpan(), new int[3].AsSpan());

        act.Should().Throw<ArgumentException>();
    }
}
