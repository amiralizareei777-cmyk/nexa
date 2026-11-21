namespace Nexa.Core.Models;

/// <summary>
/// Represents a file search result returned by the search_files action.
/// </summary>
public class FileSearchItem
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required string Modified { get; set; }
    public required string Size { get; set; }
    public string Extension { get; set; } = "";
}