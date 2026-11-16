using Bojalib.IO;
using FluentAssertions;

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
