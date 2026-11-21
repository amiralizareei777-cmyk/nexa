namespace Nexa.Core.Interfaces;

/// <summary>
/// Manages application settings and configuration.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current application settings.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Saves settings to persistent storage.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Loads settings from persistent storage.
    /// </summary>
    Task LoadAsync();
}
