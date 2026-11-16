namespace Bojalib.IO;

public static class Locate
{
    /// <summary>
    /// Returns <paramref name="path"/> followed by all of its subdirectories, recursively.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static List<string> DirectoriesRecursively(string path)
    {
        var paths = new List<string> { path };
        foreach (string dir in Directory.GetDirectories(path))
        {
            paths.AddRange(DirectoriesRecursively(dir));
        }

        return paths;
    }

    /// <summary>
    /// Returns a list with all files in a given directory. If none is found, an empty list is returned.
    /// </summary>
    /// <param name="dir"></param>
    /// <returns></returns>
    public static List<string> Files(string dir)
    {
        return Directory.GetFiles(dir).ToList();
    }
}