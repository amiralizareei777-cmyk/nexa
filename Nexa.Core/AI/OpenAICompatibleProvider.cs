using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Core.AI;

public class OpenAICompatibleProvider : IAIProvider
{
    protected readonly ISettingsService _settings;
    protected readonly ISecretStore _secretStore;
    protected readonly HttpClient _http;
    protected readonly ILogger? _logger;

    public virtual string Name => "OpenAI Compatible";
    protected virtual string DefaultBaseUrl => "";
    protected virtual bool UseDefaultBaseUrlOnly => false;

    public OpenAICompatibleProvider(
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
            if (string.IsNullOrEmpty(config.Url))
                return new ProviderTestResult { Success = false, Message = "No Base URL configured." };

            var payload = new { model = config.Model, messages = new[] { new { role = "user", content = "hi" } }, max_tokens = 5 };
            using var request = new HttpRequestMessage(HttpMethod.Post, config.Url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
            request.Content = JsonContent.Create(payload);
            using var response = await _http.SendAsync(request, cancellationToken);
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
            return new ProviderTestResult { Success = false, Message = "Request timed out. Check network and provider status." };
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

    public virtual async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var config = await BuildConfigAsync();
        if (string.IsNullOrEmpty(config.BaseUrl))
            throw new NotSupportedException("No Base URL configured.");

        var url = $"{config.BaseUrl.TrimEnd('/')}/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Authentication failed (401). Check the API key for the selected provider.");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                    models.Add(id.GetString() ?? "");
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
        if (string.IsNullOrEmpty(config.Url))
            throw new InvalidOperationException("AI provider is not configured. Set a Base URL in Settings.");

        var messages = new List<object>
        {
            new { role = "system", content = "You are Nexa, an AI assistant integrated into Windows. You help the user control their computer and answer questions concisely." }
        };

        // Add conversation history if provided
        if (conversationHistory != null)
        {
            foreach (var (role, msgContent) in conversationHistory)
            {
                messages.Add(new { role, content = msgContent });
            }
        }

        // With audio, the user message carries an input_audio part (the AI does the
        // speech recognition); the text part only hints at what to do.
        object userContent = audioWav != null
            ? (object)new object[]
            {
                new { type = "text", text = string.IsNullOrWhiteSpace(query)
                    ? "Transcribe the audio and respond to the user's spoken request, using tools when needed."
                    : query },
                new { type = "input_audio", input_audio = new { data = Convert.ToBase64String(audioWav), format = "wav" } }
            }
            : query;

        messages.Add(new { role = "user", content = userContent });

        var toolsJson = BuildToolsJson(availableTools);
        object payload;
        if (toolsJson != null)
        {
            payload = new { model = config.Model, messages, tools = toolsJson, tool_choice = "auto", temperature = 0.1 };
        }
        else
        {
            payload = new { model = config.Model, messages, temperature = 0.7 };
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.Url);
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
        httpRequest.Content = content;

        var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger?.LogError("AI provider returned {Status}", response.StatusCode);
            throw new InvalidOperationException(FormatApiError(response.StatusCode, errorBody));
        }

        return await ParseResponseAsync(response, cancellationToken);
    }

    protected async Task<(string Url, string Model, string ApiKey, string BaseUrl)> BuildConfigAsync()
    {
        var settings = _settings.Settings;
        var baseUrl = UseDefaultBaseUrlOnly
            ? DefaultBaseUrl
            : settings.AI.BaseUrl?.Trim().TrimEnd('/') ?? "";
        baseUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
        var url = string.IsNullOrEmpty(baseUrl) ? "" : $"{baseUrl}/chat/completions";
        var model = settings.AI.Model ?? "";
        var apiKey = await _secretStore.GetSecretAsync(ProviderKeyStore.KeyFor(settings.AI.Provider));
        return (url, model, apiKey ?? "", baseUrl);
    }

    protected virtual string FormatApiError(System.Net.HttpStatusCode status, string errorBody)
        => $"AI provider error ({status}). Check your Base URL and API key.";

    private async Task<AIResponse> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var aiResponse = new AIResponse();

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return aiResponse;

        var message = choices[0].GetProperty("message");

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            aiResponse.TextResponse = content.GetString();

        if (message.TryGetProperty("tool_calls", out var toolCalls))
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                if (!tc.TryGetProperty("function", out var function)) continue;
                var call = new AIToolCall
                {
                    ToolName = function.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                };
                if (function.TryGetProperty("arguments", out var argsProp))
                {
                    var argsText = argsProp.ValueKind == JsonValueKind.String ? argsProp.GetString() : argsProp.GetRawText();
                    if (!string.IsNullOrEmpty(argsText))
                    {
                        using var argsDoc = JsonDocument.Parse(argsText);
                        foreach (var p in argsDoc.RootElement.EnumerateObject())
                            call.Arguments[p.Name] = ToObject(p.Value)!;
                    }
                }
                if (!string.IsNullOrEmpty(call.ToolName))
                    aiResponse.ToolCalls.Add(call);
            }
        }

        return aiResponse;
    }

    protected static object? ToObject(JsonElement element) => element.ValueKind switch
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
                var prop = new Dictionary<string, object>
                {
                    ["type"] = p.Value.Type,
                    ["description"] = p.Value.Description
                };
                if (p.Value.Enum != null)
                    prop["enum"] = p.Value.Enum;
                props[p.Key] = prop;
                if (p.Value.Required) required.Add(p.Key);
            }
            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = new Dictionary<string, object> { ["type"] = "object", ["properties"] = props, ["required"] = required }
                }
            });
        }
        return list.Count > 0 ? list : null;
    }

    protected static string ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? "";
                return err.ToString();
            }
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }
}
