using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class TrieSearchTests
{
    // Keys over a tiny alphabet so they share lots of prefixes and edges get split and merged often.
    private static readonly Gen<string> SmallKey = Gen.Char['a', 'c'].Array[0, 6].Select(static c => new string(c));

    [Test]
    public void RandomOperations_MatchSortedDictionary_WithLabelCompaction()
    {
        Gen.Select(SmallKey, Gen.Int[0, 3]).Array[1, 400].Sample(ops =>
        {
            var trie = new Trie<int>();
            var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ops.Length; i++)
            {
                (string key, int op) = ops[i];
                if (op == 0)
                {
                    trie.Remove(key).Should().Be(expected.Remove(key));
                }
                else
                {
                    trie[key] = i;
                    expected[key] = i;
                }
            }

            trie.Should().Equal(expected);
            foreach (string prefix in (string[])["", "a", "ab", "abc", "c", "cc"])
            {
                trie.EnumeratePrefix(prefix).Should().Equal(expected.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)));
                trie.ContainsPrefix(prefix).Should().Be(expected.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)));
            }
        }, iter: 500);
    }

    [Test]
    public void IgnoreCase_MatchesOrdinalIgnoreCaseDictionary()
    {
        Gen<string> mixedKey = Gen.Char.AlphaNumeric.Array[0, 5].Select(static c => new string(c));
        Gen.Select(mixedKey, Gen.Bool).Array[1, 300].Sample(ops =>
        {
            var trie = new Trie<int>(ignoreCase: true);
            var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ops.Length; i++)
            {
                (string key, bool remove) = ops[i];
                if (remove)
                {
                    trie.Remove(key).Should().Be(expected.Remove(key));
                }
                else
                {
                    trie[key] = i;
                    expected[key] = i;
                }
            }

            trie.Should().BeEquivalentTo(expected);
            foreach (string key in expected.Keys)
            {
                trie.TryGetValue(key.ToLowerInvariant(), out int v).Should().BeTrue();
                v.Should().Be(expected[key]);
                trie.ContainsKey(key.ToUpperInvariant()).Should().BeTrue();
            }
        }, iter: 300);
    }

    [Test]
    public void IgnoreCase_KeepsFirstCasingAndLongKeysUsePooledBuffer()
    {
        var trie = new Trie<int>(ignoreCase: true) { ["Hello"] = 1 };
        trie["HELLO"] = 2;
        string longKey = new('x', 1000);
        trie[longKey] = 3;

        trie.Should().Contain(new KeyValuePair<string, int>("Hello", 2));
        trie[longKey.ToUpperInvariant()].Should().Be(3);
        trie.IgnoreCase.Should().BeTrue();
    }

    [Test]
    public void IgnoreCase_SurrogatePairs_IndexAndTreeAgree()
    {
        // U+10400 and U+10428 are upper/lower case of the same Deseret letter, encoded as surrogate pairs. The trie
        // folds case per UTF-16 unit, so it treats them as different keys, and its exact-lookup index must agree.
        const string upper = "\U00010400";
        const string lower = "\U00010428";
        var trie = new Trie<int>(ignoreCase: true) { [upper] = 1 };

        trie.ContainsKey(lower).Should().BeFalse();
        trie.ContainsKey(lower.AsSpan()).Should().BeFalse();
        trie.ContainsPrefix(lower).Should().BeFalse();
        trie.TryGetValue(upper, out int value).Should().BeTrue();
        value.Should().Be(1);

        trie[lower] = 2;
        trie.Count.Should().Be(2);
        trie.Remove(upper).Should().BeTrue();
        trie.ContainsKey(lower).Should().BeTrue();
        trie.Keys.Should().Equal(lower);
    }

    [Test]
    public void TryMatchLongestPrefix_MatchesBruteForce()
    {
        Gen.Select(SmallKey.Array[0, 30], Gen.Char['a', 'd'].Array[0, 10].Select(static c => new string(c))).Sample(t =>
        {
            (string[] keys, string text) = t;
            var trie = new Trie<string>();
            foreach (string key in keys)
            {
                trie[key] = key;
            }

            string? expected = keys.Where(k => text.StartsWith(k, StringComparison.Ordinal)).MaxBy(static k => k.Length);

            bool found = trie.TryMatchLongestPrefix(text, out int length, out string? value);

            found.Should().Be(expected is not null);
            if (expected is not null)
            {
                length.Should().Be(expected.Length);
                value.Should().Be(expected);
            }
        }, iter: 2000);
    }

    [Test]
    public void TryMatchLongestPrefix_Tokenizer()
    {
        var keywords = new Trie<string>(ignoreCase: true) { ["fn"] = "FN", ["for"] = "FOR", ["foreach"] = "FOREACH" };

        keywords.TryMatchLongestPrefix("FOREACH (x in y)", out int length, out string? token).Should().BeTrue();
        length.Should().Be(7);
        token.Should().Be("FOREACH");

        keywords.TryMatchLongestPrefix("format", out length, out token).Should().BeTrue();
        token.Should().Be("FOR");

        keywords.TryMatchLongestPrefix("xyz", out _, out _).Should().BeFalse();
    }

    [Test]
    public void EnumerateMatches_MatchesBruteForce()
    {
        Gen<string> pattern = Gen.OneOf(Gen.Char['a', 'c'], Gen.Const('?')).Array[0, 5].Select(static c => new string(c));
        Gen.Select(SmallKey.Array[0, 40], pattern).Sample(t =>
        {
            (string[] keys, string p) = t;
            var trie = new Trie<int>();
            foreach (string key in keys)
            {
                trie[key] = 0;
            }

            IEnumerable<string> expected = keys.Distinct()
                .Where(k => k.Length == p.Length && k.Zip(p).All(static x => x.Second == '?' || x.First == x.Second))
                .Order(StringComparer.Ordinal);

            trie.EnumerateMatches(p).Select(static m => m.Key).Should().Equal(expected);
        }, iter: 2000);
    }

    [Test]
    public void EnumerateMatches_CustomWildcardAndIgnoreCase()
    {
        var trie = new Trie<int>(ignoreCase: true) { ["Cat"] = 1, ["cut"] = 2, ["cart"] = 3 };

        trie.EnumerateMatches("C*T", wildcard: '*').Select(static m => m.Key).Should().Equal("Cat", "cut");
    }

    [Test]
    public void EnumerateFuzzy_MatchesBruteForceLevenshtein()
    {
        Gen.Select(SmallKey.Array[0, 40], SmallKey, Gen.Int[0, 3]).Sample(t =>
        {
            (string[] keys, string word, int max) = t;
            var trie = new Trie<int>();
            foreach (string key in keys)
            {
                trie[key] = key.Length;
            }

            var expected = keys.Distinct()
                .Select(k => (Key: k, Distance: Levenshtein(k, word)))
                .Where(x => x.Distance <= max)
                .OrderBy(static x => x.Key, StringComparer.Ordinal)
                .ToList();

            trie.EnumerateFuzzy(word, max).Select(static m => (m.Key, m.Distance)).Should().Equal(expected);
        }, iter: 2000);
    }

    [Test]
    public void EnumerateFuzzy_SpellingSuggestions_AndLongKeysGrowRowBuffer()
    {
        var words = new Trie<int>(ignoreCase: true) { ["kitten"] = 1, ["sitting"] = 2, ["mitten"] = 3, [new string('k', 100)] = 4 };

        words.EnumerateFuzzy("KITTEN", 1).Select(static m => (m.Key, m.Value, m.Distance))
            .Should().Equal(("kitten", 1, 0), ("mitten", 3, 1));
        words.EnumerateFuzzy(new string('k', 99), 1).Should().ContainSingle().Which.Value.Should().Be(4);
        words.EnumerateFuzzy("", 0).Should().BeEmpty();
        new Trie<int> { [""] = 9 }.EnumerateFuzzy("ab", 2).Should().ContainSingle().Which.Distance.Should().Be(2);
    }

    [Test]
    public void SearchArguments_AreValidated()
    {
        var trie = new Trie<int>();

        FluentActions.Invoking(() => trie.EnumerateFuzzy("a", -1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => trie.EnumerateFuzzy(null!, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => trie.EnumerateMatches(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => trie.Add((string)null!, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => trie[(string)null!] = 1).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Enumerators_DetectModificationAndReset()
    {
        var trie = new Trie<int> { ["a"] = 1, ["b"] = 2 };

        Trie<int>.Enumerator e = trie.GetEnumerator();
        e.MoveNext().Should().BeTrue();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.Current.Key.Should().Be("a");

        trie["c"] = 3;
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() =>
        {
            foreach (KeyValuePair<string, int> _ in trie.EnumerateMatches("?"))
            {
                trie["zz"] = 0;
            }
        }).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() =>
        {
            foreach (Trie<int>.FuzzyMatch _ in trie.EnumerateFuzzy("a", 2))
            {
                trie["new"] = 0;
            }
        }).Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void StringAndSpanOverloads_Agree()
    {
        var trie = new Trie<int>();
        trie.Add("one", 1);
        trie.Add("two".AsSpan(), 2);
        trie.TryAdd("three", 3).Should().BeTrue();
        trie.TryAdd("three".AsSpan(), 4).Should().BeFalse();

        trie["one"].Should().Be(1);
        trie["two".AsSpan()].Should().Be(2);
        FluentActions.Invoking(() => trie.Add("one".AsSpan(), 0)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => _ = trie["missing".AsSpan()]).Should().Throw<KeyNotFoundException>();
    }

    private static int Levenshtein(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        return d[a.Length, b.Length];
    }
}
