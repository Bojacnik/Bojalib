using System.Security.Cryptography;
using System.Text;

namespace Bojalib
{
    namespace Security
    {
        namespace Verification
        {
            public class Verification
            {
                private static string globalPassword;
                public static string GlobalPassword
                {
                    get => globalPassword;
                    set
                    {
                        if (GlobalEncryption)
                            globalPassword = Verification.HashPassword(value);
                        else globalPassword = value;
                    }
                }

                private static bool globalEncryption;
                public static bool GlobalEncryption
                {
                    get => globalEncryption;
                    set => globalEncryption = value;
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

                private string password;
                public string Password
                {
                    get => password;
                    set
                    {
                        if (Encryption)
                            password = Verification.HashPassword(value);
                        else password = value;
                    }
                }
                private bool encryption;
                public bool Encryption
                {
                    get => encryption;
                    set => encryption = value;
                }

            }
        }
    }

}
