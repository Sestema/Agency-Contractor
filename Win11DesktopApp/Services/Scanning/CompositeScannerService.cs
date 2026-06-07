using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public sealed class CompositeScannerService : IScannerService
    {
        private readonly WiaScannerService _wia = new();
        private readonly TwainScannerService _twain = new();

        public bool IsAvailable => _wia.IsAvailable || _twain.IsAvailable;

        public async Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            var merged = new List<ScannerDeviceInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_wia.IsAvailable)
            {
                foreach (var device in await _wia.GetDevicesAsync(cancellationToken))
                {
                    if (seen.Add(device.Id))
                        merged.Add(device);
                }
            }

            if (_twain.IsAvailable)
            {
                foreach (var device in await _twain.GetDevicesAsync(cancellationToken))
                {
                    if (seen.Add(device.Id))
                        merged.Add(device);
                }
            }

            LoggingService.LogInfo("CompositeScannerService", $"Total scanner devices: {merged.Count}");
            return merged;
        }

        public async Task<string> ScanToFileAsync(ScanSettings settings, string outputFolder, CancellationToken cancellationToken = default)
        {
            if (string.Equals(settings.Provider, "TWAIN", StringComparison.OrdinalIgnoreCase))
                return await _twain.ScanToFileAsync(settings, outputFolder, cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.DeviceId) &&
                settings.DeviceId.StartsWith("twain:", StringComparison.OrdinalIgnoreCase))
            {
                settings.Provider = "TWAIN";
                return await _twain.ScanToFileAsync(settings, outputFolder, cancellationToken);
            }

            Exception? lastError = null;

            if (_wia.IsAvailable)
            {
                try
                {
                    return await _wia.ScanToFileAsync(settings, outputFolder, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LoggingService.LogWarning("CompositeScannerService.ScanToFileAsync.WIA", ex.Message);
                }

                try
                {
                    return await _wia.ScanViaDialogAsync(outputFolder, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LoggingService.LogWarning("CompositeScannerService.ScanToFileAsync.WIA.Dialog", ex.Message);
                }
            }

            if (_twain.IsAvailable)
                return await _twain.ScanViaDialogAsync(outputFolder, cancellationToken);

            throw lastError ?? new InvalidOperationException("No scanner provider is available.");
        }

        public async Task<string> ScanViaDialogAsync(string outputFolder, CancellationToken cancellationToken = default)
        {
            Exception? lastError = null;

            if (_wia.IsAvailable)
            {
                try
                {
                    return await _wia.ScanViaDialogAsync(outputFolder, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LoggingService.LogWarning("CompositeScannerService.ScanViaDialogAsync.WIA", ex.Message);
                }
            }

            if (_twain.IsAvailable)
                return await _twain.ScanViaDialogAsync(outputFolder, cancellationToken);

            throw lastError ?? new InvalidOperationException("No scanner dialog provider is available.");
        }

        public Task<ScannerDeviceInfo?> PickDeviceViaDialogAsync(CancellationToken cancellationToken = default)
        {
            if (_wia.IsAvailable)
                return _wia.PickDeviceViaDialogAsync(cancellationToken);

            return Task.FromResult<ScannerDeviceInfo?>(null);
        }
    }
}
