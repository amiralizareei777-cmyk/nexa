using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using Nexa.Core.Parsing;
using Nexa.Windows.Actions;
using System.Text;
using Xunit;

namespace Nexa.Tests;

public class FileWorkflowTests
{
    [Fact]
    public async Task WriteFile_CreatesHtmlAndKeepsItsPath_WithoutOverwriting()
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "file-workflow-test"));
        var path = Path.Combine(folder, $"site-{Guid.NewGuid():N}.html");
        var recent = new RecentArtifactStore();
        var action = new WriteFileAction(recent);
        var parameters = new Dictionary<string, object>
        {
            ["path"] = path,
            ["content"] = "<html><body>Hello Nexa</body></html>"
        };
        try
        {
            var first = await action.ExecuteAsync(parameters);
            first.Success.Should().BeTrue();
            first.Data!["path"].Should().Be(path);
            recent.LastCreatedPath.Should().Be(path);
            (await File.ReadAllTextAsync(path, Encoding.UTF8)).Should().Contain("Hello Nexa");

            parameters["content"] = "replacement";
            var second = await action.ExecuteAsync(parameters);
            second.Success.Should().BeFalse();
            (await File.ReadAllTextAsync(path, Encoding.UTF8)).Should().Contain("Hello Nexa");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    [Fact]
    public async Task SpokenToolFollowup_RetainsAudioAndCreatedFilePath()
    {
        var parser = new Mock<IIntentParser>();
        var router = new ActionRouter(parser.Object);
        var fileAction = new Mock<IAction>();
        fileAction.SetupGet(a => a.Name).Returns("write_file");
        fileAction.SetupGet(a => a.RiskLevel).Returns(ActionRiskLevel.Safe);
        fileAction.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        fileAction.Setup(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Successful("Saved C:\\site.html",
                new Dictionary<string, object> { ["path"] = "C:\\site.html" }));
        router.RegisterAction(fileAction.Object);

        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            AI = new AISettings { Provider = "compatible", BaseUrl = "http://localhost:20128/v1", Model = "m" }
        });
        var secrets = new Mock<ISecretStore>();
        secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("test-key");
        secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var requests = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, _) =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync());
            var body = requests.Count == 1
                ? """{"choices":[{"message":{"content":null,"tool_calls":[{"function":{"name":"write_file","arguments":"{\"path\":\"site.html\",\"content\":\"<h1>Hello</h1>\"}"}}]}}]}"""
                : """{"choices":[{"message":{"content":"I made the website."}}]}""";
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });
        var factory = new AIProviderFactory(settings.Object, secrets.Object,
            NullLoggerFactory.Instance, new HttpClient(handler));
        var assistant = new AIAssistant(router, factory, settings.Object, secrets.Object,
            NullLogger<AIAssistant>.Instance);

        var (result, answer) = await assistant.ProcessAsync("", audioWav: new byte[] { 1, 2, 3 });

        result.Success.Should().BeTrue();
        answer.Should().Contain("website");
        result.Data!["path"].Should().Be("C:\\site.html");
        result.Data["tool_log"].Should().BeOfType<string>().Which.Should().Contain("Saved C:\\site.html");
        requests.Should().HaveCount(2);
        requests[1].Should().Contain("input_audio");
        using var retry = System.Text.Json.JsonDocument.Parse(requests[1]);
        var textPart = retry.RootElement.GetProperty("messages").EnumerateArray().Last()
            .GetProperty("content")[0].GetProperty("text").GetString();
        textPart.Should().Contain("Saved C:\\site.html");
    }
}
