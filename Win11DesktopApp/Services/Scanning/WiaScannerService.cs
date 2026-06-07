using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public sealed class WiaScannerService : IScannerService
    {
        private const int ScannerDeviceType = 1;
        private const int UnspecifiedDeviceType = 0;
        private const int CameraDeviceType = 2;
        private const int VideoDeviceType = 3;
        private const int UnknownDeviceType = -1;
        private const int WiaDipDevId = 2;
        private const int WiaDipDevName = 7;
        private const int WiaDipDevDesc = 8;
        private const int WiaIntentColor = 1;
        private const int WiaIntentGrayscale = 2;
        private const int WiaIntentText = 4;
        private const int WiaImageBiasMaximizeQuality = 131072;
        private const int WiaHorizontalResolution = 6147;
        private const int WiaVerticalResolution = 6148;
        private const int WiaCurrentIntent = 6146;
        private const int WiaDocumentHandlingSelect = 3088;
        private const string WiaFormatJpeg = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

        public bool IsAvailable => OperatingSystem.IsWindows() && Type.GetTypeFromProgID("WIA.DeviceManager") != null;

        public Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
                return Task.FromResult<IReadOnlyList<ScannerDeviceInfo>>(Array.Empty<ScannerDeviceInfo>());

            return RunOnUiThread(GetDevicesInternal, cancellationToken);
        }

        public Task<string> ScanToFileAsync(ScanSettings settings, string outputFolder, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
                throw new InvalidOperationException("WIA scanner is not available on this system.");

            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Output folder is required.", nameof(outputFolder));

            Directory.CreateDirectory(outputFolder);
            return RunOnUiThread(() => ScanInternal(settings, outputFolder, cancellationToken), cancellationToken);
        }

        public Task<string> ScanViaDialogAsync(string outputFolder, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
                throw new InvalidOperationException("WIA scanner is not available on this system.");

            Directory.CreateDirectory(outputFolder);
            return RunOnUiThread(() => ScanViaCommonDialog(outputFolder, cancellationToken), cancellationToken);
        }

        public Task<ScannerDeviceInfo?> PickDeviceViaDialogAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
                return Task.FromResult<ScannerDeviceInfo?>(null);

            return RunOnUiThread(PickDeviceViaCommonDialog, cancellationToken);
        }

        private static void ThrowIfCancelled(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher?.HasShutdownStarted == true)
                throw new OperationCanceledException("Application is shutting down.");
        }

        private static Task<T> RunOnUiThread<T>(Func<T> action, CancellationToken cancellationToken)
        {
            ThrowIfCancelled(cancellationToken);

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess())
            {
                ThrowIfCancelled(cancellationToken);
                return Task.FromResult(action());
            }

            return dispatcher.InvokeAsync(() =>
            {
                ThrowIfCancelled(cancellationToken);
                return action();
            }, DispatcherPriority.Normal).Task;
        }

        private static IReadOnlyList<ScannerDeviceInfo> GetDevicesInternal()
        {
            var candidates = new List<WiaDeviceCandidate>();
            dynamic? manager = null;

            try
            {
                manager = CreateDeviceManager();
                foreach (dynamic info in manager.DeviceInfos)
                {
                    try
                    {
                        if (!TryCreateDeviceInfo(info, out WiaDeviceDetails deviceInfo))
                            continue;

                        var rawId = string.IsNullOrWhiteSpace(deviceInfo.RawId)
                            ? deviceInfo.Name
                            : deviceInfo.RawId;

                        if (string.IsNullOrWhiteSpace(rawId))
                            continue;

                        candidates.Add(new WiaDeviceCandidate
                        {
                            Details = deviceInfo,
                            RawId = rawId,
                            Fingerprint = ExtractHardwareFingerprint(deviceInfo.RawId, deviceInfo.Name)
                        });
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("WiaScannerService.GetDevicesInternal.Device", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WiaScannerService.GetDevicesInternal", ex);
            }
            finally
            {
                ReleaseCom(manager);
            }

            return DeduplicateCandidates(candidates);
        }

        private sealed class WiaDeviceCandidate
        {
            public WiaDeviceDetails Details { get; init; } = new();
            public string RawId { get; init; } = string.Empty;
            public string? Fingerprint { get; init; }
        }

        private static List<ScannerDeviceInfo> DeduplicateCandidates(IReadOnlyList<WiaDeviceCandidate> candidates)
        {
            var groups = new Dictionary<string, WiaDeviceCandidate>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                var groupKey = candidate.Fingerprint ?? candidate.RawId;
                if (groups.TryGetValue(groupKey, out var existing))
                {
                    if (!IsPreferredOver(candidate, existing))
                    {
                        LoggingService.LogInfo("WiaScannerService",
                            $"WIA duplicate skipped: Type={candidate.Details.Type}, Name={candidate.Details.Name}, Id={candidate.RawId}");
                        continue;
                    }

                    LoggingService.LogInfo("WiaScannerService",
                        $"WIA duplicate skipped: Type={existing.Details.Type}, Name={existing.Details.Name}, Id={existing.RawId}");
                }

                groups[groupKey] = candidate;
                LoggingService.LogInfo("WiaScannerService",
                    $"WIA device accepted: Type={candidate.Details.Type}, Name={candidate.Details.Name}, Id={candidate.RawId}");
            }

            var devices = new List<ScannerDeviceInfo>();
            foreach (var candidate in groups.Values)
            {
                devices.Add(new ScannerDeviceInfo
                {
                    Id = $"wia:{candidate.RawId}",
                    Name = candidate.Details.Name,
                    Provider = "WIA"
                });
            }

            devices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            LoggingService.LogInfo("WiaScannerService", $"WIA scanners listed: {devices.Count}");
            return devices;
        }

        private static bool IsPreferredOver(WiaDeviceCandidate candidate, WiaDeviceCandidate existing)
        {
            var typeDelta = GetTypePriority(candidate.Details.Type) - GetTypePriority(existing.Details.Type);
            if (typeDelta != 0)
                return typeDelta < 0;

            var nameDelta = GetNamePriority(candidate.Details.Name) - GetNamePriority(existing.Details.Name);
            if (nameDelta != 0)
                return nameDelta < 0;

            return candidate.Details.Name.Length > existing.Details.Name.Length;
        }

        private static int GetTypePriority(int type) => type switch
        {
            ScannerDeviceType => 0,
            UnspecifiedDeviceType => 1,
            UnknownDeviceType or 65535 => 2,
            _ => 3
        };

        private static int GetNamePriority(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return 2;

            if (name.Contains("HP ", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Color Laser", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("MFP", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("LaserJet", StringComparison.OrdinalIgnoreCase))
                return 0;

            if (name.StartsWith("HP", StringComparison.OrdinalIgnoreCase))
                return 1;

            return 2;
        }

        private static string? ExtractHardwareFingerprint(string id, string name)
        {
            foreach (var source in new[] { name, id })
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                var hpMatch = Regex.Match(source, @"HP([0-9A-F]{12})", RegexOptions.IgnoreCase);
                if (hpMatch.Success)
                    return hpMatch.Groups[1].Value.ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(id))
                return null;

            var tail = id.Split('\\', '/');
            var lastSegment = tail.Length > 0 ? tail[^1] : id;
            var hexMatches = Regex.Matches(lastSegment, @"[0-9a-f]{8,12}", RegexOptions.IgnoreCase);
            if (hexMatches.Count == 0)
                return null;

            var fingerprint = hexMatches[^1].Value.ToLowerInvariant();
            return fingerprint.Length >= 8 ? fingerprint : null;
        }

        private sealed class WiaDeviceDetails
        {
            public int Type { get; init; }
            public string RawId { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        private static bool TryCreateDeviceInfo(dynamic info, out WiaDeviceDetails deviceInfo)
        {
            deviceInfo = new WiaDeviceDetails();
            var type = SafeInt(info.Type);
            (string id, string name) = ReadDeviceIdentity((object)info);
            name = NormalizeDeviceName(id, name);

            deviceInfo = new WiaDeviceDetails
            {
                Type = type,
                RawId = id,
                Name = name
            };

            LoggingService.LogInfo("WiaScannerService",
                $"WIA device detected: Type={type}, Name={name}, Id={id}");

            if (type == VideoDeviceType || type == CameraDeviceType)
                return false;

            if (!IsScannerCandidate(type, id, name))
                return false;

            return !string.IsNullOrWhiteSpace(id) || !string.IsNullOrWhiteSpace(name);
        }

        private static bool IsScannerCandidate(int type, string id, string name)
        {
            if (type is ScannerDeviceType or UnspecifiedDeviceType or UnknownDeviceType or 65535)
                return true;

            return LooksLikeScanner(id, name);
        }

        private static bool LooksLikeScanner(string id, string name)
        {
            var combined = $"{id} {name}";
            if (combined.Contains("Escl", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains(@"SWD\", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains(@"WSD\", StringComparison.OrdinalIgnoreCase))
                return true;

            if (combined.Contains("scan", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("mfp", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("laser", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("multifunction", StringComparison.OrdinalIgnoreCase))
                return true;

            return Regex.IsMatch(name, @"HP[0-9A-F]{8,12}", RegexOptions.IgnoreCase);
        }

        private static (string Id, string Name) ReadDeviceIdentity(object source)
        {
            dynamic device = source;
            var id = device.DeviceID as string ?? string.Empty;
            var name = string.Empty;

            try
            {
                name = ReadProperty(device.Properties, "Name")
                    ?? ReadPropertyById(device.Properties, WiaDipDevName)
                    ?? ReadPropertyById(device.Properties, WiaDipDevDesc)
                    ?? id;
            }
            catch
            {
                name = id;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    id = ReadPropertyById(device.Properties, WiaDipDevId) ?? string.Empty;
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = id;

            return (id.Trim(), name.Trim());
        }

        private static string NormalizeDeviceName(string id, string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !IsWiaScannerClassId(name))
                return name;

            if (!string.IsNullOrWhiteSpace(id) && !IsWiaScannerClassId(id))
                return id;

            return name;
        }

        private static bool IsWiaScannerClassId(string value) =>
            value.Contains("6BDD1FC6-810F-11D0-BEC7-08002BE2092F", StringComparison.OrdinalIgnoreCase);

        private static string ScanInternal(ScanSettings settings, string outputFolder, CancellationToken cancellationToken)
        {
            dynamic? manager = null;
            dynamic? device = null;
            dynamic? item = null;
            dynamic? image = null;

            try
            {
                ThrowIfCancelled(cancellationToken);
                manager = CreateDeviceManager();
                var wiaDeviceId = UnwrapDeviceId(settings.DeviceId, "wia:");
                dynamic? deviceInfo = FindDeviceInfo(manager, wiaDeviceId);
                if (deviceInfo == null)
                    throw new InvalidOperationException("No scanner device found.");

                device = deviceInfo.Connect();
                if (device == null)
                    throw new InvalidOperationException("Could not connect to the scanner.");

                item = TryResolveScanItem(device);
                if (item == null)
                    throw new InvalidOperationException("Scanner connected but no scan source was found.");

                TrySetItemProperty(item, WiaHorizontalResolution, settings.Dpi);
                TrySetItemProperty(item, WiaVerticalResolution, settings.Dpi);
                TrySetItemProperty(item, WiaCurrentIntent, settings.ColorMode switch
                {
                    ScanColorMode.Grayscale => WiaIntentGrayscale,
                    ScanColorMode.BlackWhite => WiaIntentText,
                    _ => WiaIntentColor
                });

                if (settings.Source == ScanSource.Feeder)
                    TrySetItemProperty(item, WiaDocumentHandlingSelect, 1);
                else if (settings.Source == ScanSource.Flatbed)
                    TrySetItemProperty(item, WiaDocumentHandlingSelect, 2);

                ThrowIfCancelled(cancellationToken);
                image = item.Transfer(WiaFormatJpeg);
                if (image == null)
                    throw new InvalidOperationException("Scanner returned no image.");

                Directory.CreateDirectory(outputFolder);
                var outputPath = Path.Combine(outputFolder, $"wia-{Guid.NewGuid():N}.jpg");
                image.SaveFile(outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WiaScannerService.ScanInternal", ex);
                throw;
            }
            finally
            {
                ReleaseCom(image);
                ReleaseCom(item);
                ReleaseCom(device);
                ReleaseCom(manager);
            }
        }

        private static string ScanViaCommonDialog(string outputFolder, CancellationToken cancellationToken)
        {
            dynamic? dialog = null;
            dynamic? image = null;

            try
            {
                ThrowIfCancelled(cancellationToken);
                Directory.CreateDirectory(outputFolder);

                dialog = CreateCommonDialog();
                image = dialog.ShowAcquireImage(
                    UnspecifiedDeviceType,
                    WiaIntentColor,
                    WiaImageBiasMaximizeQuality,
                    WiaFormatJpeg,
                    true,
                    true,
                    false);

                if (image == null)
                    throw new OperationCanceledException("Scan cancelled.");

                ThrowIfCancelled(cancellationToken);
                if (!Directory.Exists(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                var outputPath = Path.Combine(outputFolder, $"wia-dialog-{Guid.NewGuid():N}.jpg");
                image.SaveFile(outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WiaScannerService.ScanViaCommonDialog", ex);
                throw;
            }
            finally
            {
                ReleaseCom(image);
                ReleaseCom(dialog);
            }
        }

        private static ScannerDeviceInfo? PickDeviceViaCommonDialog()
        {
            dynamic? dialog = null;
            dynamic? device = null;

            try
            {
                dialog = CreateCommonDialog();
                device = dialog.ShowSelectDevice(UnspecifiedDeviceType, true, true);
                if (device == null)
                    device = dialog.ShowSelectDevice(ScannerDeviceType, true, true);
                if (device == null)
                    return null;

                var id = device.DeviceID as string ?? string.Empty;
                (string pickedId, string pickedName) = ReadDeviceIdentity((object)device);
                if (string.IsNullOrWhiteSpace(id))
                    id = pickedId;
                var name = NormalizeDeviceName(id, pickedName);

                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                    return null;

                var rawId = string.IsNullOrWhiteSpace(id) ? name : id;
                return new ScannerDeviceInfo
                {
                    Id = $"wia:{rawId}",
                    Name = name,
                    Provider = "WIA"
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WiaScannerService.PickDeviceViaCommonDialog", ex.Message);
                return null;
            }
            finally
            {
                ReleaseCom(device);
                ReleaseCom(dialog);
            }
        }

        private static dynamic CreateDeviceManager()
        {
            var type = Type.GetTypeFromProgID("WIA.DeviceManager")
                ?? throw new InvalidOperationException("WIA is not installed.");
            return Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Could not create WIA DeviceManager.");
        }

        private static dynamic CreateCommonDialog()
        {
            var type = Type.GetTypeFromProgID("WIA.CommonDialog")
                ?? throw new InvalidOperationException("WIA CommonDialog is not installed.");
            return Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Could not create WIA CommonDialog.");
        }

        private static dynamic? FindDeviceInfo(dynamic manager, string? deviceId)
        {
            WiaDeviceCandidate? bestCandidate = null;
            dynamic? bestInfo = null;
            dynamic? fallbackInfo = null;

            foreach (dynamic info in manager.DeviceInfos)
            {
                if (!TryCreateDeviceInfo(info, out WiaDeviceDetails details))
                    continue;

                var rawId = string.IsNullOrWhiteSpace(details.RawId) ? details.Name : details.RawId;
                var candidate = new WiaDeviceCandidate
                {
                    Details = details,
                    RawId = rawId,
                    Fingerprint = ExtractHardwareFingerprint(details.RawId, details.Name)
                };

                fallbackInfo ??= info;

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    if (bestCandidate == null || IsPreferredOver(candidate, bestCandidate))
                    {
                        bestCandidate = candidate;
                        bestInfo = info;
                    }

                    continue;
                }

                if (!DeviceIdMatches(candidate, deviceId))
                    continue;

                if (bestCandidate == null || IsPreferredOver(candidate, bestCandidate))
                {
                    bestCandidate = candidate;
                    bestInfo = info;
                }
            }

            return bestInfo ?? fallbackInfo;
        }

        private static bool DeviceIdMatches(WiaDeviceCandidate candidate, string deviceId)
        {
            if (string.Equals(candidate.RawId, deviceId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(candidate.Details.Name, deviceId, StringComparison.OrdinalIgnoreCase))
                return true;

            var fingerprint = ExtractHardwareFingerprint(deviceId, string.Empty);
            if (!string.IsNullOrWhiteSpace(fingerprint) &&
                string.Equals(candidate.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                return true;

            return candidate.RawId.Contains(deviceId, StringComparison.OrdinalIgnoreCase) ||
                   deviceId.Contains(candidate.RawId, StringComparison.OrdinalIgnoreCase);
        }

        private static string? UnwrapDeviceId(string? deviceId, string prefix)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return null;

            return deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? deviceId[prefix.Length..]
                : deviceId;
        }

        private static int SafeInt(object? value)
        {
            try
            {
                return value == null ? -1 : Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static dynamic? TryResolveScanItem(dynamic device)
        {
            try
            {
                var count = SafeInt(device.Items.Count);
                if (count < 1)
                    return null;

                for (var i = 1; i <= count; i++)
                {
                    try
                    {
                        var candidate = device.Items[i];
                        if (candidate != null)
                            return candidate;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("WiaScannerService.TryResolveScanItem", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WiaScannerService.TryResolveScanItem", ex.Message);
            }

            return null;
        }

        private static void TrySetItemProperty(object item, int propertyId, object value)
        {
            if (TrySetPropertyViaIndex(item, propertyId, value) ||
                TrySetPropertyViaEnumeration(item, propertyId, value))
                return;

            LoggingService.LogWarning(
                "WiaScannerService.TrySetItemProperty",
                $"Could not set WIA property {propertyId}; scanner will use its default.");
        }

        private static bool TrySetPropertyViaIndex(object item, int propertyId, object value)
        {
            try
            {
                dynamic dynamicItem = item;
                dynamic properties = dynamicItem.Properties;
                if (properties == null)
                    return false;

                try
                {
                    dynamic property = properties[propertyId];
                    if (property != null && TrySetPropertyValue(property, value))
                        return true;
                }
                catch
                {
                }

                try
                {
                    dynamic property = properties.Item[propertyId];
                    if (property != null && TrySetPropertyValue(property, value))
                        return true;
                }
                catch
                {
                }

                try
                {
                    dynamic property = properties.Item(propertyId);
                    if (property != null && TrySetPropertyValue(property, value))
                        return true;
                }
                catch
                {
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetPropertyViaEnumeration(object item, int propertyId, object value)
        {
            try
            {
                dynamic dynamicItem = item;
                foreach (dynamic property in dynamicItem.Properties)
                {
                    if (property == null)
                        continue;

                    if (SafeInt(property.PropertyID) != propertyId)
                        continue;

                    if (TrySetPropertyValue(property, value))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetPropertyValue(object property, object value)
        {
            if (property == null)
                return false;

            const BindingFlags instancePublic = BindingFlags.Instance | BindingFlags.Public;
            var type = property.GetType();

            try
            {
                type.InvokeMember(
                    "Value",
                    instancePublic | BindingFlags.SetProperty,
                    null,
                    property,
                    new[] { value });
                return true;
            }
            catch
            {
            }

            foreach (var methodName in new[] { "set_Value", "put_Value" })
            {
                try
                {
                    type.InvokeMember(
                        methodName,
                        instancePublic | BindingFlags.InvokeMethod,
                        null,
                        property,
                        new[] { value });
                    return true;
                }
                catch
                {
                }
            }

            try
            {
                dynamic dynamicProperty = property;
                dynamicProperty.Value = value;
                return true;
            }
            catch
            {
            }

            return false;
        }

        private static string? ReadProperty(dynamic properties, string name)
        {
            foreach (dynamic property in properties)
            {
                if (!string.Equals(property.Name as string, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ReadPropertyValue(property);
            }

            return null;
        }

        private static string? ReadPropertyById(dynamic properties, int propertyId)
        {
            foreach (dynamic property in properties)
            {
                if ((int)property.PropertyID != propertyId)
                    continue;

                return ReadPropertyValue(property);
            }

            return null;
        }

        private static string? ReadPropertyValue(dynamic property)
        {
            try
            {
                return property.Value?.ToString();
            }
            catch
            {
                try
                {
                    return property.get_Value()?.ToString();
                }
                catch
                {
                    return null;
                }
            }
        }

        private static void ReleaseCom(object? comObject)
        {
            if (comObject == null)
                return;

            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch
            {
            }
        }
    }
}
