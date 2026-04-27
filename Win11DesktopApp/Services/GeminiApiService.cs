using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class GeminiFunctionTool
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public object Parameters { get; init; } = new();
    }

    public sealed class GeminiFunctionCall
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ArgumentsJson { get; init; } = "{}";
    }

    public sealed class GeminiToolChatResult
    {
        public string Text { get; init; } = string.Empty;
        public int ToolCallsUsed { get; init; }
    }

    public class GeminiApiService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
        public const string ApiKeyNotSetResponse = "[Gemini API key not set]";
        public const string ParseFailureResponse = "[Failed to parse Gemini response]";
        public const string TimeoutResponse = "[Error: Request timed out]";
        public const string NetworkErrorResponse = "[Error: Network error]";

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
            if (!IsConfigured) return ApiKeyNotSetResponse;

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
            catch (HttpRequestException ex)
            {
                LoggingService.LogWarning("GeminiApiService.Chat", ex.Message);
                return NetworkErrorResponse;
            }
            catch (OperationCanceledException ex)
            {
                LoggingService.LogWarning("GeminiApiService.Chat", ex.Message);
                return TimeoutResponse;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.Chat", ex);
                return $"[Error: {ex.Message}]";
            }
        }

        public async Task<string> TranscribeAudioAsync(byte[] audioBytes, string mimeType = "audio/ogg", string? modelOverride = null, CancellationToken ct = default)
        {
            if (!IsConfigured) return ApiKeyNotSetResponse;
            if (audioBytes == null || audioBytes.Length == 0) return "[Audio not found]";

            try
            {
                var base64 = Convert.ToBase64String(audioBytes);
                var body = new
                {
                    contents = new object[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new { text = "Transcribe this voice message exactly. Return only the spoken text, in the original language, with no commentary." },
                                new { inline_data = new { mime_type = mimeType, data = base64 } }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1
                    }
                };

                return await SendRequest(body, ct, modelOverride).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                LoggingService.LogWarning("GeminiApiService.TranscribeAudio", ex.Message);
                return NetworkErrorResponse;
            }
            catch (OperationCanceledException ex)
            {
                LoggingService.LogWarning("GeminiApiService.TranscribeAudio", ex.Message);
                return TimeoutResponse;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.TranscribeAudio", ex);
                return $"[Error: {ex.Message}]";
            }
        }

        public async Task<GeminiToolChatResult> ChatWithToolsAsync(
            List<(string role, string text)>? history,
            string message,
            IReadOnlyList<GeminiFunctionTool> tools,
            Func<GeminiFunctionCall, CancellationToken, Task<object>> toolExecutor,
            string? systemPrompt = null,
            string? modelOverride = null,
            CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new GeminiToolChatResult { Text = ApiKeyNotSetResponse };

            try
            {
                var contents = new List<object>();

                if (!string.IsNullOrWhiteSpace(systemPrompt))
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

                var declarations = tools?
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    })
                    .ToArray() ?? Array.Empty<object>();

                var toolCount = 0;
                for (var round = 0; round < 6; round++)
                {
                    var body = new
                    {
                        contents,
                        tools = declarations.Length == 0
                            ? Array.Empty<object>()
                            : new object[]
                            {
                                new
                                {
                                    functionDeclarations = declarations
                                }
                            },
                        generationConfig = new
                        {
                            temperature = 0.05,
                            topP = 0.8,
                            topK = 20
                        }
                    };

                    var rawResponse = await SendRawRequest(body, ct, modelOverride).ConfigureAwait(false);
                    if (IsFailureResponse(rawResponse))
                        return new GeminiToolChatResult { Text = rawResponse, ToolCallsUsed = toolCount };

                    using var doc = JsonDocument.Parse(rawResponse);
                    var candidate = GetFirstCandidate(doc.RootElement);
                    if (candidate == null)
                        return new GeminiToolChatResult { Text = ParseFailureResponse, ToolCallsUsed = toolCount };

                    if (!candidate.Value.TryGetProperty("content", out var contentElement))
                        return new GeminiToolChatResult { Text = ParseFailureResponse, ToolCallsUsed = toolCount };

                    var toolCalls = ExtractFunctionCalls(candidate.Value).ToList();
                    if (toolCalls.Count == 0)
                    {
                        return new GeminiToolChatResult
                        {
                            Text = ExtractText(rawResponse),
                            ToolCallsUsed = toolCount
                        };
                    }

                    contents.Add(JsonSerializer.Deserialize<object>(contentElement.GetRawText())!);

                    var toolResponseParts = new List<object>();
                    foreach (var toolCall in toolCalls)
                    {
                        var toolResult = await toolExecutor(toolCall, ct).ConfigureAwait(false);
                        toolCount++;

                        var functionResponse = new Dictionary<string, object?>
                        {
                            ["name"] = toolCall.Name,
                            ["response"] = toolResult
                        };

                        if (!string.IsNullOrWhiteSpace(toolCall.Id))
                            functionResponse["id"] = toolCall.Id;

                        toolResponseParts.Add(new Dictionary<string, object?>
                        {
                            ["functionResponse"] = functionResponse
                        });
                    }

                    contents.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["parts"] = toolResponseParts
                    });
                }

                return new GeminiToolChatResult
                {
                    Text = "[Error: Tool calling exceeded maximum rounds]",
                    ToolCallsUsed = toolCount
                };
            }
            catch (HttpRequestException ex)
            {
                LoggingService.LogWarning("GeminiApiService.ChatWithTools", ex.Message);
                return new GeminiToolChatResult { Text = NetworkErrorResponse };
            }
            catch (OperationCanceledException ex)
            {
                LoggingService.LogWarning("GeminiApiService.ChatWithTools", ex.Message);
                return new GeminiToolChatResult { Text = TimeoutResponse };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.ChatWithTools", ex);
                return new GeminiToolChatResult { Text = $"[Error: {ex.Message}]" };
            }
        }

        public async Task<string> ChatWithImageAsync(string imagePath, string message, string? systemPrompt = null, CancellationToken ct = default)
        {
            if (!IsConfigured) return ApiKeyNotSetResponse;
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
            catch (HttpRequestException ex)
            {
                LoggingService.LogWarning("GeminiApiService.ChatWithImage", ex.Message);
                return NetworkErrorResponse;
            }
            catch (OperationCanceledException ex)
            {
                LoggingService.LogWarning("GeminiApiService.ChatWithImage", ex.Message);
                return TimeoutResponse;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("GeminiApiService.ChatWithImage", ex);
                return $"[Error: {ex.Message}]";
            }
        }

        public async Task<string> ChatWithFileAsync(string filePath, string message, string? systemPrompt = null, CancellationToken ct = default)
        {
            if (!IsConfigured) return ApiKeyNotSetResponse;
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
            catch (HttpRequestException ex)
            {
                LoggingService.LogWarning("GeminiApiService.ChatWithFile", ex.Message);
                return NetworkErrorResponse;
            }
            catch (OperationCanceledException ex)
            {
                LoggingService.LogWarning("GeminiApiService.ChatWithFile", ex.Message);
                return TimeoutResponse;
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
                if (IsFailureResponse(result)) return (false, result);
                return (true, "Gemini connected: " + result.Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private const int MaxRetries = 3;
        private static readonly int[] RetryDelaysMs = { 1000, 2000, 4000 };

        private async Task<string> SendRequest(object body, CancellationToken ct, string? modelOverride = null)
        {
            var raw = await SendRawRequest(body, ct, modelOverride).ConfigureAwait(false);
            if (IsFailureResponse(raw))
                return raw;

            return ExtractText(raw);
        }

        private async Task<string> SendRawRequest(object body, CancellationToken ct, string? modelOverride = null)
        {
            var modelName = string.IsNullOrWhiteSpace(modelOverride) ? _model : modelOverride.Trim();
            var url = $"{BaseUrl}/{modelName}:generateContent?key={_apiKey}";
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
                    return responseText;

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
                var first = GetFirstCandidate(doc.RootElement);
                if (first == null)
                    return ParseFailureResponse;

                var parts = first.Value.GetProperty("content").GetProperty("parts");
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
                return ParseFailureResponse;
            }
        }

        private static JsonElement? GetFirstCandidate(JsonElement root)
        {
            if (!root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
                return null;

            return candidates[0];
        }

        private static IEnumerable<GeminiFunctionCall> ExtractFunctionCalls(JsonElement candidate)
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("functionCall", out var functionCall))
                    continue;

                yield return new GeminiFunctionCall
                {
                    Id = functionCall.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
                    Name = functionCall.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty,
                    ArgumentsJson = functionCall.TryGetProperty("args", out var argsElement) ? argsElement.GetRawText() : "{}"
                };
            }
        }

        public static bool IsFailureResponse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return true;

            return response.StartsWith("[Error:", StringComparison.OrdinalIgnoreCase)
                || response.StartsWith(ApiKeyNotSetResponse, StringComparison.OrdinalIgnoreCase)
                || response.StartsWith(ParseFailureResponse, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTimeoutResponse(string? response)
            => string.Equals(response?.Trim(), TimeoutResponse, StringComparison.OrdinalIgnoreCase);

        public static bool IsNetworkErrorResponse(string? response)
            => string.Equals(response?.Trim(), NetworkErrorResponse, StringComparison.OrdinalIgnoreCase);

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
                ".ogg" => "audio/ogg",
                ".oga" => "audio/ogg",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".m4a" => "audio/mp4",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}
