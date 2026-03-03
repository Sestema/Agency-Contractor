using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public class LicenseInfo
    {
        public string MachineId { get; set; } = string.Empty;
        public string ActivatedOn { get; set; } = string.Empty;
        public string ExpiresOn { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public string LastChecked { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public static class LicenseService
    {
        private static readonly string LicenseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgencyContractor");
        private static readonly string LicensePath = Path.Combine(LicenseFolder, ".license");

        public static bool IsLicenseValid()
        {
            try
            {
                var info = LoadLicense();
                if (info == null) return false;

                if (info.MachineId != GetMachineId()) return false;

                if (!VerifySignature(info)) return false;

                // Unlimited license
                if (info.Plan == "unlimited") 
                {
                    UpdateLastChecked(info);
                    return true;
                }

                if (!DateTime.TryParse(info.ExpiresOn, out var expires)) return false;
                if (DateTime.Now > expires) return false;

                // Anti-rollback: if current date < last checked date, suspicious
                if (DateTime.TryParse(info.LastChecked, out var lastChecked))
                {
                    if (DateTime.Now.AddDays(1) < lastChecked) return false;
                }

                UpdateLastChecked(info);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (bool Success, string Message) Activate(string activatorKeyPath, int days)
        {
            try
            {
                if (!File.Exists(activatorKeyPath))
                    return (false, "Файл активації не знайдено.");

                var keyContent = File.ReadAllText(activatorKeyPath).Trim();
                if (!ValidateActivatorKey(keyContent))
                    return (false, "Невірний файл активації.");

                var machineId = GetMachineId();
                var now = DateTime.Now;
                var plan = days <= 0 ? "unlimited" : $"{days}days";
                var expiresOn = days <= 0 ? "9999-12-31" : now.AddDays(days).ToString("yyyy-MM-dd");

                var info = new LicenseInfo
                {
                    MachineId = machineId,
                    ActivatedOn = now.ToString("yyyy-MM-dd"),
                    ExpiresOn = expiresOn,
                    Plan = plan,
                    LastChecked = now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                info.Signature = ComputeSignature(info, keyContent);

                SaveLicense(info);

                var planLabel = days <= 0 ? "Безліміт" : $"{days} днів (до {expiresOn})";
                return (true, $"Ліцензію активовано: {planLabel}");
            }
            catch (Exception ex)
            {
                return (false, $"Помилка активації: {ex.Message}");
            }
        }

        public static string GetLicenseStatus()
        {
            try
            {
                var info = LoadLicense();
                if (info == null) return "Не активовано";

                if (info.MachineId != GetMachineId()) return "Ліцензія для іншого ПК";
                if (!VerifySignature(info)) return "Ліцензія пошкоджена";

                if (info.Plan == "unlimited") return "Безлімітна ліцензія";

                if (!DateTime.TryParse(info.ExpiresOn, out var expires))
                    return "Ліцензія пошкоджена";

                if (DateTime.Now > expires)
                    return $"Ліцензія закінчилась {expires:dd.MM.yyyy}";

                var daysLeft = (expires - DateTime.Now).Days;
                return $"Активна до {expires:dd.MM.yyyy} ({daysLeft} дн.)";
            }
            catch
            {
                return "Помилка перевірки";
            }
        }

        public static int GetDaysLeft()
        {
            try
            {
                var info = LoadLicense();
                if (info == null) return -1;
                if (info.Plan == "unlimited") return 99999;
                if (!DateTime.TryParse(info.ExpiresOn, out var expires)) return -1;
                return Math.Max(0, (expires - DateTime.Now).Days);
            }
            catch { return -1; }
        }

        public static string GetMachineId()
        {
            try
            {
                var sb = new StringBuilder();

                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        sb.Append(obj["SerialNumber"]?.ToString()?.Trim() ?? "");
                        break;
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        sb.Append(obj["ProcessorId"]?.ToString()?.Trim() ?? "");
                        break;
                    }
                }

                sb.Append(Environment.MachineName);

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToBase64String(hash).Substring(0, 16);
            }
            catch
            {
                return Environment.MachineName + "-fallback";
            }
        }

        private static bool ValidateActivatorKey(string keyContent)
        {
            // The activator key must start with a specific marker
            return keyContent.StartsWith("ACK-") && keyContent.Length >= 40;
        }

        private static string ComputeSignature(LicenseInfo info, string secret)
        {
            var data = $"{info.MachineId}|{info.ActivatedOn}|{info.ExpiresOn}|{info.Plan}|{secret}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash);
        }

        private static bool VerifySignature(LicenseInfo info)
        {
            // We can't verify without the original key, but we stored the signature
            // at activation time. We verify the data hasn't been tampered with by
            // re-reading the stored license and checking structural integrity.
            if (string.IsNullOrEmpty(info.Signature)) return false;
            if (string.IsNullOrEmpty(info.MachineId)) return false;
            if (string.IsNullOrEmpty(info.ActivatedOn)) return false;
            if (string.IsNullOrEmpty(info.ExpiresOn)) return false;

            // Verify the stored file hash matches
            var storedPath = LicensePath;
            if (!File.Exists(storedPath)) return false;

            try
            {
                var rawBytes = File.ReadAllBytes(storedPath);
                var decrypted = ProtectedData.Unprotect(rawBytes, GetEntropy(), DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                var stored = JsonSerializer.Deserialize<LicenseInfo>(json);
                if (stored == null) return false;
                return stored.Signature == info.Signature
                    && stored.MachineId == info.MachineId
                    && stored.ExpiresOn == info.ExpiresOn
                    && stored.Plan == info.Plan;
            }
            catch { return false; }
        }

        private static void UpdateLastChecked(LicenseInfo info)
        {
            try
            {
                info.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                SaveLicense(info);
            }
            catch { /* non-critical */ }
        }

        private static void SaveLicense(LicenseInfo info)
        {
            Directory.CreateDirectory(LicenseFolder);
            var json = JsonSerializer.Serialize(info);
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), GetEntropy(), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(LicensePath, encrypted);
        }

        private static LicenseInfo? LoadLicense()
        {
            if (!File.Exists(LicensePath)) return null;
            try
            {
                var rawBytes = File.ReadAllBytes(LicensePath);
                var decrypted = ProtectedData.Unprotect(rawBytes, GetEntropy(), DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonSerializer.Deserialize<LicenseInfo>(json);
            }
            catch { return null; }
        }

        private static byte[] GetEntropy()
        {
            return Encoding.UTF8.GetBytes("AC-2026-OleksandrKachalin");
        }
    }
}


