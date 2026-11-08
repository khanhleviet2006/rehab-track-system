using System;
using System.Security.Cryptography;

namespace AngleMonitorWPF.Rom
{
    public static class PasswordHasher
    {
        public const int Iterations = 210000;
        public static string Hash(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 12 || password.Length > 256)
                throw new ArgumentException("Mật khẩu phải có 12–256 ký tự.");
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return "PBKDF2-SHA256$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(kdf.GetBytes(32));
        }
        public static bool Verify(string password, string encoded)
        {
            if (string.IsNullOrEmpty(password) || password.Length > 256 || encoded == null) return false;
            try
            {
                var parts = encoded.Split('$');
                int iterations;
                if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out iterations) || iterations < 100000 || iterations > 1000000) return false;
                var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
                if (salt.Length != 16 || expected.Length != 32) return false;
                using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                {
                    byte[] actual = kdf.GetBytes(32); int diff = 0;
                    for (int i = 0; i < actual.Length; i++) diff |= actual[i] ^ expected[i];
                    return diff == 0;
                }
            }
            catch (FormatException) { return false; }
        }
    }
}
