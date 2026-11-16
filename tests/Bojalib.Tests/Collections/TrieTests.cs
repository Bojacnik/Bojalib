using Bojalib.Collections;

namespace Bojalib.Tests.Collections;

public enum Token
{
    I32,
    I64,
    Function,
    Struct,
    Structe,
}

public class TrieTests
{
    [Test]
    public void Indexer_SetThenGet_ReturnsValue()
    {
        var trie = new Trie<Token> { ["struct"] = Token.Struct, ["structe"] = Token.Structe };

        trie["struct"].Should().Be(Token.Struct);
        trie["structe"].Should().Be(Token.Structe);
        trie.Count.Should().Be(2);
    }

    [Test]
    public void Indexer_SetExisting_ReplacesValueWithoutChangingCount()
    {
        var trie = new Trie<int> { ["a"] = 1 };

        trie["a"] = 2;

        trie["a"].Should().Be(2);
        trie.Count.Should().Be(1);
    }

    [Test]
    public void Indexer_GetMissing_ThrowsKeyNotFound()
    {
        var trie = new Trie<int> { ["abc"] = 1 };

        Action act = () => _ = trie["ab"];

        act.Should().Throw<KeyNotFoundException>();
    }

    [Test]
    public void Add_Duplicate_Throws()
    {
        var trie = new Trie<int>();
        trie.Add("a", 1);

        Action act = () => trie.Add("a", 2);

        act.Should().Throw<ArgumentException>();
        trie["a"].Should().Be(1);
    }

    [Test]
    public void TryAdd_ReturnsWhetherAdded()
    {
        var trie = new Trie<int>();

        trie.TryAdd("a", 1).Should().BeTrue();
        trie.TryAdd("a", 2).Should().BeFalse();
        trie["a"].Should().Be(1);
    }

    [Test]
    public void TryGetValue_Prefix_ReturnsFalse()
    {
        var trie = new Trie<Token> { ["struct"] = Token.Struct };

        trie.TryGetValue("str", out _).Should().BeFalse();
        trie.ContainsKey("str").Should().BeFalse();
        trie.TryGetValue("structs", out _).Should().BeFalse();
    }

    [Test]
    public void TryGetValue_StoredDefaultValue_ReturnsTrue()
    {
        var trie = new Trie<Token> { ["i32"] = Token.I32 };

        trie.TryGetValue("i32", out Token value).Should().BeTrue();
        value.Should().Be(Token.I32);
    }

    [Test]
    public void TryGetValue_SpanSlice_FindsKeyWithoutAllocatingSubstring()
    {
        var trie = new Trie<int> { ["fn"] = 1 };
        const string source = "pub fn main";

        trie.TryGetValue(source.AsSpan(4, 2), out int value).Should().BeTrue();
        value.Should().Be(1);
    }

    [Test]
    public void EmptyKey_CanBeStoredFoundAndRemoved()
    {
        var trie = new Trie<string> { [""] = "empty" };

        trie[""].Should().Be("empty");
        trie.Remove("").Should().BeTrue();
        trie.ContainsKey("").Should().BeFalse();
        trie.Should().BeEmpty();
    }

    [Test]
    public void ContainsPrefix_ReportsWhetherAnyKeyStartsWithPrefix()
    {
        var trie = new Trie<int> { ["hello"] = 1 };

        trie.ContainsPrefix("he").Should().BeTrue();
        trie.ContainsPrefix("hello").Should().BeTrue();
        trie.ContainsPrefix("").Should().BeTrue();
        trie.ContainsPrefix("hex").Should().BeFalse();
        new Trie<int>().ContainsPrefix("").Should().BeFalse();
    }

    [Test]
    public void Remove_LeafKey_KeepsKeysSharingItsPrefix()
    {
        var trie = new Trie<int> { ["car"] = 1, ["cart"] = 2, ["care"] = 3 };

        trie.Remove("cart").Should().BeTrue();

        trie.Should().BeEquivalentTo(new Dictionary<string, int> { ["car"] = 1, ["care"] = 3 });
        trie.ContainsPrefix("cart").Should().BeFalse();
    }

    [Test]
    public void Remove_KeyThatIsPrefixOfOthers_KeepsLongerKeys()
    {
        var trie = new Trie<int> { ["car"] = 1, ["cart"] = 2 };

        trie.Remove("car").Should().BeTrue();

        trie.ContainsKey("car").Should().BeFalse();
        trie["cart"].Should().Be(2);
        trie.Count.Should().Be(1);
    }

    [Test]
    public void Remove_MissingOrPrefixOnly_ReturnsFalse()
    {
        var trie = new Trie<int> { ["cart"] = 1 };

        trie.Remove("car").Should().BeFalse();
        trie.Remove("dog").Should().BeFalse();
        trie.Count.Should().Be(1);
    }

    [Test]
    public void Remove_LastKey_PrunesAllNodes()
    {
        var trie = new Trie<int> { ["abc"] = 1 };

        trie.Remove("abc");

        trie.ContainsPrefix("a").Should().BeFalse();
        trie.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        var trie = new Trie<int> { ["a"] = 1, ["b"] = 2 };

        trie.Clear();

        trie.Should().BeEmpty();
        trie.ContainsPrefix("").Should().BeFalse();
    }

    [Test]
    public void Enumerate_YieldsKeysInOrdinalOrder()
    {
        var trie = new Trie<int> { ["b"] = 1, ["a"] = 2, ["ab"] = 3, [""] = 4, ["B"] = 5 };

        trie.Keys.Should().Equal("", "B", "a", "ab", "b");
        trie.Values.Should().Equal(4, 5, 2, 3, 1);
    }

    [Test]
    public void EnumeratePrefix_YieldsMatchingKeysIncludingPrefixItself()
    {
        var trie = new Trie<int> { ["car"] = 1, ["cart"] = 2, ["care"] = 3, ["cat"] = 4, ["dog"] = 5 };

        trie.EnumeratePrefix("car").Select(static p => p.Key).Should().Equal("car", "care", "cart");
        trie.EnumeratePrefix("x").Should().BeEmpty();
        trie.EnumeratePrefix("").Should().HaveCount(5);
    }

    [Test]
    public void Enumerate_ModifiedDuringEnumeration_Throws()
    {
        var trie = new Trie<int> { ["a"] = 1, ["b"] = 2 };

        Action act = () =>
        {
            foreach (KeyValuePair<string, int> pair in trie)
            {
                trie[pair.Key + "x"] = 0;
            }
        };

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ReadOnlyDictionaryInterface_Works()
    {
        IReadOnlyDictionary<string, int> dictionary = new Trie<int> { ["one"] = 1 };

        dictionary["one"].Should().Be(1);
        dictionary.ContainsKey("one").Should().BeTrue();
        dictionary.TryGetValue("two", out _).Should().BeFalse();
        dictionary.Keys.Should().Equal("one");
        dictionary.Invoking(d => d.ContainsKey(null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void RandomOperations_MatchSortedDictionary()
    {
        var random = new Random(7);
        var trie = new Trie<int>();
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < 20_000; i++)
        {
            string key = new([.. Enumerable.Range(0, random.Next(0, 5)).Select(_ => (char)('a' + random.Next(3)))]);
            if (random.Next(3) == 0)
            {
                trie.Remove(key).Should().Be(expected.Remove(key));
            }
            else
            {
                trie[key] = i;
                expected[key] = i;
            }
        }

        trie.Count.Should().Be(expected.Count);
        trie.Should().Equal(expected);
    }
}
