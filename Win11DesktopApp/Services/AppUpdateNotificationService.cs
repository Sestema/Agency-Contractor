using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services;

public sealed class AppUpdateNotificationService
{
    private const string ReleaseNotesFileName = "release-notes.uk.json";
    private readonly AppNotificationService _notificationService;
    private string? _lastAvailableVersionNotified;

    public AppUpdateNotificationService(AppNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void NotifyInstalledUpdate(string? fromVersion)
    {
        var currentVersion = AppSettingsService.CurrentAppVersion;
        var notes = GetReleaseNotes(currentVersion);
        var versionText = string.IsNullOrWhiteSpace(fromVersion)
            ? $"Встановлено версію v{currentVersion}."
            : $"Оновлено з v{fromVersion} до v{currentVersion}.";
        var message = BuildMessage(versionText, notes);

        _notificationService.Success("Програму оновлено", message, showToast: true);
    }

    public async Task CheckForAvailableUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);

            var update = await UpdateService.CheckForUpdatesAsync(PolicyService.CurrentPolicy.UpdateChannel).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (update == null)
                return;

            var targetVersion = update.TargetFullRelease.Version?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetVersion))
                return;

            if (string.Equals(_lastAvailableVersionNotified, targetVersion, StringComparison.OrdinalIgnoreCase))
                return;

            _lastAvailableVersionNotified = targetVersion;
            var notes = GetReleaseNotes(targetVersion);
            var message = BuildMessage(
                $"Доступна нова версія v{targetVersion}. Поточна версія: v{AppSettingsService.CurrentAppVersion}. Відкрийте Налаштування -> Оновлення, щоб встановити її.",
                notes);

            _notificationService.Info("Доступна нова версія програми", message, showToast: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("AppUpdateNotificationService.CheckForAvailableUpdate", ex.Message);
        }
    }

    private static IReadOnlyList<string> GetReleaseNotes(string version)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "ReleaseNotes", ReleaseNotesFileName);
            if (!File.Exists(path))
                return Array.Empty<string>();

            var json = File.ReadAllText(path);
            var notes = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
            if (notes == null)
                return Array.Empty<string>();

            return notes.TryGetValue(NormalizeVersion(version), out var currentNotes)
                ? currentNotes.Where(note => !string.IsNullOrWhiteSpace(note)).ToArray()
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("AppUpdateNotificationService.GetReleaseNotes", ex.Message);
            return Array.Empty<string>();
        }
    }

    private static string BuildMessage(string lead, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
            return lead;

        return $"{lead}\nЩо нового:\n- {string.Join("\n- ", notes)}";
    }

    private static string NormalizeVersion(string version)
    {
        var normalized = version.Trim();
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized[1..]
            : normalized;
    }
}
