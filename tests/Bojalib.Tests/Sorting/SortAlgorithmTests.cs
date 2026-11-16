using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

/// <summary>CompareTo returns the raw difference rather than -1/0/1, which the IComparable contract allows.</summary>
public readonly record struct Score(int Value) : IComparable<Score>
{
    public int CompareTo(Score other) => Value - other.Value;

    public static bool operator <(Score left, Score right) => left.CompareTo(right) < 0;

    public static bool operator <=(Score left, Score right) => left.CompareTo(right) <= 0;

    public static bool operator >(Score left, Score right) => left.CompareTo(right) > 0;

    public static bool operator >=(Score left, Score right) => left.CompareTo(right) >= 0;
}

/// <summary>Shared tests every sorting algorithm must pass.</summary>
public abstract class SortAlgorithmTests
{
    protected abstract void Sort<T>(Span<T> span) where T : IComparable<T>?;

    protected abstract void Sort<T>(Span<T> span, IComparer<T>? comparer);

    protected abstract void Sort<T>(Span<T> span, Comparison<T> comparison);

    /// <summary>Largest input for the generated-pattern tests; O(n²) algorithms use a smaller one.</summary>
    protected virtual int LargeLength => 100_000;

    public static IEnumerable<int[]> SmallArrays()
    {
        yield return [];
        yield return [1];
        yield return [2, 1];
        yield return [2, 3, 1, 7, 3, 20, 5236236, 0, 2];
        yield return [-5, 10, -5, 0, 3, 3, 3, -100];
        yield return [int.MaxValue, int.MinValue, 0, -1, 1];
    }

    public static IEnumerable<string> Patterns() => ["random", "sorted", "reversed", "equal", "organ-pipe", "few-distinct", "sawtooth"];

    [TestCaseSource(nameof(SmallArrays))]
    public void Sort_SmallArrays_SortsAscending(int[] array)
    {
        int[] expected = [.. array.Order()];

        Sort(array);

        array.Should().Equal(expected);
    }

    [TestCaseSource(nameof(Patterns))]
    public void Sort_LargePatterns_MatchesBuiltInSort(string pattern)
    {
        int[] array = Generate(pattern, LargeLength);
        int[] expected = [.. array.Order()];

        Sort(array);

        array.Should().Equal(expected);
    }

    [Test]
    public void Sort_RandomArraysOfEveryLength_MatchesBuiltInSort()
    {
        var random = new Random(12345);
        for (int length = 0; length < 300; length++)
        {
            int[] array = [.. Enumerable.Range(0, length).Select(_ => random.Next(-50, 50))];
            int[] expected = [.. array.Order()];

            Sort(array);

            array.Should().Equal(expected, "length {0}", length);
        }
    }

    [Test]
    public void Sort_CompareToReturnsValuesOtherThanOne_SortsAscending()
    {
        Score[] array = [new(50), new(-7), new(3), new(1000), new(3), new(-200)];
        Score[] expected = [.. array.Order()];

        Sort(array);

        array.Should().Equal(expected);
    }

    [Test]
    public void Sort_NullElements_SortFirst()
    {
        string?[] array = ["pear", null, "apple", null, "fig"];

        Sort(array);

        array.Should().Equal(null, null, "apple", "fig", "pear");
    }

    [Test]
    public void Sort_Slice_OnlySortsTheSlice()
    {
        int[] array = [9, 5, 4, 3, 0];

        Sort(array.AsSpan(1, 3));

        array.Should().Equal(9, 3, 4, 5, 0);
    }

    [Test]
    public void Sort_WithComparer_UsesComparer()
    {
        int[] array = [3, 1, 2];

        Sort(array, Comparer<int>.Create(static (a, b) => b.CompareTo(a)));

        array.Should().Equal(3, 2, 1);
    }

    [Test]
    public void Sort_WithNullComparer_UsesDefaultComparer()
    {
        int[] array = [3, 1, 2];

        Sort(array, (IComparer<int>?)null);

        array.Should().Equal(1, 2, 3);
    }

    [Test]
    public void Sort_WithComparison_UsesComparison()
    {
        string[] array = ["ccc", "a", "bb"];

        Sort(array, static (a, b) => a.Length.CompareTo(b.Length));

        array.Should().Equal("a", "bb", "ccc");
    }

    [Test]
    public void Sort_WithNullComparison_Throws()
    {
        Action act = () => Sort(new int[1], (Comparison<int>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static int[] Generate(string pattern, int length)
    {
        var random = new Random(42);
        return pattern switch
        {
            "random" => [.. Enumerable.Range(0, length).Select(_ => random.Next())],
            "sorted" => [.. Enumerable.Range(0, length)],
            "reversed" => [.. Enumerable.Range(0, length).Reverse()],
            "equal" => [.. Enumerable.Repeat(7, length)],
            "organ-pipe" => [.. Enumerable.Range(0, length).Select(i => i < length / 2 ? i : length - i)],
            "few-distinct" => [.. Enumerable.Range(0, length).Select(_ => random.Next(4))],
            "sawtooth" => [.. Enumerable.Range(0, length).Select(i => i % 97)],
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }
}

public sealed class QuickSortTests : SortAlgorithmTests
{
    protected override void Sort<T>(Span<T> span) => QuickSort.Sort(span);

    protected override void Sort<T>(Span<T> span, IComparer<T>? comparer) => QuickSort.Sort(span, comparer);

    protected override void Sort<T>(Span<T> span, Comparison<T> comparison) => QuickSort.Sort(span, comparison);

    private readonly struct DescendingComparer : IComparer<int>
    {
        public int Compare(int x, int y) => y.CompareTo(x);
    }

    [Test]
    public void Sort_WithStructComparer_UsesComparer()
    {
        int[] array = [.. Enumerable.Range(0, 1000)];

        QuickSort.Sort(array.AsSpan(), new DescendingComparer());

        array.Should().BeInDescendingOrder();
    }

    [Test]
    public void Sort_WithNullGenericComparer_Throws()
    {
        Action act = () => QuickSort.Sort<int, IComparer<int>>(new int[1], null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class HeapSortTests : SortAlgorithmTests
{
    protected override void Sort<T>(Span<T> span) => HeapSort.Sort(span);

    protected override void Sort<T>(Span<T> span, IComparer<T>? comparer) => HeapSort.Sort(span, comparer);

    protected override void Sort<T>(Span<T> span, Comparison<T> comparison) => HeapSort.Sort(span, comparison);
}

/// <summary>Extra guarantee for stable algorithms: equal elements keep their original order.</summary>
public abstract class StableSortAlgorithmTests : SortAlgorithmTests
{
    [Test]
    public void Sort_EqualKeys_KeepOriginalOrder()
    {
        (int Key, int Order)[] array = [.. Enumerable.Range(0, 200).Select(i => (Key: i % 7, Order: i))];

        Sort(array, static (a, b) => a.Key.CompareTo(b.Key));

        array.Should().BeInAscendingOrder(static x => x.Key).And.ThenBeInAscendingOrder(static x => x.Order);
    }
}

public sealed class InsertionSortTests : StableSortAlgorithmTests
{
    protected override int LargeLength => 3_000;

    protected override void Sort<T>(Span<T> span) => InsertionSort.Sort(span);

    protected override void Sort<T>(Span<T> span, IComparer<T>? comparer) => InsertionSort.Sort(span, comparer);

    protected override void Sort<T>(Span<T> span, Comparison<T> comparison) => InsertionSort.Sort(span, comparison);
}

public sealed class BubbleSortTests : StableSortAlgorithmTests
{
    protected override int LargeLength => 2_000;

    protected override void Sort<T>(Span<T> span) => BubbleSort.Sort(span);

    protected override void Sort<T>(Span<T> span, IComparer<T>? comparer) => BubbleSort.Sort(span, comparer);

    protected override void Sort<T>(Span<T> span, Comparison<T> comparison) => BubbleSort.Sort(span, comparison);
}

public sealed class MergeSortTests : StableSortAlgorithmTests
{
    protected override void Sort<T>(Span<T> span) => MergeSort.Sort(span);

    protected override void Sort<T>(Span<T> span, IComparer<T>? comparer) => MergeSort.Sort(span, comparer);

    protected override void Sort<T>(Span<T> span, Comparison<T> comparison) => MergeSort.Sort(span, comparison);

    [Test]
    public void Sort_ManyRunsWithDuplicates_IsStable()
    {
        Gen.Int[0, 20].Array[0, 5000].Sample(keys =>
        {
            (int Key, int Order)[] items = [.. keys.Select((k, i) => (k, i))];
            (int Key, int Order)[] expected = [.. items.OrderBy(static x => x.Key)]; // LINQ OrderBy is stable

            MergeSort.Sort(items, static (a, b) => a.Key.CompareTo(b.Key));

            items.Should().Equal(expected);
        }, iter: 200);
    }
}

