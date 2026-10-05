using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Win11DesktopApp.Services
{
    public sealed class DocxEditorWebHost : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly Dictionary<int, string?[]> _saveChunks = new();
        private WebView2? _webView;
        private int _nextRequestId = 1;
        private bool _ready;
        private bool _disposed;

        public event Action? Ready;
        public event Action? DocumentLoaded;
        public event Action<string>? DocumentLoadFailed;
        public event Action? DocumentChanged;
        public event Action? SaveRequested;

        public bool IsReady => _ready;

        public async Task InitializeAsync(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgencyContractor", "WebView2", "DocxEditor");
            Directory.CreateDirectory(userData);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await webView.EnsureCoreWebView2Async(environment);

            var editorFolder = Path.Combine(AppContext.BaseDirectory, "WebPanel", "docx-editor");
            if (!File.Exists(Path.Combine(editorFolder, "index.html")))
                throw new FileNotFoundException("DOCX editor web assets were not found.", editorFolder);

            var core = webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                "app-editor.local",
                editorFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(
                "app-fonts.local",
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnWebMessageReceived;

            try
            {
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
                await core.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DocxEditorWebHost.DisableCache", ex.Message);
            }

            var indexStamp = File.GetLastWriteTimeUtc(Path.Combine(editorFolder, "index.html")).Ticks;
            core.Navigate($"https://app-editor.local/index.html?v={indexStamp}");
        }

        public void SendInit(string locale, IReadOnlyList<string> tags)
        {
            Post(new
            {
                type = "init",
                locale,
                tags,
                fonts = WindowsEditorFontCatalog.Build("https://app-fonts.local")
            });
        }

        public void LoadDocument(byte[]? docxBytes)
        {
            if (docxBytes == null || docxBytes.Length == 0)
            {
                LoggingService.LogWarning("DocxEditorWebHost.LoadDocument", "Refusing to open an empty document.");
                Post(new { type = "loadStart", count = 0 });
                return;
            }

            var payload = Convert.ToBase64String(docxBytes);
            const int chunkChars = 200_000;
            var count = Math.Max(1, (payload.Length + chunkChars - 1) / chunkChars);
            LoggingService.LogInfo("DocxEditorWebHost.LoadDocument", $"Sending {docxBytes.Length} bytes in {count} message(s).");
            Post(new { type = "loadStart", count });
            for (var index = 0; index < count; index++)
            {
                var start = index * chunkChars;
                var length = Math.Min(chunkChars, payload.Length - start);
                Post(new { type = "loadChunk", index, data = payload.Substring(start, length) });
            }
        }

        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            Post(new { type = "insertText", text });
        }

        public async Task<byte[]?> SaveAsync()
        {
            var result = await RequestAsync("save");
            if (result.ValueKind != JsonValueKind.String)
                return null;
            var base64 = result.GetString();
            return string.IsNullOrEmpty(base64) ? null : Convert.FromBase64String(base64);
        }

        public async Task<string?> GetPlainTextAsync()
        {
            var result = await RequestAsync("plainText");
            return result.ValueKind == JsonValueKind.String ? result.GetString() : string.Empty;
        }

        public async Task<int> ReplaceTagsAsync(IReadOnlyList<(string ContextBefore, string ReplaceWhat, string Tag)> items)
        {
            var payload = new object[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                payload[i] = new
                {
                    contextBefore = items[i].ContextBefore,
                    replaceWhat = items[i].ReplaceWhat,
                    tag = items[i].Tag
                };
            }

            var result = await RequestAsync("replaceTags", payload);
            return result.ValueKind == JsonValueKind.Number ? result.GetInt32() : 0;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_webView?.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;

            foreach (var pending in _pending.Values)
                pending.TrySetCanceled();
            _pending.Clear();
        }

        private async Task<JsonElement> RequestAsync(string op, object? items = null)
        {
            if (_webView?.CoreWebView2 == null)
                throw new InvalidOperationException("Editor web view is not initialized.");

            var id = _nextRequestId++;
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            Post(new { type = "request", id, op, items });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2)));
            if (completed != tcs.Task)
            {
                _pending.Remove(id);
                throw new TimeoutException("The document editor did not answer in time.");
            }

            return await tcs.Task;
        }

        private void Post(object message)
        {
            _webView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var document = JsonDocument.Parse(e.WebMessageAsJson);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                switch (type)
                {
                    case "ready":
                        _ready = true;
                        Ready?.Invoke();
                        break;
                    case "loaded":
                        DocumentLoaded?.Invoke();
                        break;
                    case "loadFailed":
                        var failure = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;
                        LoggingService.LogWarning("DocxEditorWebHost.LoadFailed", failure);
                        DocumentLoadFailed?.Invoke(failure);
                        break;
                    case "trace":
                        LoggingService.LogInfo("DocxEditorWebHost.Trace", root.TryGetProperty("message", out var trace) ? trace.GetString() ?? string.Empty : string.Empty);
                        break;
                    case "changed":
                        DocumentChanged?.Invoke();
                        break;
                    case "saveRequested":
                        SaveRequested?.Invoke();
                        break;
                    case "response":
                        CompleteRequest(root);
                        break;
                    case "saveChunk":
                        AcceptSaveChunk(root);
                        break;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DocxEditorWebHost.OnWebMessageReceived", ex.Message);
            }
        }

        private void CompleteRequest(JsonElement root)
        {
            if (!root.TryGetProperty("id", out var idProp) || !_pending.Remove(idProp.GetInt32(), out var tcs))
                return;

            var ok = !root.TryGetProperty("ok", out var okProp) || okProp.GetBoolean();
            if (!ok)
            {
                var error = root.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "Editor request failed";
                tcs.TrySetException(new InvalidOperationException(error));
                return;
            }

            tcs.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
        }

        private void AcceptSaveChunk(JsonElement root)
        {
            if (!root.TryGetProperty("id", out var idProp) || !root.TryGetProperty("index", out var indexProp) || !root.TryGetProperty("count", out var countProp))
                return;

            var id = idProp.GetInt32();
            var index = indexProp.GetInt32();
            var count = countProp.GetInt32();
            if (count <= 0 || index < 0 || index >= count || !_pending.ContainsKey(id))
                return;

            if (!_saveChunks.TryGetValue(id, out var parts) || parts.Length != count)
            {
                parts = new string?[count];
                _saveChunks[id] = parts;
            }

            parts[index] = root.TryGetProperty("data", out var dataProp) ? dataProp.GetString() ?? string.Empty : string.Empty;
            if (parts.Any(part => part == null))
                return;

            _saveChunks.Remove(id);
            if (!_pending.Remove(id, out var tcs))
                return;

            var combined = string.Concat(parts);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(combined));
            tcs.TrySetResult(json.RootElement.Clone());
        }
    }

    internal static class WindowsEditorFontCatalog
    {
        private static readonly (string FileName, string Family, int Weight, string Style)[] Faces =
        {
            ("arial.ttf", "Arial", 400, "normal"),
            ("arialbd.ttf", "Arial", 700, "normal"),
            ("ariali.ttf", "Arial", 400, "italic"),
            ("arialbi.ttf", "Arial", 700, "italic"),
            ("calibri.ttf", "Calibri", 400, "normal"),
            ("calibrib.ttf", "Calibri", 700, "normal"),
            ("calibrii.ttf", "Calibri", 400, "italic"),
            ("calibriz.ttf", "Calibri", 700, "italic"),
            ("times.ttf", "Times New Roman", 400, "normal"),
            ("timesbd.ttf", "Times New Roman", 700, "normal"),
            ("timesi.ttf", "Times New Roman", 400, "italic"),
            ("timesbi.ttf", "Times New Roman", 700, "italic"),
            ("segoeui.ttf", "Segoe UI", 400, "normal"),
            ("segoeuib.ttf", "Segoe UI", 700, "normal"),
            ("segoeuii.ttf", "Segoe UI", 400, "italic"),
            ("segoeuiz.ttf", "Segoe UI", 700, "italic"),
            ("cour.ttf", "Courier New", 400, "normal"),
            ("courbd.ttf", "Courier New", 700, "normal"),
            ("couri.ttf", "Courier New", 400, "italic"),
            ("courbi.ttf", "Courier New", 700, "italic")
        };

        public static object[] Build(string hostOrigin)
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var result = new List<object>();
            foreach (var face in Faces)
            {
                if (!File.Exists(Path.Combine(fontsDir, face.FileName)))
                    continue;
                result.Add(new
                {
                    family = face.Family,
                    weight = face.Weight,
                    style = face.Style,
                    url = $"{hostOrigin}/{face.FileName}"
                });
            }
            return result.ToArray();
        }
    }
}
