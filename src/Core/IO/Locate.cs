using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Bojalib.Core.IO;

public static class Locate
{
    /// <summary>
    /// Returns a list with all subdirectories including the path directory. If none are found an empty list is returned.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static List<string> DirectoriesRecursively(string path)
    {
        var paths = new List<string> { path };
        foreach (string dir in Directory.GetDirectories(path))
        {
            DirectoriesRecursively(dir);
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