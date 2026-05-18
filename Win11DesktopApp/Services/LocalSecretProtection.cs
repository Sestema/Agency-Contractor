using System;
using System.Security.Cryptography;
using System.Text;

namespace Win11DesktopApp.Services
{
    public static class LocalSecretProtection
    {
        public static string Protect(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return string.Empty;

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText))
                return string.Empty;

            try
            {
                var bytes = Convert.FromBase64String(protectedText);
                var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("LocalSecretProtection.Unprotect", ex.Message);
                return string.Empty;
            }
        }
    }
}
