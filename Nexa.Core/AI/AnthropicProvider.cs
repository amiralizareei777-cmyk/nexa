using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Core.AI;

public class AnthropicProvider : IAIProvider
{
    private const string ApiVersion = "2023-06-01";
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secretStore;
    private readonly HttpClient _http;
    private readonly ILogger? _logger;

    public string Name => "Anthropic";

    public AnthropicProvider(
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

            var request = new { model = config.Model, max_tokens = 10, messages = new[] { new { role = "user", content = "hi" } } };
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.Url);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
            httpRequest.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
            httpRequest.Content = content;

            var response = await _http.SendAsync(httpRequest, cancellationToken);
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

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(
            new[] { "claude-sonnet-5", "claude-opus-5", "claude-haiku-4-5-20251001" });
    }

    public virtual async Task<AIResponse> ProcessQueryAsync(
        string query,
        IEnumerable<AITool> availableTools,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        byte[]? audioWav = null)
    {
        // The Messages API accepts text, image and document inputs, but no
        // audio input block. Never send a fabricated block that the API rejects.
        if (audioWav != null)
            throw new NotSupportedException(
                "Anthropic Messages API does not support audio input. Choose a model with audio support for voice sessions.");

        var config = await BuildConfigAsync();
        if (string.IsNullOrEmpty(config.Url))
            throw new InvalidOperationException("Anthropic is not configured. Set a Base URL in Settings.");

        var messages = new List<object>();

        // Add conversation history if provided
        if (conversationHistory != null)
        {
            foreach (var (role, msgContent) in conversationHistory)
            {
                messages.Add(new { role, content = msgContent });
            }
        }

        messages.Add(new { role = "user", content = query });

        var payload = new Dictionary<string, object>
        {
            ["model"] = config.Model,
            ["max_tokens"] = 1024,
            ["system"] = "You are Nexa, an AI assistant integrated into Windows. You help the user control their computer and answer questions concisely.",
            ["messages"] = messages.ToArray()
        };

        var toolsJson = BuildToolsJson(availableTools);
        if (toolsJson != null)
        {
            payload["tools"] = toolsJson;
            payload["tool_choice"] = new { type = "auto" };
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.Url);
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        httpRequest.Content = content;

        var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger?.LogError("Anthropic returned {Status}: {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"Anthropic error ({response.StatusCode}). {ExtractErrorMessage(errorBody)}");
        }

        return await ParseResponseAsync(response, cancellationToken);
    }

    private async Task<AIResponse> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var aiResponse = new AIResponse();

        if (!root.TryGetProperty("content", out var content) || content.GetArrayLength() == 0)
            return aiResponse;

        foreach (var block in content.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "";
            switch (type)
            {
                case "text":
                    if (block.TryGetProperty("text", out var textProp))
                        aiResponse.TextResponse = textProp.GetString();
                    break;
                case "tool_use":
                    var call = new AIToolCall
                    {
                        ToolName = block.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                    };
                    if (block.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in input.EnumerateObject())
                            call.Arguments[p.Name] = ToObject(p.Value)!;
                    }
                    if (!string.IsNullOrEmpty(call.ToolName))
                        aiResponse.ToolCalls.Add(call);
                    break;
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
                var prop = new Dictionary<string, object> { ["type"] = p.Value.Type, ["description"] = p.Value.Description };
                if (p.Value.Enum != null) prop["enum"] = p.Value.Enum;
                props[p.Key] = prop;
                if (p.Value.Required) required.Add(p.Key);
            }
            list.Add(new { name = tool.Name, description = tool.Description, input_schema = new Dictionary<string, object> { ["type"] = "object", ["properties"] = props, ["required"] = required } });
        }
        return list.Count > 0 ? list : null;
    }

    private async Task<(string Url, string Model, string ApiKey)> BuildConfigAsync()
    {
        var settings = _settings.Settings;
        var baseUrl = settings.AI.BaseUrl?.Trim().TrimEnd('/') ?? "";
        baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://api.anthropic.com" : baseUrl;
        var url = $"{baseUrl}/v1/messages";
        var model = string.IsNullOrEmpty(settings.AI.Model) ? "claude-sonnet-5" : settings.AI.Model;
        var apiKey = await _secretStore.GetSecretAsync(ProviderKeyStore.KeyFor(settings.AI.Provider));
        return (url, model, apiKey ?? "");
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
