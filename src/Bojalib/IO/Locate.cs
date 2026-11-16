namespace Bojalib.IO;

/// <summary>
/// Lazy, recursive file-system searches that are safe to point at any directory: symbolic links to directories are
/// reported but never followed (so link loops can't hang), and unreadable directories are skipped instead of throwing.
/// Hidden and system entries are included.
/// </summary>
public static class Locate
{
    private static readonly EnumerationOptions s_oneLevel = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>Enumerates every directory below <paramref name="path"/> (not <paramref name="path"/> itself).</summary>
    /// <param name="path">The directory to search. Relative paths give relative results.</param>
    /// <returns>Directory paths, parents before their children. Enumeration is lazy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/> doesn't exist.</exception>
    public static IEnumerable<string> Directories(string path) => Walk(EnsureExists(path), files: false, directories: true, recursive: true);

    /// <summary>Enumerates the files in <paramref name="path"/>.</summary>
    /// <param name="path">The directory to search. Relative paths give relative results.</param>
    /// <param name="recursive"><see langword="true"/> to include files in all subdirectories.</param>
    /// <returns>File paths. Enumeration is lazy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/> doesn't exist.</exception>
    public static IEnumerable<string> Files(string path, bool recursive = false) =>
        Walk(EnsureExists(path), files: true, directories: false, recursive);

    private static string EnsureExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Directory.Exists(path) ? path : throw new DirectoryNotFoundException($"Could not find directory '{path}'.");
    }

    private static IEnumerable<string> Walk(string root, bool files, bool directories, bool recursive)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out string? current))
        {
            var subdirectories = new List<string>();
            foreach (FileSystemInfo entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", s_oneLevel))
            {
                // Build from the caller's path so relative input gives relative output.
                string path = Path.Join(current, entry.Name);
                if (entry is DirectoryInfo)
                {
                    if (directories)
                    {
                        yield return path;
                    }

                    if (recursive && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        subdirectories.Add(path);
                    }
                }
                else if (files)
                {
                    yield return path;
                }
            }

            // Push in reverse so directories are visited in the order they were listed.
            for (int i = subdirectories.Count - 1; i >= 0; i--)
            {
                pending.Push(subdirectories[i]);
            }
        }
    }
}
