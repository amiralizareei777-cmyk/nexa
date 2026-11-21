using FluentAssertions;
using Xunit;
using Nexa.Core.Models;
using Nexa.Core.AI;

namespace Nexa.Tests;

public class VoiceAndSettingsTests
{
    [Fact]
    public void AppSettings_DefaultProvider_IsCompatible()
    {
        new AppSettings().AI.Provider.Should().Be("compatible");
    }

    [Fact]
    public void AppSettings_DefaultVoiceLanguage_IsAuto()
    {
        var voice = new AppSettings().Voice;
        voice.SpeechLanguage.Should().Be("auto");
        voice.VoiceResponses.Should().BeTrue();
        voice.SpeechRate.Should().Be(0);
    }

    [Theory]
    [InlineData("openai", "ApiKey:openai")]
    [InlineData("gemini", "ApiKey:gemini")]
    [InlineData("OmniRoute", "ApiKey:omniroute")]
    public void ProviderKeys_AreStoredSeparately(string provider, string expected)
    {
        ProviderKeyStore.KeyFor(provider).Should().Be(expected);
    }

    [Fact]
    public void AppSettings_SettingsFile_StoresNoApiKey()
    {
        // The AppSettings model has no ApiKey field — secrets live only in the secret store.
        typeof(AppSettings).GetProperties()
            .SelectMany(p => p.PropertyType.GetProperties())
            .Select(p => p.Name)
            .Should().NotContain("ApiKey");
    }
}
