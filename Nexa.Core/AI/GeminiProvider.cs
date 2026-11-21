using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Core.AI;

public class GeminiProvider : IAIProvider
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secretStore;
    private readonly HttpClient _http;
    private readonly ILogger? _logger;

    public string Name => "Google Gemini";

    public GeminiProvider(
        ISettingsService settings,
        ISecretStore secretStore,
        HttpClient http,
        ILogger? logger)
    {
        _settings = settings;
        _secretStore = secretStore;
        _http = http;
        _logger = logger;
    }

    public virtual async Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await BuildConfigAsync();
            if (string.IsNullOrEmpty(config.ApiKey))
                return new ProviderTestResult { Success = false, Message = "No API key configured." };

            var url = $"{config.BaseUrl}/models?key={config.ApiKey}";
            var response = await _http.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return new ProviderTestResult { Success = true, Message = "Connected successfully." };

            return new ProviderTestResult
            {
                Success = false,
                Message = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Authentication failed. Check your API key.",
                    System.Net.HttpStatusCode.NotFound => "Endpoint not found. Check the Base URL.",
                    System.Net.HttpStatusCode.TooManyRequests => "Rate limited. Try again later.",
                    _ => $"Error ({(int)response.StatusCode}). {ExtractErrorMessage(body)}"
                }
            };
        }
        catch (TaskCanceledException)
        {
            return new ProviderTestResult { Success = false, Message = "Request timed out." };
        }
        catch (HttpRequestException ex)
        {
            return new ProviderTestResult { Success = false, Message = $"Network error: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ProviderTestResult { Success = false, Message = $"Error: {ex.Message}" };
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var config = await BuildConfigAsync();
        var url = $"{config.BaseUrl}/models?key={config.ApiKey}";
        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name))
                {
                    var modelName = name.GetString() ?? "";
                    if (modelName.StartsWith("models/"))
                        modelName = modelName["models/".Length..];
                    models.Add(modelName);
                }
            }
        }

        return models;
    }

    public virtual async Task<AIResponse> ProcessQueryAsync(
        string query,
        IEnumerable<AITool> availableTools,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        byte[]? audioWav = null)
    {
        var config = await BuildConfigAsync();
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var model = string.IsNullOrEmpty(config.Model) ? "gemini-2.0-flash" : config.Model;
        var url = $"{config.BaseUrl}/models/{model}:generateContent?key={config.ApiKey}";

        var contents = new List<object>();

        // Add conversation history if provided
        if (conversationHistory != null)
        {
            foreach (var (role, msgContent) in conversationHistory)
            {
                // Map roles: "assistant" -> "model" for Gemini
                var geminiRole = role == "assistant" ? "model" : "user";
                contents.Add(new { role = geminiRole, parts = new[] { new { text = msgContent } } });
            }
        }

        // With audio, the user content gains an inline_data part (the AI does the
        // speech recognition); a text part is included when a hint is provided.
        var userParts = new List<object>();
        if (audioWav != null)
        {
            userParts.Add(new { inline_data = new { mime_type = "audio/wav", data = Convert.ToBase64String(audioWav) } });
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            userParts.Add(new { text = query });
        }
        if (userParts.Count == 0)
        {
            userParts.Add(new { text = "Respond to the user's spoken request, using tools when needed." });
        }
        contents.Add(new { role = "user", parts = userParts.ToArray() });

        var payload = new Dictionary<string, object>
        {
            ["contents"] = contents,
            ["systemInstruction"] = new { parts = new[] { new { text = "You are Nexa, an AI assistant integrated into Windows. You help the user control their computer and answer questions concisely." } } }
        };

        var geminiTools = BuildToolsJson(availableTools);
        if (geminiTools != null)
            payload["tools"] = geminiTools;

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger?.LogError("Gemini returned {Status}: {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"Gemini error ({response.StatusCode}). {ExtractErrorMessage(errorBody)}");
        }

        return await ParseResponseAsync(response, cancellationToken);
    }

    private async Task<AIResponse> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var aiResponse = new AIResponse();

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return aiResponse;

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content))
            return aiResponse;

        if (!content.TryGetProperty("parts", out var parts))
            return aiResponse;

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProp))
            {
                aiResponse.TextResponse = textProp.GetString();
            }

            if (part.TryGetProperty("functionCall", out var fc))
            {
                var call = new AIToolCall
                {
                    ToolName = fc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                };
                if (fc.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in args.EnumerateObject())
                        call.Arguments[p.Name] = ToObject(p.Value)!;
                }
                if (!string.IsNullOrEmpty(call.ToolName))
                    aiResponse.ToolCalls.Add(call);
            }
        }

        return aiResponse;
    }

    private static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => element.ToString()
    };

    private object? BuildToolsJson(IEnumerable<AITool> tools)
    {
        var list = new List<object>();
        foreach (var tool in tools)
        {
            var props = new Dictionary<string, object>();
            var required = new List<string>();
            foreach (var p in tool.Parameters)
            {
                props[p.Key] = new { type = p.Value.Type, description = p.Value.Description };
                if (p.Value.Required) required.Add(p.Key);
            }

            var declarations = new List<object>
            {
                new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = props,
                        ["required"] = required
                    }
                }
            };

            list.Add(new Dictionary<string, object>
            {
                ["functionDeclarations"] = declarations
            });
        }
        return list.Count > 0 ? list : null;
    }

    private async Task<(string BaseUrl, string Model, string ApiKey)> BuildConfigAsync()
    {
        var settings = _settings.Settings;
        var baseUrl = settings.AI.BaseUrl?.Trim().TrimEnd('/') ?? "https://generativelanguage.googleapis.com/v1beta";
        var model = settings.AI.Model ?? "";
        var apiKey = await _secretStore.GetSecretAsync(ProviderKeyStore.KeyFor(settings.AI.Provider));
        return (baseUrl, model, apiKey ?? "");
    }

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? "";
            }
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }
}
