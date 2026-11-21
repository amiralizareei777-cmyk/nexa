using FluentAssertions;
using Xunit;
using Nexa.Windows.AppLauncher;

namespace Nexa.Tests;

public class AppMatcherTests
{
    private static readonly List<InstalledApp> SampleApps = new()
    {
        new InstalledApp { Name = "Google Chrome", ExecutablePath = "chrome.exe", Source = "test" },
        new InstalledApp { Name = "Visual Studio Code", ExecutablePath = "code.exe", Source = "test" },
        new InstalledApp { Name = "Visual Studio 2022", ExecutablePath = "devenv.exe", Source = "test" },
        new InstalledApp { Name = "Notepad", ExecutablePath = "notepad.exe", Source = "test" },
        new InstalledApp { Name = "Telegram Desktop", ExecutablePath = "telegram.exe", Source = "test" },
        new InstalledApp { Name = "Discord", ExecutablePath = "discord.exe", Source = "test" },
    };

    [Theory]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("notepad", "Notepad")]
    [InlineData("discord", "Discord")]
    [InlineData("telegram", "Telegram Desktop")]
    public void FindBestMatch_ExactAndContainsMatches(string query, string expectedName)
    {
        var best = AppMatcher.FindBestMatch(SampleApps, query);

        best.Should().NotBeNull();
        best!.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("visual studio", "Visual Studio 2022")] // Stronger match on starts-with score
    [InlineData("vs code", "Visual Studio Code")]
    [InlineData("vsc", "Visual Studio Code")]
    public void FindBestMatch_AmbiguousQueries(string query, string expectedName)
    {
        var best = AppMatcher.FindBestMatch(SampleApps, query);

        best.Should().NotBeNull();
        best!.Name.Should().Be(expectedName);
    }

    [Fact]
    public void FindBestMatch_ReturnsNull_ForUnknownApp()
    {
        var best = AppMatcher.FindBestMatch(SampleApps, "photoshop");

        best.Should().BeNull();
    }

    [Fact]
    public void FindBestMatch_ReturnsNull_ForEmptyQuery()
    {
        AppMatcher.FindBestMatch(SampleApps, "").Should().BeNull();
        AppMatcher.FindBestMatch(SampleApps, "   ").Should().BeNull();
    }

    [Fact]
    public void FindBestMatch_HandlesCaseInsensitivity()
    {
        var best = AppMatcher.FindBestMatch(SampleApps, "CHROME");

        best.Should().NotBeNull();
        best!.Name.Should().Be("Google Chrome");
    }

    [Theory]
    [InlineData("visual studio", "Visual Studio 2022")]
    public void FindBestMatch_DistinguishesVsCodeFromVs(string query, string expectedName)
    {
        var best = AppMatcher.FindBestMatch(SampleApps, query);

        best.Should().NotBeNull();
        best!.Name.Should().Be(expectedName);
    }
}