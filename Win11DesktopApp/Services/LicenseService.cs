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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AgencyContractor");
        private static readonly string LicensePath = Path.Combine(LicenseFolder, ".license");
        private static readonly string BackupPath = Path.Combine(LicenseFolder, ".license.bak");

        private static readonly string OldLicenseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgencyContractor");
        private static readonly string OldLicensePath = Path.Combine(OldLicenseFolder, ".license");

        private static string? _cachedMachineId;
        private static bool _migrationDone;

        public static bool IsLicenseValid()
        {
            try
            {
                var info = LoadLicense();
                if (info == null) return false;

                if (info.MachineId != GetMachineId()) return false;

                if (!VerifySignature(info)) return false;

                if (info.Plan == "unlimited")
                {
                    UpdateLastCheckedIfNeeded(info);
                    return true;
                }

                if (!DateTime.TryParse(info.ExpiresOn, out var expires)) return false;
                if (DateTime.Now > expires) return false;

                if (DateTime.TryParse(info.LastChecked, out var lastChecked))
                {
                    if (DateTime.Now.AddDays(7) < lastChecked) return false;
                }

                UpdateLastCheckedIfNeeded(info);
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
            if (_cachedMachineId != null)
                return _cachedMachineId;

            try
            {
                var sb = new StringBuilder();
                string diskSerial = "";
                string cpuId = "";

                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT SerialNumber FROM Win32_DiskDrive WHERE MediaType='Fixed hard disk media'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        using (obj)
                        {
                            var sn = obj["SerialNumber"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(sn))
                            {
                                diskSerial = sn;
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(diskSerial))
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            using (obj)
                            {
                                var sn = obj["SerialNumber"]?.ToString()?.Trim();
                                if (!string.IsNullOrEmpty(sn))
                                {
                                    diskSerial = sn;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        using (obj)
                        {
                            cpuId = obj["ProcessorId"]?.ToString()?.Trim() ?? "";
                            break;
                        }
                    }
                }
                catch { }

                sb.Append(diskSerial);
                sb.Append(cpuId);

                if (sb.Length == 0)
                {
                    sb.Append(Environment.MachineName);
                    sb.Append("-fallback-v2");
                }

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                _cachedMachineId = Convert.ToBase64String(hash).Substring(0, 16);
                return _cachedMachineId;
            }
            catch
            {
                _cachedMachineId = Environment.MachineName + "-fallback";
                return _cachedMachineId;
            }
        }

        private static bool ValidateActivatorKey(string keyContent)
        {
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
            if (string.IsNullOrEmpty(info.Signature)) return false;
            if (string.IsNullOrEmpty(info.MachineId)) return false;
            if (string.IsNullOrEmpty(info.ActivatedOn)) return false;
            if (string.IsNullOrEmpty(info.ExpiresOn)) return false;
            return true;
        }

        private static void UpdateLastCheckedIfNeeded(LicenseInfo info)
        {
            try
            {
                if (DateTime.TryParse(info.LastChecked, out var last))
                {
                    if ((DateTime.Now - last).TotalHours < 12)
                        return;
                }

                info.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                SaveLicense(info);
            }
            catch { }
        }

        private static void SaveLicense(LicenseInfo info)
        {
            Directory.CreateDirectory(LicenseFolder);
            var json = JsonSerializer.Serialize(info);

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), GetEntropy(), DataProtectionScope.CurrentUser);

            var tempPath = LicensePath + ".tmp";
            File.WriteAllBytes(tempPath, encrypted);

            if (File.Exists(LicensePath))
            {
                try { File.Copy(LicensePath, BackupPath, true); } catch { }
            }

            File.Move(tempPath, LicensePath, true);
        }

        private static LicenseInfo? LoadLicense()
        {
            MigrateFromOldLocation();

            var result = TryLoadFromPath(LicensePath);
            if (result != null) return result;

            result = TryLoadFromPath(BackupPath);
            if (result != null)
            {
                try { File.Copy(BackupPath, LicensePath, true); } catch { }
            }
            return result;
        }

        private static void MigrateFromOldLocation()
        {
            if (_migrationDone) return;
            _migrationDone = true;

            try
            {
                if (File.Exists(LicensePath)) return;

                if (File.Exists(OldLicensePath))
                {
                    Directory.CreateDirectory(LicenseFolder);
                    File.Copy(OldLicensePath, LicensePath, true);

                    var oldBackup = Path.Combine(OldLicenseFolder, ".license.bak");
                    if (File.Exists(oldBackup))
                        File.Copy(oldBackup, BackupPath, true);
                }
            }
            catch { }
        }

        private static LicenseInfo? TryLoadFromPath(string path)
        {
            if (!File.Exists(path)) return null;

            var rawBytes = File.ReadAllBytes(path);

            try
            {
                var decrypted = ProtectedData.Unprotect(rawBytes, GetEntropy(), DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonSerializer.Deserialize<LicenseInfo>(json);
            }
            catch { }

            try
            {
                var decrypted = ProtectedData.Unprotect(rawBytes, GetEntropy(), DataProtectionScope.LocalMachine);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonSerializer.Deserialize<LicenseInfo>(json);
            }
            catch { }

            return null;
        }

        private static byte[] GetEntropy()
        {
            return Encoding.UTF8.GetBytes("AC-2026-OleksandrKachalin");
        }
    }
}
