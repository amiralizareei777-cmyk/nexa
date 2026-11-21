namespace Nexa.Core.Models;

/// <summary>
/// Parsed user intent from a command.
/// </summary>
public class Intent
{
    public required string ActionName { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public float Confidence { get; set; } = 1.0f;
}
