using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresNetworkAccessResult
    {
        public bool Success { get; init; }
        public string TailscaleIp { get; init; } = string.Empty;
        public string LanIp { get; init; } = string.Empty;
        public string AllowedCidr { get; init; } = string.Empty;
        public string DataDirectory { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class PostgresNetworkAccessService
    {
        private const string TailscaleCidr = "100.64.0.0/10";
        private const string PgHbaLine = "host    all    all    100.64.0.0/10    scram-sha-256";
        private readonly AppSettingsService _appSettingsService;

        public PostgresNetworkAccessService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService;
        }

        public string TailscaleIpAddress => GetTailscaleIpAddress();
        public string LanIpAddress => GetLanNetworkInfo().ipAddress;
        public string LanCidr => GetLanNetworkInfo().cidr;

        public bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public async Task<PostgresNetworkAccessResult> ConfigureTailscaleAccessAsync(CancellationToken cancellationToken = default)
        {
            var tailscaleIp = GetTailscaleIpAddress();
            if (string.IsNullOrWhiteSpace(tailscaleIp))
            {
                return new PostgresNetworkAccessResult
                {
                    TailscaleIp = string.Empty,
                    Message = "Tailscale IP не знайдено. Перевірте, чи Tailscale встановлений і підключений."
                };
            }

            if (!IsRunningAsAdministrator())
            {
                return new PostgresNetworkAccessResult
                {
                    TailscaleIp = tailscaleIp,
                    Message = "Потрібні права адміністратора. Закрийте програму і запустіть її від імені адміністратора."
                };
            }

            try
            {
                var dataDirectory = ResolvePostgresDataDirectory();
                if (string.IsNullOrWhiteSpace(dataDirectory))
                {
                    return new PostgresNetworkAccessResult
                    {
                        TailscaleIp = tailscaleIp,
                        Message = "Не вдалося знайти папку PostgreSQL data. Вкажіть її у налаштуваннях або перевірте шлях C:\\Program Files\\PostgreSQL\\...\\data."
                    };
                }

                var pgHbaPath = Path.Combine(dataDirectory, "pg_hba.conf");
                var postgresqlConfPath = Path.Combine(dataDirectory, "postgresql.conf");
                BackupFile(pgHbaPath);
                BackupFile(postgresqlConfPath);

                EnsurePgHbaAllowsTailscale(pgHbaPath);
                EnsureListenAddresses(postgresqlConfPath);
                await EnsureFirewallRuleAsync("Agency Contractor PostgreSQL 5432", cancellationToken).ConfigureAwait(false);
                await RestartPostgresServicesAsync(cancellationToken).ConfigureAwait(false);

                return new PostgresNetworkAccessResult
                {
                    Success = true,
                    TailscaleIp = tailscaleIp,
                    DataDirectory = dataDirectory,
                    Message = $"PostgreSQL готовий для Tailscale. Сервер для інших ПК: {tailscaleIp}:5432. Дозволено діапазон {TailscaleCidr}."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PostgresNetworkAccessService.ConfigureTailscaleAccessAsync", ex);
                return new PostgresNetworkAccessResult
                {
                    TailscaleIp = tailscaleIp,
                    Message = $"Не вдалося налаштувати PostgreSQL: {ex.Message}"
                };
            }
        }

        public async Task<PostgresNetworkAccessResult> ConfigureLanAccessAsync(CancellationToken cancellationToken = default)
        {
            var lan = GetLanNetworkInfo();
            if (string.IsNullOrWhiteSpace(lan.ipAddress) || string.IsNullOrWhiteSpace(lan.cidr))
            {
                return new PostgresNetworkAccessResult
                {
                    Message = "LAN IP не знайдено. Перевірте, чи ПК підключений до локальної мережі."
                };
            }

            if (!IsRunningAsAdministrator())
            {
                return new PostgresNetworkAccessResult
                {
                    LanIp = lan.ipAddress,
                    AllowedCidr = lan.cidr,
                    Message = "Потрібні права адміністратора. Перезапустіть програму від імені адміністратора."
                };
            }

            try
            {
                var dataDirectory = ResolvePostgresDataDirectory();
                if (string.IsNullOrWhiteSpace(dataDirectory))
                {
                    return new PostgresNetworkAccessResult
                    {
                        LanIp = lan.ipAddress,
                        AllowedCidr = lan.cidr,
                        Message = "Не вдалося знайти папку PostgreSQL data. Вкажіть її у налаштуваннях або перевірте шлях C:\\Program Files\\PostgreSQL\\...\\data."
                    };
                }

                var pgHbaPath = Path.Combine(dataDirectory, "pg_hba.conf");
                var postgresqlConfPath = Path.Combine(dataDirectory, "postgresql.conf");
                BackupFile(pgHbaPath);
                BackupFile(postgresqlConfPath);

                EnsurePgHbaAllowsCidr(pgHbaPath, lan.cidr, "Agency Contractor LAN access");
                EnsureListenAddresses(postgresqlConfPath);
                await EnsureFirewallRuleAsync("Agency Contractor PostgreSQL 5432 LAN", cancellationToken).ConfigureAwait(false);
                await RestartPostgresServicesAsync(cancellationToken).ConfigureAwait(false);

                return new PostgresNetworkAccessResult
                {
                    Success = true,
                    LanIp = lan.ipAddress,
                    AllowedCidr = lan.cidr,
                    DataDirectory = dataDirectory,
                    Message = $"PostgreSQL готовий для локальної мережі. Сервер для інших ПК: {lan.ipAddress}:5432. Дозволено діапазон {lan.cidr}."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PostgresNetworkAccessService.ConfigureLanAccessAsync", ex);
                return new PostgresNetworkAccessResult
                {
                    LanIp = lan.ipAddress,
                    AllowedCidr = lan.cidr,
                    Message = $"Не вдалося налаштувати PostgreSQL для локальної мережі: {ex.Message}"
                };
            }
        }

        public static bool IsValidPostgresDataDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            return File.Exists(Path.Combine(path, "PG_VERSION"))
                && File.Exists(Path.Combine(path, "pg_hba.conf"))
                && File.Exists(Path.Combine(path, "postgresql.conf"));
        }

        private string ResolvePostgresDataDirectory()
        {
            var configuredPath = _appSettingsService.Settings.PostgresDataDirectoryPath;
            if (IsValidPostgresDataDirectory(configuredPath))
                return configuredPath;

            return FindPostgresDataDirectory();
        }

        private static string FindPostgresDataDirectory()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var postgresRoot = Path.Combine(programFiles, "PostgreSQL");
            if (!Directory.Exists(postgresRoot))
                return string.Empty;

            return Directory.EnumerateDirectories(postgresRoot)
                .Select(path => new
                {
                    DataPath = Path.Combine(path, "data"),
                    Version = ParseVersion(Path.GetFileName(path))
                })
                .Where(item => Directory.Exists(item.DataPath)
                    && File.Exists(Path.Combine(item.DataPath, "pg_hba.conf"))
                    && File.Exists(Path.Combine(item.DataPath, "postgresql.conf")))
                .OrderByDescending(item => item.Version)
                .Select(item => item.DataPath)
                .FirstOrDefault() ?? string.Empty;
        }

        private static int ParseVersion(string? value)
        {
            return int.TryParse(value, out var version) ? version : 0;
        }

        private static void BackupFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("PostgreSQL config file not found.", path);

            var backupPath = $"{path}.agency_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.Copy(path, backupPath, overwrite: false);
        }

        private static void EnsurePgHbaAllowsTailscale(string pgHbaPath)
        {
            EnsurePgHbaAllowsCidr(pgHbaPath, TailscaleCidr, "Agency Contractor Tailscale access", PgHbaLine);
        }

        private static void EnsurePgHbaAllowsCidr(string pgHbaPath, string cidr, string comment, string? line = null)
        {
            var text = File.ReadAllText(pgHbaPath);
            if (text.Contains(cidr, StringComparison.OrdinalIgnoreCase))
                return;

            line ??= $"host    all    all    {cidr}    scram-sha-256";
            File.AppendAllText(pgHbaPath, $"{Environment.NewLine}# {comment}{Environment.NewLine}{line}{Environment.NewLine}");
        }

        private static void EnsureListenAddresses(string postgresqlConfPath)
        {
            var text = File.ReadAllText(postgresqlConfPath);
            var pattern = @"(?m)^\s*#?\s*listen_addresses\s*=\s*.*$";
            var replacement = "listen_addresses = '*'";

            if (Regex.IsMatch(text, pattern))
                text = Regex.Replace(text, pattern, replacement, RegexOptions.None, TimeSpan.FromSeconds(2));
            else
                text += $"{Environment.NewLine}{replacement}{Environment.NewLine}";

            File.WriteAllText(postgresqlConfPath, text);
        }

        private static async Task EnsureFirewallRuleAsync(string ruleName, CancellationToken cancellationToken)
        {
            var arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport=5432";
            await RunProcessAsync("netsh.exe", arguments, cancellationToken).ConfigureAwait(false);
        }

        private static async Task RestartPostgresServicesAsync(CancellationToken cancellationToken)
        {
            const string command = "Get-Service -Name 'postgresql*' | Restart-Service -Force";
            await RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode == 0)
                return;

            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }

        private static string GetTailscaleIpAddress()
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up
                        && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address)
                    .Where(IsTailscaleIpAddress)
                    .ToList();

                return candidates.FirstOrDefault()?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PostgresNetworkAccess.GetTailscaleIpAddress", ex.Message);
                return string.Empty;
            }
        }

        private static bool IsTailscaleIpAddress(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4
                && bytes[0] == 100
                && bytes[1] >= 64
                && bytes[1] <= 127;
        }

        private static (string ipAddress, string cidr) GetLanNetworkInfo()
        {
            try
            {
                var address = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up
                        && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork
                        && IsPrivateLanIpAddress(item.Address)
                        && !IsTailscaleIpAddress(item.Address))
                    .Select(item => new
                    {
                        Address = item.Address,
                        Mask = item.IPv4Mask
                    })
                    .FirstOrDefault();

                if (address == null)
                    return (string.Empty, string.Empty);

                return (address.Address.ToString(), BuildCidr(address.Address, address.Mask));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PostgresNetworkAccess.GetLanNetworkInfo", ex.Message);
                return (string.Empty, string.Empty);
            }
        }

        private static bool IsPrivateLanIpAddress(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return false;

            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        private static string BuildCidr(IPAddress address, IPAddress? mask)
        {
            var addressBytes = address.GetAddressBytes();
            var maskBytes = mask?.GetAddressBytes();
            if (addressBytes.Length != 4 || maskBytes == null || maskBytes.Length != 4)
                return $"{addressBytes[0]}.{addressBytes[1]}.{addressBytes[2]}.0/24";

            var prefixLength = 0;
            foreach (var value in maskBytes)
            {
                for (var bit = 7; bit >= 0; bit--)
                {
                    if ((value & (1 << bit)) != 0)
                        prefixLength++;
                }
            }

            var networkBytes = new byte[4];
            for (var i = 0; i < 4; i++)
                networkBytes[i] = (byte)(addressBytes[i] & maskBytes[i]);

            return $"{networkBytes[0]}.{networkBytes[1]}.{networkBytes[2]}.{networkBytes[3]}/{prefixLength}";
        }
    }
}
