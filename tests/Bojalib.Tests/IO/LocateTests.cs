using Bojalib.IO;

namespace Bojalib.Tests.IO;

[NonParallelizable] // One test changes the current directory.
public class LocateTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("bojalib-tests-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine([_root, .. parts])).FullName;

    private string File(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    [Test]
    public void Directories_ReturnsAllNestedDirectoriesButNotRoot()
    {
        string a = Dir("a");
        string ab = Dir("a", "b");
        string abc = Dir("a", "b", "c");
        string d = Dir("d");

        Locate.Directories(_root).Should().BeEquivalentTo([a, ab, abc, d]);
    }

    [Test]
    public void Directories_ParentsComeBeforeChildren()
    {
        string a = Dir("a");
        string ab = Dir("a", "b");

        List<string> result = [.. Locate.Directories(_root)];

        result.IndexOf(a).Should().BeLessThan(result.IndexOf(ab));
    }

    [Test]
    public void Directories_Empty_ReturnsNothing()
    {
        Locate.Directories(_root).Should().BeEmpty();
    }

    [Test]
    public void Directories_SymlinkLoops_AreListedButNotFollowed()
    {
        string a = Dir("a");
        string loop1 = Path.Combine(a, "loop1");
        string loop2 = Path.Combine(a, "loop2");
        Directory.CreateSymbolicLink(loop1, _root);
        Directory.CreateSymbolicLink(loop2, _root);

        Locate.Directories(_root).Should().BeEquivalentTo([a, loop1, loop2]);
    }

    [Test]
    public void Directories_IncludesHiddenDirectories()
    {
        string hidden = Dir(".hidden");
        System.IO.File.SetAttributes(hidden, System.IO.File.GetAttributes(hidden) | FileAttributes.Hidden);

        Locate.Directories(_root).Should().Contain(hidden);
    }

    [Test]
    public void Directories_RelativePath_ReturnsRelativePaths()
    {
        Dir("a", "b");
        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
        try
        {
            Locate.Directories("a").Should().Equal(Path.Combine("a", "b"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Test]
    public void Directories_MissingOrNullPath_ThrowsImmediately()
    {
        string missing = Path.Combine(_root, "missing");

        // Thrown when called, not on first MoveNext.
        FluentActions.Invoking(() => Locate.Directories(missing)).Should().Throw<DirectoryNotFoundException>();
        FluentActions.Invoking(() => Locate.Files(null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Files_NonRecursive_ReturnsOnlyTopLevelFiles()
    {
        string top = File("top.txt");
        Dir("sub");
        File("sub", "nested.txt");

        Locate.Files(_root).Should().Equal(top);
    }

    [Test]
    public void Files_Recursive_ReturnsFilesAtEveryLevelWithoutFollowingLinks()
    {
        string top = File("top.txt");
        Dir("sub");
        string nested = File("sub", "nested.txt");
        Directory.CreateSymbolicLink(Path.Combine(_root, "sub", "loop"), _root);

        Locate.Files(_root, recursive: true).Should().BeEquivalentTo([top, nested]);
    }
}
