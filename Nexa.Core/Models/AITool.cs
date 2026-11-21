namespace Nexa.Core.Models;

/// <summary>
/// Definition of a tool that can be called by AI.
/// </summary>
public class AITool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Dictionary<string, AIToolParameter> Parameters { get; set; } = new();
}

/// <summary>
/// Parameter definition for an AI tool.
/// </summary>
public class AIToolParameter
{
    public required string Type { get; set; }
    public required string Description { get; set; }
    public bool Required { get; set; }
    public List<string>? Enum { get; set; }
}
