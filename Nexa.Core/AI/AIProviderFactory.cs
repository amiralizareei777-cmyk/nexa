using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Nexa.Core.AI;

/// <summary>
/// Creates AI provider instances based on configuration.
/// </summary>
public class AIProviderFactory
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secretStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HttpClient _httpClient;

    public AIProviderFactory(
        ISettingsService settings,
        ISecretStore secretStore,
        ILoggerFactory loggerFactory,
        HttpClient? httpClient = null)
    {
        _settings = settings;
        _secretStore = secretStore;
        _loggerFactory = loggerFactory;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public virtual IAIProvider CreateProvider() => CreateProvider(_settings.Settings.AI.Provider);

    public IAIProvider CreateProvider(string providerName)
    {
        return (providerName ?? "").ToLowerInvariant() switch
        {
            "openai" => new OpenAIProvider(_settings, _secretStore, _httpClient, _loggerFactory.CreateLogger<OpenAIProvider>()),
            "anthropic" => new AnthropicProvider(_settings, _secretStore, _httpClient, _loggerFactory.CreateLogger<AnthropicProvider>()),
            "gemini" => new GeminiProvider(_settings, _secretStore, _httpClient, _loggerFactory.CreateLogger<GeminiProvider>()),
            "openrouter" => new OpenRouterProvider(_settings, _secretStore, _httpClient, _loggerFactory.CreateLogger<OpenRouterProvider>()),
            "omniroute" => new OmniRouteProvider(_settings, _secretStore, _httpClient, _loggerFactory.CreateLogger<OmniRouteProvider>()),
            _ => new OpenAICompatibleProvider(_settings, _secretStore, _httpClient, _loggerFactory.CreateLogger<OpenAICompatibleProvider>())
        };
    }

    /// <summary>
    /// Per-provider default base URLs for the settings UI.
    /// </summary>
    public static string DefaultBaseUrl(string providerName) => providerName.ToLowerInvariant() switch
    {
        "openai" => "https://api.openai.com/v1",
        "anthropic" => "https://api.anthropic.com",
        "gemini" => "https://generativelanguage.googleapis.com/v1beta",
        "openrouter" => "https://openrouter.ai/api/v1",
        "omniroute" => "http://localhost:20128/v1",
        _ => ""
    };

    /// <summary>
    /// Provider keys used in the settings UI ComboBox, in order.
    /// </summary>
    public static string[] ProviderKeys => new[]
    {
        "openai", "anthropic", "gemini", "openrouter", "compatible", "omniroute"
    };
}