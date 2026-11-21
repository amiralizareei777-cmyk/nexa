using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using Nexa.Core.Parsing;
using Xunit;

namespace Nexa.Tests;

public class AgenticFlowTests
{
    private static (ISettingsService Settings, ISecretStore Secrets) Config(string provider)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            AI = new AISettings
            {
                Provider = provider,
                BaseUrl = "http://localhost:20128/v1",
                Model = "audio-model"
            }
        });
        var secrets = new Mock<ISecretStore>();
        secrets.Setup(s => s.GetSecretAsync(ProviderKeyStore.KeyFor(provider))).ReturnsAsync("test-key");
        secrets.Setup(s => s.SecretExistsAsync(ProviderKeyStore.KeyFor(provider))).ReturnsAsync(true);
        return (settings.Object, secrets.Object);
    }

    [Theory]
    [InlineData("compatible")]
    [InlineData("gemini")]
    public async Task AudioRequest_EmbedsWavForSupportedProvider(string providerName)
    {
        var wav = new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4 };
        var (settings, secrets) = Config(providerName);
        var handler = new RecordingHandler(providerName == "gemini"
            ? """{"candidates":[{"content":{"parts":[{"text":"heard"}]}}]}"""
            : """{"choices":[{"message":{"content":"heard"}}]}""");
        IAIProvider provider = providerName == "gemini"
            ? new GeminiProvider(settings, secrets, new HttpClient(handler), null)
            : new OpenAICompatibleProvider(settings, secrets, new HttpClient(handler), null);

        await provider.ProcessQueryAsync("Transcribe", Array.Empty<AITool>(), audioWav: wav);

        using var doc = JsonDocument.Parse(handler.Body!);
        JsonElement audioPart;
        string encoded;
        if (providerName == "gemini")
        {
            audioPart = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("inline_data");
            audioPart.GetProperty("mime_type").GetString().Should().Be("audio/wav");
            encoded = audioPart.GetProperty("data").GetString()!;
        }
        else
        {
            audioPart = doc.RootElement.GetProperty("messages")[1].GetProperty("content")[1]
                .GetProperty("input_audio");
            audioPart.GetProperty("format").GetString().Should().Be("wav");
            encoded = audioPart.GetProperty("data").GetString()!;
        }
        Convert.FromBase64String(encoded).Should().Equal(wav);
    }

    [Fact]
    public async Task Anthropic_AudioRequest_ReportsUnsupportedWithoutSending()
    {
        var (settings, secrets) = Config("anthropic");
        var handler = new RecordingHandler("""{"content":[]}""");
        var provider = new AnthropicProvider(settings, secrets, new HttpClient(handler), null);

        var act = () => provider.ProcessQueryAsync("Transcribe", Array.Empty<AITool>(),
            audioWav: new byte[] { 1, 2 });

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not support audio input*");
        handler.Body.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardedTool_ExecutesOnlyAfterApproval(bool approve)
    {
        var (settings, secrets) = Config("compatible");
        var parser = new Mock<IIntentParser>();
        var router = new ActionRouter(parser.Object);
        var shell = new Mock<IAction>();
        shell.Setup(a => a.Name).Returns("run_command");
        shell.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.ConfirmationRequired);
        shell.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        shell.Setup(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Successful("Command completed."));
        router.RegisterAction(shell.Object);

        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            calls++;
            var body = calls == 1
                ? """{"choices":[{"message":{"content":null,"tool_calls":[{"function":{"name":"run_command","arguments":"{\"command\":\"echo hello\"}"}}]}}]}"""
                : """{"choices":[{"message":{"content":"Finished."}}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });
        var factory = new AIProviderFactory(settings, secrets, NullLoggerFactory.Instance,
            new HttpClient(handler));
        var assistant = new AIAssistant(router, factory, settings, secrets,
            NullLogger<AIAssistant>.Instance);
        string? shownCommand = null;

        var (_, answer) = await assistant.ProcessAsync("Run this", confirmToolCall: (name, args) =>
        {
            name.Should().Be("run_command");
            shownCommand = args["command"].ToString();
            return Task.FromResult(approve);
        });

        shownCommand.Should().Be("echo hello");
        answer.Should().Be("Finished.");
        shell.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), approve ? Times.Once : Times.Never);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string? Body { get; private set; }
        public RecordingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? null :
                await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
