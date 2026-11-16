using Bojalib.Collections;
using AwesomeAssertions;

namespace Bojalib.Tests.Collections;

public enum Token
{
    I32,
    I64,
    Function,
    Struct,
    Structe
}

public class TrieTreeTest
{
    private readonly TrieTree<Token> _tree = new();

    [Test]
    public void InsertAndFindWordTest()
    {
        // Arrange
        const string KEY = "struct";
        const Token EXPECTED_VALUE = Token.Struct;

        // Act
        _tree.InsertWord(KEY, EXPECTED_VALUE);

        // Assert
        _tree.FindWord(KEY).Should().Be(EXPECTED_VALUE);
    }

    [Test]
    public void FindLongestMatchingTest()
    {
        // Arrange
        const string KEY = "struct";
        const Token EXPECTED_VALUE = Token.Struct;

        const string KEY2 = "structe";
        const Token EXPECTED_VALUE2 = Token.Structe;

        // Act
        _tree.InsertWord(KEY, EXPECTED_VALUE);
        _tree.InsertWord(KEY2, EXPECTED_VALUE2);

        // Assert
        _tree.FindWord(KEY2).Should().Be(EXPECTED_VALUE2);
    }
}
public class TrieTreeLookupTest
{
    [Test]
    public void TryFindWord_Prefix_ReturnsFalse()
    {
        var tree = new TrieTree<Token>();
        tree.InsertWord("struct", Token.Struct);

        tree.TryFindWord("str", out _).Should().BeFalse();
        tree.ContainsWord("str").Should().BeFalse();
    }

    [Test]
    public void TryFindWord_StoredDefaultValue_ReturnsTrue()
    {
        var tree = new TrieTree<Token>();
        tree.InsertWord("i32", Token.I32);

        tree.TryFindWord("i32", out Token value).Should().BeTrue();
        value.Should().Be(Token.I32);
    }

    [Test]
    public void InsertWord_EmptyString_CanBeFound()
    {
        var tree = new TrieTree<string>();
        tree.InsertWord("", "empty");

        tree.FindWord("").Should().Be("empty");
    }

    [Test]
    public void InsertWord_ExistingWord_ReplacesValue()
    {
        var tree = new TrieTree<int>();
        tree.InsertWord("a", 1);
        tree.InsertWord("a", 2);

        tree.FindWord("a").Should().Be(2);
    }

    [Test]
    public void InsertWord_Null_Throws()
    {
        var tree = new TrieTree<int>();

        Action act = () => tree.InsertWord(null!, 1);

        act.Should().Throw<ArgumentNullException>();
    }
}
