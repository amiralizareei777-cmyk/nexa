namespace Nexa.Core.Models;

/// <summary>
/// Response from an AI provider.
/// </summary>
public class AIResponse
{
    public string? TextResponse { get; set; }
    /// <summary>Text recognized by a separate transcription step, when available.</summary>
    public string? RecognizedUserText { get; set; }
    public List<AIToolCall> ToolCalls { get; set; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;
}
