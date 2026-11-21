using Microsoft.Extensions.Logging;

namespace Nexa.Core.AI;

public class OmniRouteProvider : OpenAICompatibleProvider
{
    public override string Name => "OmniRoute";
    protected override string DefaultBaseUrl => "http://localhost:20128/v1";

    public OmniRouteProvider(
        ISettingsService settings,
        ISecretStore secretStore,
        HttpClient http,
        ILogger? logger)
        : base(settings, secretStore, http, logger) { }
}
