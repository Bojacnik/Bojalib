namespace Bojalib.IO;

public static class Locate
{
    private static readonly EnumerationOptions s_oneLevel = new()
    {
        // Skip directories we can't read instead of failing the whole call.
        IgnoreInaccessible = true,
        // Include hidden/system directories (e.g. ".git"); the default would skip them.
        AttributesToSkip = 0,
    };

    /// <summary>
    /// Returns <paramref name="path"/> followed by all of its subdirectories, recursively.
    /// Symbolic links to directories are listed but not followed; directories that can't be read are skipped.
    /// </summary>
    public static List<string> DirectoriesRecursively(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var paths = new List<string> { path };
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (DirectoryInfo dir in new DirectoryInfo(current).EnumerateDirectories("*", s_oneLevel))
            {
                // Build from the caller's path so relative input gives relative output.
                string child = Path.Combine(current, dir.Name);
                paths.Add(child);
                if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Returns the files directly inside <paramref name="dir"/> (not subdirectories). Empty if there are none.
    /// </summary>
    public static List<string> Files(string dir)
    {
        return Directory.GetFiles(dir).ToList();
    }
}