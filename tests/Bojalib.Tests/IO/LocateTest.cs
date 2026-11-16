using Bojalib.IO;
using AwesomeAssertions;

namespace Bojalib.Tests.IO;

public class LocateTest
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("bojalib-tests-").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void DirectoriesRecursively_ReturnsRootAndAllNestedDirectories()
    {
        string a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        string ab = Directory.CreateDirectory(Path.Combine(a, "b")).FullName;
        string abc = Directory.CreateDirectory(Path.Combine(ab, "c")).FullName;
        string d = Directory.CreateDirectory(Path.Combine(_root, "d")).FullName;

        List<string> result = Locate.DirectoriesRecursively(_root);

        result.Should().BeEquivalentTo([_root, a, ab, abc, d]);
        result[0].Should().Be(_root);
    }

    [Test]
    public void DirectoriesRecursively_SymlinkLoop_IsNotFollowed()
    {
        string a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        string loop1 = Path.Combine(a, "loop1");
        string loop2 = Path.Combine(a, "loop2");
        Directory.CreateSymbolicLink(loop1, _root);
        Directory.CreateSymbolicLink(loop2, _root);

        List<string> result = Locate.DirectoriesRecursively(_root);

        // The links themselves are listed, but never descended into.
        result.Should().BeEquivalentTo([_root, a, loop1, loop2]);
    }

    [Test]
    public void DirectoriesRecursively_IncludesHiddenDirectories()
    {
        string hidden = Directory.CreateDirectory(Path.Combine(_root, ".hidden")).FullName;
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

        Locate.DirectoriesRecursively(_root).Should().Contain(hidden);
    }

    [Test]
    public void DirectoriesRecursively_RelativePath_ReturnsRelativePaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", "b"));
        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
        try
        {
            Locate.DirectoriesRecursively("a").Should().BeEquivalentTo(["a", Path.Combine("a", "b")]);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Test]
    public void DirectoriesRecursively_NoSubdirectories_ReturnsOnlyRoot()
    {
        Locate.DirectoriesRecursively(_root).Should().Equal(_root);
    }

    [Test]
    public void Files_ReturnsFilesInDirectoryOnly()
    {
        string file = Path.Combine(_root, "x.txt");
        File.WriteAllText(file, "");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "y.txt"), "");

        Locate.Files(_root).Should().Equal(file);
    }
}
