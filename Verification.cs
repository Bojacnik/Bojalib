using System;
using System.Security.Cryptography;
using System.Text;

namespace Bojalib
{
    namespace Security
    {
        namespace Verification
        {
            public static class Verification
            {
                private static string passwd;
                public static string Password
                {
                    get => passwd;
                    set
                    {
                        passwd = value;
                    }
                }

                /// <summary>
                /// Takes unhashed password and hashes it with SHA512.
                /// </summary>
                /// <param name="password"></param>
                /// <returns></returns>
                public static string HashPassword(string password)
                {
                    HashAlgorithm ha = SHA512.Create();
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in ha.ComputeHash(Encoding.UTF8.GetBytes(password)))
                    {
                        sb.Append(b);
                    }

                    ha.Dispose();
                    return sb.ToString();
                }
                /// <summary>
                /// Takes unhashed password and hashes it with provided hash algorithm.
                /// </summary>
                /// <param name="password"></param>
                /// <returns></returns>
                public static string HashPassword<T>(string password, T HashingAlgorithm) where T : HashAlgorithm
                {
                    HashAlgorithm ha = HashingAlgorithm;
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in ha.ComputeHash(Encoding.UTF8.GetBytes(password)))
                    {
                        sb.Append(b);
                    }

                    ha.Dispose();
                    return sb.ToString();
                }
            }
        }
    }
    
}
