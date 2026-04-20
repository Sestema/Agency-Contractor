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
        public int SignatureVersion { get; set; }
        public string SignatureSecret { get; set; } = string.Empty;
    }

    public sealed class LocalLicenseStatus
    {
        public bool IsValid { get; init; }
        public string StatusText { get; init; } = string.Empty;
        public bool IsUnlimited { get; init; }
        public int DaysLeft { get; init; } = -1;
        public DateTime? ExpiresAtUtc { get; init; }
        public string Plan { get; init; } = string.Empty;
        public string ActivatedOn { get; init; } = string.Empty;
        public string ExpiresOn { get; init; } = string.Empty;
    }

    public static class LicenseService
    {
        private static AppSettingsService? _appSettingsService;
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
        private static bool _legacyWarningLogged;

        public static void Initialize(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        }

        public static bool IsLicenseValid()
        {
            return GetLocalLicenseStatus().IsValid;
        }

        public static (bool Success, string Message) Activate(string activatorKeyPath, int days)
        {
            try
            {
                if (!File.Exists(activatorKeyPath))
                    return (false, "Файл активації не знайдено.");

                var keyContent = SafeFileService.ReadAllText(activatorKeyPath).Trim();
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
                    LastChecked = now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SignatureVersion = 2,
                    SignatureSecret = keyContent
                };
                info.Signature = ComputeSignature(info, keyContent);

                SaveLicense(info);

                LoggingService.LogInfo("LicenseService", $"License activated: plan={plan}, expires={expiresOn}");
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
            return GetLocalLicenseStatus().StatusText;
        }

        public static string GetExpiresAt()
        {
            try
            {
                var info = LoadLicense();
                if (info == null) return DateTime.UtcNow.AddDays(1).ToString("o");
                if (info.Plan == "unlimited") return "9999-12-31T00:00:00Z";
                if (DateTime.TryParse(info.ExpiresOn, out var dt))
                    return dt.ToUniversalTime().ToString("o");
            }
            catch (Exception ex) { LoggingService.LogWarning("LicenseService.GetExpiresAt", ex.Message); }
            return DateTime.UtcNow.AddDays(1).ToString("o");
        }

        public static int GetDaysLeft()
        {
            return GetLocalLicenseStatus().DaysLeft;
        }

        public static LocalLicenseStatus GetLocalLicenseStatus()
        {
            try
            {
                var info = LoadLicense();
                if (info == null)
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = "Не активовано"
                    };
                }

                if (info.MachineId != GetMachineId())
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = "Ліцензія для іншого ПК"
                    };
                }

                if (!VerifySignature(info))
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = "Ліцензія пошкоджена"
                    };
                }

                if (!CanTrustLocalLicense(info))
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = "Локальна ліцензія перенесена на сервер"
                    };
                }

                if (info.Plan == "unlimited")
                {
                    UpdateLastCheckedIfNeeded(info);
                    return new LocalLicenseStatus
                    {
                        IsValid = true,
                        StatusText = "Безлімітна ліцензія",
                        IsUnlimited = true,
                        DaysLeft = 99999,
                        ExpiresAtUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                        Plan = info.Plan,
                        ActivatedOn = info.ActivatedOn,
                        ExpiresOn = info.ExpiresOn
                    };
                }

                if (!DateTime.TryParse(info.ExpiresOn, out var expires))
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = "Ліцензія пошкоджена"
                    };
                }

                if (DateTime.Now > expires)
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = $"Ліцензія закінчилась {expires:dd.MM.yyyy}",
                        DaysLeft = 0,
                        ExpiresAtUtc = expires.ToUniversalTime()
                    };
                }

                if (DateTime.TryParse(info.LastChecked, out var lastChecked) && DateTime.Now.AddDays(7) < lastChecked)
                {
                    return new LocalLicenseStatus
                    {
                        IsValid = false,
                        StatusText = "Ліцензія пошкоджена",
                        ExpiresAtUtc = expires.ToUniversalTime()
                    };
                }

                UpdateLastCheckedIfNeeded(info);
                var daysLeft = Math.Max(0, (expires - DateTime.Now).Days);
                return new LocalLicenseStatus
                {
                    IsValid = true,
                    StatusText = $"Активна до {expires:dd.MM.yyyy} ({daysLeft} дн.)",
                    DaysLeft = daysLeft,
                    ExpiresAtUtc = expires.ToUniversalTime(),
                    Plan = info.Plan,
                    ActivatedOn = info.ActivatedOn,
                    ExpiresOn = info.ExpiresOn
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("LicenseService.GetLocalLicenseStatus", ex);
                return new LocalLicenseStatus
                {
                    IsValid = false,
                    StatusText = "Помилка перевірки"
                };
            }
        }

        internal static bool CanTrustLocalLicense(LicenseInfo? info)
        {
            var migratedAtUtc = _appSettingsService?.Settings.LegacyLicenseMigratedAtUtc;
            return CanTrustLocalLicense(info, migratedAtUtc);
        }

        internal static bool CanTrustLocalLicense(LicenseInfo? info, string? legacyLicenseMigratedAtUtc)
        {
            if (info == null)
                return false;

            if (!IsLegacyLicense(info))
                return true;

            return string.IsNullOrWhiteSpace(legacyLicenseMigratedAtUtc);
        }

        internal static bool ShouldLogLegacyLicenseWarning(LicenseInfo? info)
        {
            var migratedAtUtc = _appSettingsService?.Settings.LegacyLicenseMigratedAtUtc;
            return ShouldLogLegacyLicenseWarning(info, migratedAtUtc);
        }

        internal static bool ShouldLogLegacyLicenseWarning(LicenseInfo? info, string? legacyLicenseMigratedAtUtc)
        {
            return IsLegacyLicense(info)
                && string.IsNullOrWhiteSpace(legacyLicenseMigratedAtUtc);
        }

        internal static bool IsLegacyLicense(LicenseInfo? info)
        {
            return info != null
                && (info.SignatureVersion < 2 || string.IsNullOrWhiteSpace(info.SignatureSecret));
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
                catch (Exception ex) { LoggingService.LogWarning("LicenseService.GetMachineId", $"Fixed disk query failed: {ex.Message}"); }

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
                    catch (Exception ex) { LoggingService.LogWarning("LicenseService.GetMachineId", $"Fallback disk query failed: {ex.Message}"); }
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
                catch (Exception ex) { LoggingService.LogWarning("LicenseService.GetMachineId", $"CPU query failed: {ex.Message}"); }

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
            catch (Exception ex)
            {
                LoggingService.LogError("LicenseService.GetMachineId", $"Fatal fallback: {ex.Message}");
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
            if (info.SignatureVersion < 2 || string.IsNullOrWhiteSpace(info.SignatureSecret))
            {
                if (!_legacyWarningLogged && ShouldLogLegacyLicenseWarning(info))
                {
                    LoggingService.LogWarning("LicenseService.VerifySignature", "Legacy license format detected; cryptographic verification is unavailable.");
                    _legacyWarningLogged = true;
                }
                return true;
            }

            var expected = ComputeSignature(info, info.SignatureSecret);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(info.Signature);
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
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
            catch (Exception ex) { LoggingService.LogWarning("LicenseService.UpdateLastChecked", ex.Message); }
        }

        private static void SaveLicense(LicenseInfo info)
        {
            Directory.CreateDirectory(LicenseFolder);
            var json = JsonSerializer.Serialize(info);

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), GetEntropy(), DataProtectionScope.CurrentUser);

            if (File.Exists(LicensePath))
            {
                try { SafeFileService.CopyFile(LicensePath, BackupPath); } catch (Exception ex) { LoggingService.LogWarning("LicenseService.SaveLicense", $"Backup copy failed: {ex.Message}"); }
            }

            SafeFileService.WriteBytesAtomic(LicensePath, encrypted);
        }

        private static LicenseInfo? LoadLicense()
        {
            MigrateFromOldLocation();

            var result = TryLoadFromPath(LicensePath);
            if (result != null) return result;

            result = TryLoadFromPath(BackupPath);
            if (result != null)
            {
                try { SafeFileService.CopyFile(BackupPath, LicensePath); } catch (Exception ex) { LoggingService.LogWarning("LicenseService.LoadLicense", $"Restore from backup failed: {ex.Message}"); }
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
                    SafeFileService.CopyFile(OldLicensePath, LicensePath);

                    var oldBackup = Path.Combine(OldLicenseFolder, ".license.bak");
                    if (File.Exists(oldBackup))
                        SafeFileService.CopyFile(oldBackup, BackupPath);
                }
            }
            catch (Exception ex) { LoggingService.LogWarning("LicenseService.Migrate", ex.Message); }
        }

        private static LicenseInfo? TryLoadFromPath(string path)
        {
            if (!File.Exists(path)) return null;

            var rawBytes = SafeFileService.ReadAllBytes(path);

            try
            {
                var decrypted = ProtectedData.Unprotect(rawBytes, GetEntropy(), DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonSerializer.Deserialize<LicenseInfo>(json);
            }
            catch (Exception ex) { LoggingService.LogWarning("LicenseService.TryLoad", $"CurrentUser decrypt failed: {ex.Message}"); }

            try
            {
                var decrypted = ProtectedData.Unprotect(rawBytes, GetEntropy(), DataProtectionScope.LocalMachine);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonSerializer.Deserialize<LicenseInfo>(json);
            }
            catch (Exception ex) { LoggingService.LogWarning("LicenseService.TryLoad", $"LocalMachine decrypt failed: {ex.Message}"); }

            return null;
        }

        private static byte[] GetEntropy()
        {
            return Encoding.UTF8.GetBytes("AC-2026-OleksandrKachalin");
        }
    }
}
