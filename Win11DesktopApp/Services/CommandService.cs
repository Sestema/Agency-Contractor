using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.Services
{
    public sealed class RemoteCommand
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CommandType { get; set; } = string.Empty;
        public JsonElement? PayloadJson { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? ExecutedAt { get; set; }
        public JsonElement? ResultJson { get; set; }
        public string ErrorText { get; set; } = string.Empty;
    }

    public static class CommandService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public static Task<List<RemoteCommand>> GetPendingCommandsAsync(string? clientId)
        {
            return Task.FromResult(new List<RemoteCommand>());
        }

        public static async Task ExecutePendingCommandsAsync(IEnumerable<RemoteCommand> commands, string? clientId)
        {
            foreach (var command in commands)
            {
                try
                {
                    var result = await ExecuteCommandAsync(command, clientId).ConfigureAwait(false);
                    await AcknowledgeCommandAsync(command.Id, "ack", result, null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await AcknowledgeCommandAsync(command.Id, "failed", null, ex.Message).ConfigureAwait(false);
                    LoggingService.LogWarning("CommandService.ExecutePending", ex.Message);
                }
            }
        }

        private static async Task<Dictionary<string, object?>> ExecuteCommandAsync(RemoteCommand command, string? clientId)
        {
            switch ((command.CommandType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "show_message":
                    await ShowMessageAsync(command).ConfigureAwait(false);
                    return new Dictionary<string, object?> { ["message"] = "shown" };

                case "run_update_check":
                    {
                        var update = await UpdateService.CheckForUpdatesAsync(PolicyService.CurrentPolicy.UpdateChannel).ConfigureAwait(false);
                        var text = update == null
                            ? "Оновлень не знайдено"
                            : $"Доступне оновлення {update.TargetFullRelease.Version}";

                        await Application.Current.Dispatcher.InvokeAsync(() => ToastService.Instance.Info(text));
                        return new Dictionary<string, object?> { ["update_available"] = update != null, ["message"] = text };
                    }

                case "open_license_window":
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Backward-compatible command name: now opens the server-first access window.
                        var window = new LicenseWindow
                        {
                            Owner = Application.Current.MainWindow
                        };
                        window.ShowDialog();
                    });
                    return new Dictionary<string, object?> { ["opened"] = true, ["window"] = "access" };

                case "enter_readonly_mode":
                    {
                        var adHocPolicy = PolicyService.CurrentPolicy ?? new RemotePolicy();
                        adHocPolicy.ReadOnlyMode = true;
                        if (TryGetPayloadString(command.PayloadJson, "admin_message", out var message))
                            adHocPolicy.AdminMessage = message;

                        await PolicyService.ApplyPolicyAsync(adHocPolicy).ConfigureAwait(false);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            ToastService.Instance.Warning("Клієнт переведено в read-only режим адміністратором."));
                        return new Dictionary<string, object?> { ["read_only_mode"] = true };
                    }

                case "restart_app":
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1500).ConfigureAwait(false);
                        try
                        {
                            var exePath = Environment.ProcessPath;
                            if (!string.IsNullOrWhiteSpace(exePath))
                            {
                                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                                await Application.Current.Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogWarning("CommandService.Restart", ex.Message);
                        }
                    });
                    return new Dictionary<string, object?> { ["restart_scheduled"] = true };

                case "upload_diagnostics":
                    LoggingService.LogWarning("CommandService.ExecuteRemoteCommand",
                        "upload_diagnostics command ignored because diagnostics uploads are disabled.");
                    return new Dictionary<string, object?> { ["uploaded"] = false, ["disabled"] = true };

                default:
                    throw new InvalidOperationException($"Unknown remote command: {command.CommandType}");
            }
        }

        private static async Task ShowMessageAsync(RemoteCommand command)
        {
            var message = TryGetPayloadString(command.PayloadJson, "message", out var text)
                ? text
                : "Повідомлення від адміністратора";
            var severity = TryGetPayloadString(command.PayloadJson, "severity", out var severityText)
                ? severityText
                : "info";
            var modal = TryGetPayloadBool(command.PayloadJson, "modal", out var modalValue) && modalValue;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (modal)
                {
                    MessageBox.Show(message, "Повідомлення адміністратора", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                switch (severity.ToLowerInvariant())
                {
                    case "warning":
                        ToastService.Instance.Warning(message);
                        break;
                    case "error":
                        ToastService.Instance.Error(message);
                        break;
                    case "success":
                        ToastService.Instance.Success(message);
                        break;
                    default:
                        ToastService.Instance.Info(message);
                        break;
                }
            });
        }

        private static async Task AcknowledgeCommandAsync(string commandId, string status, Dictionary<string, object?>? result, string? errorText)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                return;

            try
            {
                var acked = await TelemetryService.AcknowledgeCommandAsync(commandId, status, result, errorText).ConfigureAwait(false);
                if (!acked)
                    LoggingService.LogWarning("CommandService.Acknowledge", $"Gateway ack failed for command {commandId}.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("CommandService.Acknowledge", ex.Message);
            }
        }

        private static bool TryGetPayloadString(JsonElement? payload, string property, out string value)
        {
            value = string.Empty;
            if (payload?.ValueKind != JsonValueKind.Object || !payload.Value.TryGetProperty(property, out var element))
                return false;

            value = element.GetString() ?? string.Empty;
            return true;
        }

        private static bool TryGetPayloadBool(JsonElement? payload, string property, out bool value)
        {
            value = false;
            if (payload?.ValueKind != JsonValueKind.Object || !payload.Value.TryGetProperty(property, out var element))
                return false;

            if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
            {
                value = element.GetBoolean();
                return true;
            }

            return false;
        }
    }
}
