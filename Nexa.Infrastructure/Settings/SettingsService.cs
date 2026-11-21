using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexa.Infrastructure.Settings;

/// <summary>
/// Manages application settings with JSON persistence.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService>? _logger;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public SettingsService(ILogger<SettingsService>? logger = null)
    {
        _logger = logger;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nexa"
        );
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _settings = new AppSettings();
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                _logger?.LogInformation("Settings loaded from {Path}", _settingsPath);
            }
            else
            {
                _logger?.LogInformation("No settings file found, using defaults");
                _settings = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings, using defaults");
            _settings = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_settings, options);
            await File.WriteAllTextAsync(_settingsPath, json);
            _logger?.LogInformation("Settings saved to {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
            throw;
        }
    }
}
