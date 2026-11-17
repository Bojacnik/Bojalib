using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

public class StringSortTests
{
    private static void AssertMatchesOrdinal(string?[] input)
    {
        string?[] expected = [.. input];
        Array.Sort(expected, StringComparer.Ordinal);
        string?[] actual = [.. input];

        StringSort.Sort(actual);

        actual.Should().Equal(expected);
    }

    [Test]
    public void RandomStrings_MatchOrdinalSort()
    {
        // Small alphabets and lengths force deep ties, equal strings and prefixes of each other.
        Gen<string?> tight = Gen.Char['a', 'c'].Array[0, 8].Select(static c => (string?)new string(c));
        Gen<string?> anyText = Gen.String.Select(static s => (string?)s);
        Gen<string?> special = Gen.Int[0, 1].Select(static i => i == 0 ? null : string.Empty);
        Gen.OneOf(tight, anyText, special).Array[0, 600].Sample(AssertMatchesOrdinal, iter: 500);
    }

    [Test]
    public void NonAsciiAndSurrogates_UseCodeUnitOrder()
    {
        AssertMatchesOrdinal(["é", "e", "z", "\U0001F600", "￿", "ä", "A", "a", "\U00010400", "Ω"]);
    }

    [Test]
    public void LongSharedPrefixes_DoNotOverflowAndSortCorrectly()
    {
        string prefix = new('x', 5_000);
        var random = new Random(3);
        string?[] input = [.. Enumerable.Range(0, 2_000).Select(_ => prefix + random.Next(1000).ToString(System.Globalization.CultureInfo.InvariantCulture))];

        AssertMatchesOrdinal(input);
    }

    [Test]
    public void LargeRealisticInputs()
    {
        var random = new Random(9);
        string?[] urls = [.. Enumerable.Range(0, 50_000).Select(_ => $"https://example.com/api/v2/users/{random.Next(100_000)}/orders/{random.Next(50)}")];
        AssertMatchesOrdinal(urls);

        string?[] words = [.. Enumerable.Range(0, 50_000).Select(_ => new string([.. Enumerable.Range(0, random.Next(1, 12)).Select(_ => (char)('a' + random.Next(26)))]))];
        AssertMatchesOrdinal(words);

        string?[] same = [.. Enumerable.Repeat("identical", 10_000)];
        AssertMatchesOrdinal(same);
    }

    [Test]
    public void KeysAndItems_StayPaired()
    {
        Gen.Char['a', 'd'].Array[0, 5].Select(static c => new string(c)).Array[0, 500].Sample(input =>
        {
            string?[] keys = [.. input];
            int[] items = [.. Enumerable.Range(0, keys.Length)];

            StringSort.Sort(keys.AsSpan(), items.AsSpan());

            keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].Should().Be(input[items[i]]);
            }
        }, iter: 300);
    }

    [Test]
    public void KeysAndItems_WithNullsAndValidation()
    {
        string?[] keys = ["b", null, "a"];
        string[] items = ["B", "null", "A"];

        StringSort.Sort(keys.AsSpan(), items.AsSpan());

        keys.Should().Equal(null, "a", "b");
        items.Should().Equal("null", "A", "B");
        FluentActions.Invoking(() => StringSort.Sort(new string?[2].AsSpan(), new int[3].AsSpan())).Should().Throw<ArgumentException>();
        StringSort.Sort(Span<string?>.Empty);
    }
}
