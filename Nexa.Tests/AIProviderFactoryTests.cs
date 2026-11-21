using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Tests;

public class AIProviderFactoryTests
{
    private static AIProviderFactory CreateFactory(string providerName)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings { AI = new AISettings { Provider = providerName } });
        var secrets = new Mock<ISecretStore>();
        var loggerFactory = NullLoggerFactory.Instance;
        return new AIProviderFactory(settings.Object, secrets.Object, loggerFactory);
    }

    [Theory]
    [InlineData("openai", typeof(OpenAIProvider))]
    [InlineData("anthropic", typeof(AnthropicProvider))]
    [InlineData("gemini", typeof(GeminiProvider))]
    [InlineData("openrouter", typeof(OpenRouterProvider))]
    [InlineData("compatible", typeof(OpenAICompatibleProvider))]
    [InlineData("omniroute", typeof(OmniRouteProvider))]
    public void CreateProvider_CreatesCorrectProviderForName(string name, Type expectedType)
    {
        var factory = CreateFactory(name);
        factory.CreateProvider().Should().BeOfType(expectedType);
    }

    [Fact]
    public void CreateProvider_FallsBackToCompatible_ForUnknownName()
    {
        var factory = CreateFactory("unknown_provider");
        factory.CreateProvider().Should().BeAssignableTo<OpenAICompatibleProvider>();
    }

    [Fact]
    public void CreateProvider_ReadingCurrentSettingsKey()
    {
        var factory = CreateFactory("gemini");
        factory.CreateProvider().Name.Should().Be("Google Gemini");
    }

    [Theory]
    [InlineData("omniroute", "http://localhost:20128/v1")]
    [InlineData("openrouter", "https://openrouter.ai/api/v1")]
    [InlineData("gemini", "https://generativelanguage.googleapis.com/v1beta")]
    public void DefaultBaseUrl_ReturnsExpectedDefaults(string name, string expected)
    {
        AIProviderFactory.DefaultBaseUrl(name).Should().Be(expected);
    }

    [Fact]
    public void ProviderKeys_ContainsAllSixProviders()
    {
        AIProviderFactory.ProviderKeys.Should().BeEquivalentTo(
            new[] { "openai", "anthropic", "gemini", "openrouter", "compatible", "omniroute" });
    }
}