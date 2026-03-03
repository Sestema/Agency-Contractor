using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public class GeminiApiService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

        public static readonly string[] AvailableModels = {
            "gemini-2.5-flash",
            "gemini-2.5-pro",
            "gemini-2.0-flash",
        };

        private string? _apiKey;
        private string _model = "gemini-2.5-flash";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
        public string CurrentModel => _model;

        public void SetApiKey(string? key)
        {
            _apiKey = key?.Trim();
        }

        public void SetModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
                _model = model;
        }

        public async Task<string> ChatAsync(string message, string? systemPrompt = null, CancellationToken ct = default)
        {
            return await ChatWithHistoryAsync(null, message, systemPrompt, ct);
        }

        public async Task<string> ChatWithHistoryAsync(
            System.Collections.Generic.List<(string role, string text)>? history,
            string message, string? systemPrompt = null, CancellationToken ct = default)
        {
            if (!IsConfigured) return "[Gemini API key not set]";

            try
            {
                var contents = new System.Collections.Generic.List<object>();

                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    contents.Add(new { role = "user", parts = new[] { new { text = $"[System instruction]: {systemPrompt}" } } });
                    contents.Add(new { role = "model", parts = new[] { new { text = "Understood. I will follow these instructions." } } });
                }

                if (history != null)
                {
                    foreach (var (role, text) in history)
                        contents.Add(new { role, parts = new[] { new { text } } });
                }

                contents.Add(new { role = "user", parts = new[] { new { text = message } } });

                var body = new { contents };
                return await SendRequest(body, ct);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.Chat", ex);
                return $"[Error: {ex.Message}]";
            }
        }

        public async Task<string> ChatWithImageAsync(string imagePath, string message, string? systemPrompt = null, CancellationToken ct = default)
        {
            if (!IsConfigured) return "[Gemini API key not set]";
            if (!File.Exists(imagePath)) return "[Image not found]";

            try
            {
                var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
                var base64 = Convert.ToBase64String(imageBytes);
                var mimeType = GetMimeType(imagePath);

                var contents = new System.Collections.Generic.List<object>();

                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    contents.Add(new { role = "user", parts = new[] { new { text = $"[System instruction]: {systemPrompt}" } } });
                    contents.Add(new { role = "model", parts = new[] { new { text = "Understood. I will follow these instructions." } } });
                }

                var prompt = string.IsNullOrWhiteSpace(message) ? "Describe this image in detail." : message;

                contents.Add(new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = prompt },
                        new { inline_data = new { mime_type = mimeType, data = base64 } }
                    }
                });

                var body = new { contents };
                return await SendRequest(body, ct);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.ChatWithImage", ex);
                return $"[Error: {ex.Message}]";
            }
        }

        public async Task<string> ChatWithFileAsync(string filePath, string message, string? systemPrompt = null, CancellationToken ct = default)
        {
            if (!IsConfigured) return "[Gemini API key not set]";
            if (!File.Exists(filePath)) return "[File not found]";

            try
            {
                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                var base64 = Convert.ToBase64String(fileBytes);
                var mimeType = GetMimeType(filePath);

                var contents = new System.Collections.Generic.List<object>();

                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    contents.Add(new { role = "user", parts = new[] { new { text = $"[System instruction]: {systemPrompt}" } } });
                    contents.Add(new { role = "model", parts = new[] { new { text = "Understood. I will follow these instructions." } } });
                }

                var prompt = string.IsNullOrWhiteSpace(message) ? "Analyze this document in detail." : message;

                contents.Add(new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = prompt },
                        new { inline_data = new { mime_type = mimeType, data = base64 } }
                    }
                });

                var body = new { contents };
                return await SendRequest(body, ct);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.ChatWithFile", ex);
                return $"[Error: {ex.Message}]";
            }
        }

        public async Task<(bool success, string message)> TestConnectionAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return (false, "API key not set");

            try
            {
                var result = await ChatAsync("Reply with exactly: OK", ct: ct);
                if (result.StartsWith("[Error")) return (false, result);
                return (true, "Gemini connected: " + result.Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private const int MaxRetries = 3;
        private static readonly int[] RetryDelaysMs = { 1000, 2000, 4000 };

        private async Task<string> SendRequest(object body, CancellationToken ct)
        {
            var url = $"{BaseUrl}/{_model}:generateContent?key={_apiKey}";
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(url, content, ct);

                const long MaxResponseBytes = 10 * 1024 * 1024; // 10 MB
                if (response.Content.Headers.ContentLength > MaxResponseBytes)
                    return "[Error: Response too large]";

                var responseText = await response.Content.ReadAsStringAsync(ct);

                if (responseText.Length > MaxResponseBytes)
                    return "[Error: Response too large]";

                if (response.IsSuccessStatusCode)
                    return ExtractText(responseText);

                var statusCode = (int)response.StatusCode;
                bool retryable = statusCode is 429 or 500 or 502 or 503;

                if (!retryable || attempt >= MaxRetries)
                {
                    var errorMsg = TryExtractError(responseText) ?? response.StatusCode.ToString();
                    return $"[Error: {errorMsg}]";
                }

                await Task.Delay(RetryDelaysMs[attempt], ct);
            }

            return "[Error: Max retries exceeded]";
        }

        private static string ExtractText(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var candidates = doc.RootElement.GetProperty("candidates");
                var first = candidates[0];
                var parts = first.GetProperty("content").GetProperty("parts");
                var sb = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textEl))
                        sb.Append(textEl.GetString());
                }
                return sb.ToString().Trim();
            }
            catch
            {
                return "[Failed to parse Gemini response]";
            }
        }

        private static string? TryExtractError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                    return err.GetProperty("message").GetString();
            }
            catch (Exception ex) { LoggingService.LogWarning("GeminiApi.TryExtractError", ex.Message); }
            return null;
        }

        public static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif";
        }

        public static bool IsDocumentFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".pdf" or ".xlsx" or ".docx";
        }

        private static string GetMimeType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}
