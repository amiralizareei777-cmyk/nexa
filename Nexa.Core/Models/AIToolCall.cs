namespace Nexa.Core.Models;

/// <summary>
/// A tool call from the AI provider.
/// </summary>
public class AIToolCall
{
    public required string ToolName { get; set; }
    public Dictionary<string, object> Arguments { get; set; } = new();
}
