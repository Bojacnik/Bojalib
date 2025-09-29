using Bojalib.Core.Collections.Generic;
using FluentAssertions;

namespace Bojalib.Tests.Collections.Generic;

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