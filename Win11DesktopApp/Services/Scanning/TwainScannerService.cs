using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NTwain;
using NTwain.Data;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public sealed class TwainScannerService : IScannerService
    {
        private static readonly object SessionLock = new();

        public bool IsAvailable
        {
            get
            {
                try
                {
                    return RunOnUiThreadSync(() =>
                    {
                        lock (SessionLock)
                        {
                            var appId = CreateAppId();
                            var session = new TwainSession(appId);
                            try
                            {
                                session.Open();
                                var count = session.GetSources()?.Count() ?? 0;
                                return count > 0;
                            }
                            finally
                            {
                                try { session.Close(); } catch { }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("TwainScannerService.IsAvailable", ex.Message);
                    return false;
                }
            }
        }

        public Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            return RunOnUiThread(() =>
            {
                var devices = new List<ScannerDeviceInfo>();
                lock (SessionLock)
                {
                    try
                    {
                        var appId = CreateAppId();
                        var session = new TwainSession(appId);
                        try
                        {
                            session.Open();
                            foreach (var source in session.GetSources())
                            {
                                var name = source.Name ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(name))
                                    continue;

                                LoggingService.LogInfo("TwainScannerService", $"TWAIN source detected: {name}");
                                devices.Add(new ScannerDeviceInfo
                                {
                                    Id = $"twain:{name}",
                                    Name = name,
                                    Provider = "TWAIN"
                                });
                            }
                        }
                        finally
                        {
                            try { session.Close(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("TwainScannerService.GetDevicesAsync", ex.Message);
                    }
                }

                LoggingService.LogInfo("TwainScannerService", $"TWAIN sources listed: {devices.Count}");
                return (IReadOnlyList<ScannerDeviceInfo>)devices;
            }, cancellationToken);
        }

        public Task<string> ScanToFileAsync(ScanSettings settings, string outputFolder, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Output folder is required.", nameof(outputFolder));

            Directory.CreateDirectory(outputFolder);
            var sourceName = UnwrapDeviceId(settings.DeviceId, "twain:");
            return RunOnUiThread(() => ScanInternal(sourceName, outputFolder), cancellationToken);
        }

        public Task<string> ScanViaDialogAsync(string outputFolder, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputFolder);
            return RunOnUiThread(() => ScanInternal(null, outputFolder), cancellationToken);
        }

        public Task<ScannerDeviceInfo?> PickDeviceViaDialogAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ScannerDeviceInfo?>(null);
        }

        private static string ScanInternal(string? sourceName, string outputFolder)
        {
            lock (SessionLock)
            {
                string? savedPath = null;
                Exception? transferError = null;
                var appId = CreateAppId();
                var session = new TwainSession(appId);

                session.DataTransferred += (_, e) =>
                {
                    try
                    {
                        using var stream = e.GetNativeImageStream();
                        if (stream == null)
                            return;

                        var tempPath = Path.Combine(outputFolder, $"twain-{Guid.NewGuid():N}.bmp");
                        using (var file = File.Create(tempPath))
                            stream.CopyTo(file);

                        var jpgPath = Path.Combine(outputFolder, $"twain-{Guid.NewGuid():N}.jpg");
                        var imageService = new ImageEnhancementService();
                        using var mat = imageService.LoadImage(tempPath);
                        imageService.SaveImage(mat, jpgPath);
                        try { File.Delete(tempPath); } catch { }

                        savedPath = jpgPath;
                    }
                    catch (Exception ex)
                    {
                        transferError = ex;
                    }
                };

                session.TransferError += (_, e) =>
                {
                    transferError ??= e.Exception ?? new InvalidOperationException("TWAIN transfer error.");
                };

                session.Open();
                var source = ResolveSource(session, sourceName);
                if (source == null)
                    throw new InvalidOperationException("No TWAIN scanner source found.");

                source.Open();
                source.Enable(SourceEnableMode.ShowUI, false, IntPtr.Zero);
                source.Close();
                try { session.Close(); } catch { }

                if (transferError != null)
                    throw transferError;

                if (string.IsNullOrWhiteSpace(savedPath))
                    throw new OperationCanceledException("TWAIN scan cancelled or produced no image.");

                return savedPath;
            }
        }

        private static DataSource? ResolveSource(TwainSession session, string? sourceName)
        {
            var sources = session.GetSources()?.ToList() ?? new List<DataSource>();
            if (sources.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                var match = sources.FirstOrDefault(s =>
                    string.Equals(s.Name, sourceName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return sources.FirstOrDefault();
        }

        private static TWIdentity CreateAppId() =>
            TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());

        private static string? UnwrapDeviceId(string? deviceId, string prefix)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return null;

            return deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? deviceId[prefix.Length..]
                : deviceId;
        }

        private static T RunOnUiThreadSync<T>(Func<T> action)
        {
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess())
                return action();

            return dispatcher.Invoke(action);
        }

        private static Task<T> RunOnUiThread<T>(Func<T> action, CancellationToken cancellationToken)
        {
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            return dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                return action();
            }, DispatcherPriority.Normal).Task;
        }
    }
}
