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
        public string DataDirectory { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class PostgresNetworkAccessService
    {
        private const string TailscaleCidr = "100.64.0.0/10";
        private const string PgHbaLine = "host    all    all    100.64.0.0/10    scram-sha-256";

        public string TailscaleIpAddress => GetTailscaleIpAddress();

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
                var dataDirectory = FindPostgresDataDirectory();
                if (string.IsNullOrWhiteSpace(dataDirectory))
                {
                    return new PostgresNetworkAccessResult
                    {
                        TailscaleIp = tailscaleIp,
                        Message = "Не вдалося знайти папку PostgreSQL data. Перевірте шлях C:\\Program Files\\PostgreSQL\\...\\data."
                    };
                }

                var pgHbaPath = Path.Combine(dataDirectory, "pg_hba.conf");
                var postgresqlConfPath = Path.Combine(dataDirectory, "postgresql.conf");
                BackupFile(pgHbaPath);
                BackupFile(postgresqlConfPath);

                EnsurePgHbaAllowsTailscale(pgHbaPath);
                EnsureListenAddresses(postgresqlConfPath);
                await EnsureFirewallRuleAsync(cancellationToken).ConfigureAwait(false);
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
            var text = File.ReadAllText(pgHbaPath);
            if (text.Contains(TailscaleCidr, StringComparison.OrdinalIgnoreCase))
                return;

            File.AppendAllText(pgHbaPath, $"{Environment.NewLine}# Agency Contractor Tailscale access{Environment.NewLine}{PgHbaLine}{Environment.NewLine}");
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

        private static async Task EnsureFirewallRuleAsync(CancellationToken cancellationToken)
        {
            var arguments = "advfirewall firewall add rule name=\"Agency Contractor PostgreSQL 5432\" dir=in action=allow protocol=TCP localport=5432";
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
    }
}
