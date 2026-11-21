using Microsoft.Extensions.Logging;

namespace Nexa.Core.AI;

public class OpenAIProvider : OpenAICompatibleProvider
{
    public override string Name => "OpenAI";
    protected override string DefaultBaseUrl => "https://api.openai.com/v1";
    protected override bool UseDefaultBaseUrlOnly => true;

    public OpenAIProvider(
        ISettingsService settings,
        ISecretStore secretStore,
        HttpClient http,
        ILogger? logger)
        : base(settings, secretStore, http, logger) { }
}
