using System.IO;
using System.Collections.Generic;

namespace Bojalib
{
    namespace IO
    {

        static class Locate
        {
            /// <summary>
            /// Returns a list with all subdirectories including the path directory. If none are found an empty list is returned.
            /// </summary>
            /// <param name="path"></param>
            /// <returns></returns>
            public static List<string> DirectoriesRecursively(string path)
            {
                List<string> paths = new List<string>();
                paths.Add(path);
                foreach (string dir in Directory.GetDirectories(path))
                {
                    if (dir != null)
                        DirectoriesRecursively(dir);
                }

                return paths;
            }

            /// <summary>
            /// Returns a list with all files in given directory. If none are found an empty list is returned.
            /// </summary>
            /// <param name="dir"></param>
            /// <returns></returns>
            public static List<string> Files(string dir)
            {
                List<string> paths = new List<string>();
                foreach (string s in Directory.GetFiles(dir))
                {
                    paths.Add(s);
                }
                return paths;
            }
        }

    }
}
