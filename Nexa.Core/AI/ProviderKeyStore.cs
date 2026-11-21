namespace Nexa.Core.AI;

/// <summary>Credential Manager target for a provider's own API key.</summary>
public static class ProviderKeyStore
{
    public static string KeyFor(string? provider) =>
        $"ApiKey:{(string.IsNullOrWhiteSpace(provider) ? "compatible" : provider.Trim().ToLowerInvariant())}";
}
