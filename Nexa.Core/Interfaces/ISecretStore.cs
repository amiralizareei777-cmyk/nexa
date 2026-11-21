namespace Nexa.Core.Interfaces;

/// <summary>
/// Securely stores and retrieves secrets like API keys.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Stores a secret securely.
    /// </summary>
    Task StoreSecretAsync(string key, string value);

    /// <summary>
    /// Retrieves a secret.
    /// </summary>
    Task<string?> GetSecretAsync(string key);

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    Task DeleteSecretAsync(string key);

    /// <summary>
    /// Checks if a secret exists.
    /// </summary>
    Task<bool> SecretExistsAsync(string key);
}
