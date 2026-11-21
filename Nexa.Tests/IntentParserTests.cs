using FluentAssertions;
using Xunit;
using Nexa.Core.Parsing;

namespace Nexa.Tests;

public class IntentParserTests
{
    private readonly IntentParser _parser = new();

    [Theory]
    [InlineData("open chrome", "open_application", "chrome")]
    [InlineData("open telegram", "open_application", "telegram")]
    [InlineData("launch discord", "open_application", "discord")]
    [InlineData("start spotify", "open_application", "spotify")]
    public void TryParseLocal_ApplicationCommands(string command, string expectedAction, string expectedParam)
    {
        var intent = _parser.TryParseLocal(command);

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be(expectedAction);
        intent.Parameters["app_name"].Should().Be(expectedParam);
    }

    [Theory]
    [InlineData("set volume to 30%", "set_volume", 30)]
    [InlineData("volume 50", "set_volume", 50)]
    [InlineData("volume to 75 percent", "set_volume", 75)]
    public void TryParseLocal_VolumeCommands(string command, string expectedAction, int expectedLevel)
    {
        var intent = _parser.TryParseLocal(command);

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be(expectedAction);
        intent.Parameters["level"].Should().Be(expectedLevel);
    }

    [Theory]
    [InlineData("mute", true)]
    [InlineData("unmute", false)]
    public void TryParseLocal_MuteCommands(string command, bool expectedMute)
    {
        var intent = _parser.TryParseLocal(command);

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be("mute_volume");
        intent.Parameters["mute"].Should().Be(expectedMute);
    }

    [Theory]
    [InlineData("open downloads", "downloads")]
    [InlineData("open desktop", "desktop")]
    [InlineData("open documents", "documents")]
    public void TryParseLocal_FolderCommands(string command, string expectedFolder)
    {
        var intent = _parser.TryParseLocal(command);

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be("open_folder");
        intent.Parameters["path"].Should().Be(expectedFolder);
    }

    [Fact]
    public void TryParseLocal_RejectsInvalidVolumeRange()
    {
        var intent = _parser.TryParseLocal("volume 200");

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be("set_volume");
        intent.Parameters["level"].Should().Be(200);
    }

    [Fact]
    public void TryParseLocal_ReturnsNullForUnknownCommand()
    {
        var intent = _parser.TryParseLocal("explain quantum computing");

        intent.Should().BeNull();
    }

    [Fact]
    public void TryParseLocal_HandlesCaseInsensitiveCommands()
    {
        var intent = _parser.TryParseLocal("OPEN CHROME");

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be("open_application");
    }

    [Theory]
    [InlineData("how much ram do i have", "get_system_info", "ram")]
    [InlineData("what cpu do i have", "get_system_info", "cpu")]
    [InlineData("what windows version am i using", "get_system_info", "windows")]
    [InlineData("how much free space is left", "get_system_info", "disk")]
    public void TryParseLocal_SystemInfoCommands(string command, string expectedAction, string expectedParam)
    {
        var intent = _parser.TryParseLocal(command);

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be(expectedAction);
        intent.Parameters["info_type"].Should().Be(expectedParam);
    }

    [Theory]
    [InlineData("take a screenshot", "full_screen")]
    [InlineData("screenshot", "full_screen")]
    [InlineData("capture the active window", "active_window")]
    public void TryParseLocal_ScreenshotCommands(string command, string expectedCaptureType)
    {
        var intent = _parser.TryParseLocal(command);

        intent.Should().NotBeNull();
        intent!.ActionName.Should().Be("take_screenshot");
        intent.Parameters["capture_type"].Should().Be(expectedCaptureType);
    }
}