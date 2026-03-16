using System;
using System.Security.Cryptography;

namespace Win11DesktopApp.Services
{
    public static class PasswordHashService
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int Iterations = 100_000;

        public static string CreateSalt()
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            return Convert.ToBase64String(salt);
        }

        public static string HashPassword(string password, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            return Convert.ToBase64String(hashBytes);
        }

        public static bool VerifyPassword(string password, string salt, string expectedHash)
        {
            var actualHash = HashPassword(password, salt);
            var actualBytes = Convert.FromBase64String(actualHash);
            var expectedBytes = Convert.FromBase64String(expectedHash);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
    }
}
