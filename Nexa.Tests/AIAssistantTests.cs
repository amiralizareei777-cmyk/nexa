using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using Nexa.Core.Parsing;

namespace Nexa.Tests;

/// <summary>
/// Tests the AIAssistant orchestration: local-first routing, AI fallback,
/// tool-call execution, and the multi-step tool-call limit.
/// </summary>
public class AIAssistantTests
{
    private readonly Mock<IIntentParser> _parser = new();
    private readonly ActionRouter _router;
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<ISecretStore> _secrets = new();
    private readonly Mock<IAction> _openAppAction = new();

    public AIAssistantTests()
    {
        _router = new ActionRouter(_parser.Object);
        _openAppAction.Setup(a => a.Name).Returns("open_application");
        _openAppAction.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.Safe);
        _openAppAction.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        _openAppAction.Setup(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Successful("Opened Google Chrome"));
        _router.RegisterAction(_openAppAction.Object);
    }

    private AIAssistant BuildAssistant(string jsonBody, bool configured = true, bool useAi = true)
    {
        var settings = new AppSettings
        {
            AI = new AISettings
            {
                Provider = "compatible",
                BaseUrl = configured ? "http://localhost:20128/v1" : "",
                Model = "model-x",
                UseAIForComplexQueries = useAi
            }
        };
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync(configured ? "test-key" : null);
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(configured);

        var http = FakeHttpMessageHandler.Client(jsonBody);
        var factory = new AIProviderFactory(_settings.Object, _secrets.Object, NullLoggerFactory.Instance, http);
        return new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_LocalCommand_UsesAiWhenConfigured()
    {
        _parser.Setup(p => p.TryParseLocal("Open Chrome"))
            .Returns(new Intent { ActionName = "open_application", Parameters = new Dictionary<string, object> { ["app_name"] = "Chrome" } });

        var assistant = BuildAssistant("""{"choices":[{"message":{"content":"AI handled Chrome."}}]}""");

        var (result, answer) = await assistant.ProcessAsync("Open Chrome");

        result.Success.Should().BeTrue();
        answer.Should().Be("AI handled Chrome.");
        _parser.Verify(p => p.TryParseLocal(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AskAi_PrefixForcesTextAnswer()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);
        var assistant = BuildAssistant("""{"choices":[{"message":{"content":"Dependency injection means..."}}]}""");

        var (_, answer) = await assistant.ProcessAsync("ask ai: what is dependency injection?");

        answer.Should().Contain("Dependency injection");
    }

    [Fact]
    public async Task ProcessAsync_AiToolCall_IsExecutedThroughRouter()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);

        // First request proposes a tool call; second request is the model replying after execution.
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            var body = callCount == 1
                ? """{"choices":[{"message":{"content":null,"tool_calls":[{"function":{"name":"open_application","arguments":"{\"app_name\":\"Chrome\"}"}}]}}]}"""
                : """{"choices":[{"message":{"content":"Done."}}]}""";
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var settings = new AppSettings { AI = new AISettings { Provider = "compatible", BaseUrl = "http://localhost:20128/v1", Model = "m" } };
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("key");
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var factory = new AIProviderFactory(_settings.Object, _secrets.Object, NullLoggerFactory.Instance, new HttpClient(handler));
        var assistant = new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);

        var (result, _) = await assistant.ProcessAsync("open chrome");

        result.Success.Should().BeTrue();
        _openAppAction.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UnknownAiTool_IsRejectedAndRecovers()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);

        // First response suggests an unknown tool; second is a plain text answer.
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            var body = callCount == 1
                ? """{"choices":[{"message":{"content":null,"tool_calls":[{"function":{"name":"unknown_tool","arguments":"{}"}}]}}]}"""
                : """{"choices":[{"message":{"content":"I can't do that, but I can help otherwise."}}]}""";
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var settings = new AppSettings { AI = new AISettings { Provider = "compatible", BaseUrl = "http://localhost:20128/v1", Model = "m" } };
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("key");
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var factory = new AIProviderFactory(_settings.Object, _secrets.Object, NullLoggerFactory.Instance, new HttpClient(handler));
        var assistant = new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);

        var (_, answer) = await assistant.ProcessAsync("do something weird");

        answer.Should().Contain("can't do that");
        _openAppAction.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ToolLoop_LimitedToMaxCalls()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);

        // Provider always suggests a tool so the loop must terminate at the limit.
        var assistant = BuildAssistant("""{"choices":[{"message":{"content":null,"tool_calls":[{"function":{"name":"open_application","arguments":"{\"app_name\":\"Chrome\"}"}}]}}]}""");

        var (result, _) = await assistant.ProcessAsync("open chrome");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("limited");
        _openAppAction.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.AtMost(AIAssistant.MaxToolCallsPerRequest));
    }

    [Fact]
    public async Task ProcessAsync_ProviderNotConfigured_ReturnsGuidance()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);
        var assistant = BuildAssistant("""{"choices":[{"message":{"content":"x"}}]}""", configured: false);

        var (result, _) = await assistant.ProcessAsync("some complex question");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("configure an AI provider");
    }

    [Fact]
    public async Task ProcessAsync_ProviderNotConfigured_UsesLocalActionFallback()
    {
        _parser.Setup(p => p.TryParseLocal("Open Chrome"))
            .Returns(new Intent
            {
                ActionName = "open_application",
                Parameters = new Dictionary<string, object> { ["app_name"] = "Chrome" }
            });
        var assistant = BuildAssistant("""{"choices":[{"message":{"content":"unused"}}]}""",
            configured: false);

        var (result, answer) = await assistant.ProcessAsync("Open Chrome");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Opened Google Chrome");
        answer.Should().BeNull();
        _openAppAction.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BuildToolDefinitions_RespectsPrivacyToggles()
    {
        var settings = new AppSettings
        {
            Privacy = new PrivacySettings
            {
                AllowScreenshotsToAI = false,
                AllowFileNamesToAI = false
            }
        };
        _settings.SetupGet(s => s.Settings).Returns(settings);
        var assistant = new AIAssistant(_router, null!, _settings.Object, _secrets.Object,
            NullLogger<AIAssistant>.Instance);

        var names = assistant.BuildToolDefinitions().Select(tool => tool.Name).ToList();

        names.Should().NotContain("take_screenshot");
        names.Should().NotContain("search_files");
        names.Should().Contain("open_application");
    }

    [Fact]
    public async Task ProcessAsync_ProviderError_FailsGracefully()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);
        var settings = new AppSettings { AI = new AISettings { Provider = "compatible", BaseUrl = "http://localhost:20128/v1", Model = "m" } };
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("key");

        var http = FakeHttpMessageHandler.Client("""{"error":{"message":"boom"}}""", System.Net.HttpStatusCode.BadGateway);
        var factory = new AIProviderFactory(_settings.Object, _secrets.Object, NullLoggerFactory.Instance, http);
        var assistant = new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);

        var (result, _) = await assistant.ProcessAsync("explain the universe");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("couldn't reach");
    }

    [Fact]
    public async Task ProcessAsync_GeminiQuotaError_ExplainsRateLimit()
    {
        _settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            AI = new AISettings
            {
                Provider = "gemini",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash"
            }
        });
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("key");
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var http = FakeHttpMessageHandler.Client(
            """{"error":{"message":"Quota exceeded for this model."}}""",
            System.Net.HttpStatusCode.TooManyRequests);
        var factory = new AIProviderFactory(_settings.Object, _secrets.Object,
            NullLoggerFactory.Instance, http);
        var assistant = new AIAssistant(_router, factory, _settings.Object, _secrets.Object,
            NullLogger<AIAssistant>.Instance);

        var (result, _) = await assistant.ProcessAsync("hello");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("request limit");
        result.Message.Should().NotContain("couldn't reach");
    }
}
