using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Tests;

public class AIProviderTests
{
    private static (ISettingsService settings, ISecretStore secrets) CreateMocks(
        string provider, string baseUrl, string model, bool hasKey = true)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            AI = new AISettings { Provider = provider, BaseUrl = baseUrl, Model = model }
        });

        var secrets = new Mock<ISecretStore>();
        secrets.Setup(s => s.GetSecretAsync(ProviderKeyStore.KeyFor(provider))).ReturnsAsync(hasKey ? "test-key" : null);
        secrets.Setup(s => s.SecretExistsAsync(ProviderKeyStore.KeyFor(provider))).ReturnsAsync(hasKey);
        return (settings.Object, secrets.Object);
    }

    private static OpenAICompatibleProvider CompatibleProvider(ISettingsService settings, ISecretStore secrets, HttpClient http)
        => new(settings, secrets, http, NullLogger<OpenAICompatibleProvider>.Instance);

    [Fact]
    public async Task OpenAICompatibleProvider_ParsesTextResponse()
    {
        var (settings, secrets) = CreateMocks("compatible", "http://localhost:20128/v1", "llama-3.1-8b-instruct");
        var provider = CompatibleProvider(settings, secrets,
            FakeHttpMessageHandler.Client("""{"choices":[{"message":{"content":"Yes, dependency injection is..."}}]}"""));

        var response = await provider.ProcessQueryAsync("explain DI", Array.Empty<AITool>());

        response.TextResponse.Should().Contain("dependency injection");
        response.HasToolCalls.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAICompatibleProvider_ParsesToolCalls()
    {
        var (settings, secrets) = CreateMocks("compatible", "http://localhost:20128/v1", "model-x");
        var provider = CompatibleProvider(settings, secrets,
            FakeHttpMessageHandler.Client("""{"choices":[{"message":{"content":null,"tool_calls":[{"function":{"name":"open_application","arguments":"{\"app_name\": \"Chrome\"}"}}]}}]}"""));

        var response = await provider.ProcessQueryAsync("open chrome", Array.Empty<AITool>());

        response.HasToolCalls.Should().BeTrue();
        response.ToolCalls[0].ToolName.Should().Be("open_application");
        response.ToolCalls[0].Arguments.Should().ContainKey("app_name");
    }

    [Fact]
    public async Task OpenAICompatibleProvider_AppendsBaseUrlAndChatCompletions()
    {
        var (settings, secrets) = CreateMocks("compatible", "http://localhost:20128/v1", "model-x");
        string? requestedUrl = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedUrl = req.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"}}]}""")
            };
        });
        var provider = new OpenAICompatibleProvider(settings, secrets, new HttpClient(handler), NullLogger<OpenAICompatibleProvider>.Instance);

        await provider.ProcessQueryAsync("hi", Array.Empty<AITool>());

        requestedUrl.Should().Be("http://localhost:20128/v1/chat/completions");
    }

    [Fact]
    public async Task OpenAICompatibleProvider_TestConnection_ReportsSuccess()
    {
        var (settings, secrets) = CreateMocks("compatible", "http://localhost:20128/v1", "model-x");
        var provider = CompatibleProvider(settings, secrets, FakeHttpMessageHandler.Client("", HttpStatusCode.OK));

        var result = await provider.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Connected");
    }

    [Fact]
    public async Task OpenAICompatibleProvider_TestConnection_Reports401()
    {
        var (settings, secrets) = CreateMocks("compatible", "http://localhost:20128/v1", "model-x");
        var provider = CompatibleProvider(settings, secrets, FakeHttpMessageHandler.Client("""{"error":{"message":"bad key"}}""", HttpStatusCode.Unauthorized));

        var result = await provider.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("API key");
    }

    [Fact]
    public async Task OpenAIProvider_ListModels_UsesOpenAIEndpointAfterSwitchingFromGemini()
    {
        var (settings, secrets) = CreateMocks("openai", "https://generativelanguage.googleapis.com/v1beta", "gpt-4o-mini");
        Uri? requestedUri = null;
        string? authorization = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString()
                ?? request.Headers.GetValues("Authorization").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"id":"gpt-4o-mini"}]}""")
            };
        });
        var provider = new OpenAIProvider(settings, secrets, new HttpClient(handler), NullLogger<OpenAIProvider>.Instance);

        var models = await provider.ListModelsAsync();

        requestedUri.Should().Be("https://api.openai.com/v1/models");
        authorization.Should().Be("Bearer test-key");
        models.Should().ContainSingle().Which.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task OpenAICompatibleProvider_TestConnection_SendsStoredKey()
    {
        var (settings, secrets) = CreateMocks("compatible", "http://localhost:20128/v1", "model-x");
        string? authorization = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString()
                ?? request.Headers.GetValues("Authorization").Single();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CompatibleProvider(settings, secrets, new HttpClient(handler));

        var result = await provider.TestConnectionAsync();

        result.Success.Should().BeTrue();
        authorization.Should().Be("Bearer test-key");
    }

    [Fact]
    public async Task OpenRouterProvider_DefaultsToOpenRouterBaseUrl()
    {
        var (settings, secrets) = CreateMocks("openrouter", "", "openai/gpt-4o-mini");
        string? requestedUrl = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedUrl = req.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"}}]}""")
            };
        });
        var provider = new OpenRouterProvider(settings, secrets, new HttpClient(handler), NullLogger<OpenRouterProvider>.Instance);

        await provider.ProcessQueryAsync("hi", Array.Empty<AITool>());

        requestedUrl.Should().StartWith("https://openrouter.ai/api/v1/");
    }

    [Fact]
    public async Task OpenRouterProvider_VoiceTranscribesBeforeSendingTextToMercury()
    {
        var (settings, secrets) = CreateMocks("openrouter", "https://openrouter.ai/api/v1", "inception/mercury-2.5");
        var requests = new List<(string Url, string? Authorization, string Body)>();
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            requests.Add((req.RequestUri!.ToString(), req.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(req.RequestUri.AbsolutePath.EndsWith("/audio/transcriptions")
                    ? """{"text":"open Chrome"}"""
                    : """{"choices":[{"message":{"content":"Done"}}]}""")
            };
        });
        var provider = new OpenRouterProvider(settings, secrets, new HttpClient(handler), NullLogger<OpenRouterProvider>.Instance);

        var result = await provider.ProcessQueryAsync("", Array.Empty<AITool>(), audioWav: [1, 2, 3]);

        result.TextResponse.Should().Be("Done");
        requests.Select(r => r.Url).Should().Equal(
            "https://openrouter.ai/api/v1/audio/transcriptions",
            "https://openrouter.ai/api/v1/chat/completions");
        requests.Select(r => r.Authorization).Should().OnlyContain(a => a == "Bearer test-key");
        using var transcription = JsonDocument.Parse(requests[0].Body);
        transcription.RootElement.GetProperty("model").GetString().Should().Be("openai/whisper-1");
        transcription.RootElement.GetProperty("input_audio").GetProperty("format").GetString().Should().Be("wav");
        using var chat = JsonDocument.Parse(requests[1].Body);
        chat.RootElement.GetProperty("model").GetString().Should().Be("inception/mercury-2.5");
        chat.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Be("open Chrome");
    }

    [Fact]
    public async Task OpenRouterProvider_VoiceCreditFailureIsActionableAndStopsBeforeChat()
    {
        var (settings, secrets) = CreateMocks("openrouter", "https://openrouter.ai/api/v1", "inception/mercury-2.5");
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.PaymentRequired)
            {
                Content = new StringContent("""{"error":{"message":"insufficient credits"}}""")
            };
        });
        var provider = new OpenRouterProvider(settings, secrets, new HttpClient(handler), NullLogger<OpenRouterProvider>.Instance);

        var act = () => provider.ProcessQueryAsync("", Array.Empty<AITool>(), audioWav: [1, 2, 3]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*insufficient credits (402)*");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task OmniRouteProvider_DefaultsToLocalhostBaseUrl()
    {
        var (settings, secrets) = CreateMocks("omniroute", "", "llama-3.1-8b-instruct");
        string? requestedUrl = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedUrl = req.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"}}]}""")
            };
        });
        var provider = new OmniRouteProvider(settings, secrets, new HttpClient(handler), NullLogger<OmniRouteProvider>.Instance);

        await provider.ProcessQueryAsync("hi", Array.Empty<AITool>());

        requestedUrl.Should().Be("http://localhost:20128/v1/chat/completions");
    }

    [Fact]
    public async Task OpenRouterProvider_ListModels_ParsesIds()
    {
        var (settings, secrets) = CreateMocks("openrouter", "https://openrouter.ai/api/v1", "x");
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/models"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[{"id":"openai/gpt-4o"},{"id":"anthropic/claude-3.5-sonnet"}]}""")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var provider = new OpenRouterProvider(settings, secrets, new HttpClient(handler), NullLogger<OpenRouterProvider>.Instance);

        var models = await provider.ListModelsAsync();

        models.Should().Contain("openai/gpt-4o");
        models.Should().Contain("anthropic/claude-3.5-sonnet");
    }

    [Fact]
    public async Task AnthropicProvider_ParsesTextAndToolUse()
    {
        var (settings, secrets) = CreateMocks("anthropic", "", "claude-sonnet-5");
        var json = """
            {"content":[
              {"type":"text","text":"I will open it."},
              {"type":"tool_use","id":"t1","name":"open_application","input":{"app_name":"Chrome"}}
            ]}
            """;
        var provider = new AnthropicProvider(settings, secrets, FakeHttpMessageHandler.Client(json), NullLogger<AnthropicProvider>.Instance);

        var response = await provider.ProcessQueryAsync("open chrome", Array.Empty<AITool>());

        response.TextResponse.Should().Be("I will open it.");
        response.HasToolCalls.Should().BeTrue();
        response.ToolCalls[0].ToolName.Should().Be("open_application");
    }

    [Fact]
    public async Task AnthropicProvider_UsesDefaultBaseUrlWhenEmpty()
    {
        var (settings, secrets) = CreateMocks("anthropic", "", "claude-sonnet-5");
        string? requestedUrl = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedUrl = req.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"content":[{"type":"text","text":"ok"}]}""")
            };
        });
        var provider = new AnthropicProvider(settings, secrets, new HttpClient(handler), NullLogger<AnthropicProvider>.Instance);

        await provider.ProcessQueryAsync("hi", Array.Empty<AITool>());

        requestedUrl.Should().Be("https://api.anthropic.com/v1/messages");
    }

    [Fact]
    public async Task GeminiProvider_ParsesFunctionCall()
    {
        var (settings, secrets) = CreateMocks("gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash");
        var json = """
            {"candidates":[{"content":{"parts":[
              {"text":"Opening it."},
              {"functionCall":{"name":"set_volume","args":{"level":30}}}
            ]}}]}
            """;
        var provider = new GeminiProvider(settings, secrets, FakeHttpMessageHandler.Client(json), NullLogger<GeminiProvider>.Instance);

        var response = await provider.ProcessQueryAsync("set volume 30", Array.Empty<AITool>());

        response.TextResponse.Should().Be("Opening it.");
        response.HasToolCalls.Should().BeTrue();
        response.ToolCalls[0].ToolName.Should().Be("set_volume");
        response.ToolCalls[0].Arguments.Should().ContainKey("level");
    }

    [Fact]
    public async Task GeminiProvider_ListModels_StripsPrefix()
    {
        var (settings, secrets) = CreateMocks("gemini", "https://generativelanguage.googleapis.com/v1beta", "x");
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/models"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"models":[{"name":"models/gemini-2.0-flash"},{"name":"models/gemini-1.5-pro"}]}""")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var provider = new GeminiProvider(settings, secrets, new HttpClient(handler), NullLogger<GeminiProvider>.Instance);

        var models = await provider.ListModelsAsync();

        models.Should().Contain("gemini-2.0-flash");
        models.Should().NotContain(m => m.StartsWith("models/"));
    }
}
