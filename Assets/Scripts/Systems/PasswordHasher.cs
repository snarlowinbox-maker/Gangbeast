using System;
using System.Security.Cryptography;

namespace BeefCity.Accounts
{
    /// <summary>
    /// Password hashing for the local account table: PBKDF2 (HMAC-SHA256), 100,000 iterations, a fresh 16-byte random
    /// salt per password. Stored as "pbkdf2-sha256$iterations$salt$hash" (Base64), so the iteration count can be
    /// raised later and old hashes still verify. Plain passwords are never stored or logged.
    /// </summary>
    public static class PasswordHasher
    {
        const string Scheme = "pbkdf2-sha256";
        const int Iterations = 100000;
        const int SaltSize = 16;
        const int HashSize = 32;

        public static string Hash(string password)
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash = Derive(password, salt, Iterations);
            return Scheme + "$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrEmpty(stored) || password == null) return false;
            string[] parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != Scheme) return false;

            try
            {
                int iterations = int.Parse(parts[1]);
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                byte[] actual = Derive(password, salt, iterations);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(HashSize);
        }
    }
}
