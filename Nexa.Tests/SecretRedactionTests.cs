using System.Net;
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
/// Verifies that API keys and other secrets never leak into diagnostics or
/// error messages surfaced to the user.
/// </summary>
public class SecretRedactionTests
{
    private const string ApiKey = "sk-secret-value-12345";

    private static (ISettingsService settings, ISecretStore secrets) CreateMocks()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            AI = new AISettings { Provider = "compatible", BaseUrl = "http://localhost:20128/v1", Model = "m" }
        });
        var secrets = new Mock<ISecretStore>();
        secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync(ApiKey);
        return (settings.Object, secrets.Object);
    }

    [Fact]
    public async Task TestConnection_ErrorMessage_DoesNotContainApiKey()
    {
        var (settings, secrets) = CreateMocks();
        // Server rejects auth; its body contains the request's key echo if leaked.
        var http = FakeHttpMessageHandler.Client("""{"error":{"message":"invalid credentials"}}""", HttpStatusCode.Unauthorized);
        var provider = new OpenAICompatibleProvider(settings, secrets, http, NullLogger<OpenAICompatibleProvider>.Instance);

        var result = await provider.TestConnectionAsync();

        result.Message.Should().NotContain(ApiKey);
        result.Message.Should().Contain("API key").And.NotContain("sk-secret");
    }

    [Fact]
    public async Task ProcessQuery_ThrownError_MessageDoesNotContainApiKey()
    {
        var (settings, secrets) = CreateMocks();
        var http = FakeHttpMessageHandler.Client("""{"error":{"message":"bad"}}""", HttpStatusCode.InternalServerError);
        var provider = new OpenAICompatibleProvider(settings, secrets, http, NullLogger<OpenAICompatibleProvider>.Instance);

        var act = async () => await provider.ProcessQueryAsync("hi", Array.Empty<AITool>());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => !ex.Message.Contains(ApiKey) && !ex.Message.Contains("sk-secret"));
    }

    [Fact]
    public async Task AssistantFailure_DoesNotLeakApiKey()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            AI = new AISettings { Provider = "compatible", BaseUrl = "http://localhost:20128/v1", Model = "m" }
        });
        var secrets = new Mock<ISecretStore>();
        secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync(ApiKey);

        var http = FakeHttpMessageHandler.Client("""{"error":"x"}""", HttpStatusCode.BadGateway);
        var factory = new AIProviderFactory(settings.Object, secrets.Object, NullLoggerFactory.Instance, http);
        var router = new ActionRouter(new Mock<IIntentParser>().Object);
        var assistant = new AIAssistant(router, factory, settings.Object, secrets.Object, NullLogger<AIAssistant>.Instance);

        var (result, _) = await assistant.ProcessAsync("some question");

        result.Message.Should().NotContain(ApiKey);
        result.Message.Should().NotContain("sk-secret");
    }
}
