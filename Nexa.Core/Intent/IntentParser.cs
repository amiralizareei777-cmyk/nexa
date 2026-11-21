using System.Text.RegularExpressions;

namespace Nexa.Core.Parsing;

/// <summary>
/// Parses user commands locally without AI for common patterns.
/// </summary>
public class IntentParser : IIntentParser
{
    private readonly List<CommandPattern> _patterns;

    public IntentParser()
    {
        _patterns = new List<CommandPattern>
        {
            // Volume control
            new(
                @"^(?:set\s+)?volume\s+(?:to\s+)?(\d{1,3})(?:%|\s*percent)?$",
                "set_volume",
                m => new Dictionary<string, object> { ["level"] = int.Parse(m.Groups[1].Value) }
            ),
            new(
                @"^mute$",
                "mute_volume",
                _ => new Dictionary<string, object> { ["mute"] = true }
            ),
            new(
                @"^unmute$",
                "mute_volume",
                _ => new Dictionary<string, object> { ["mute"] = false }
            ),

            // Folder opening — must come before the generic "open ..." app pattern
            new(
                @"^open\s+(downloads|documents|desktop|pictures|music|videos)$",
                "open_folder",
                m => new Dictionary<string, object> { ["path"] = m.Groups[1].Value.ToLowerInvariant() }
            ),
            new(
                @"^open\s+([a-zA-Z]:\\.+)$",
                "open_folder",
                m => new Dictionary<string, object> { ["path"] = m.Groups[1].Value.Trim() }
            ),

            // URL opening — must come before the generic "open ..." app pattern
            new(
                @"^open\s+(https?://.+)$",
                "open_url",
                m => new Dictionary<string, object> { ["url"] = m.Groups[1].Value.Trim() }
            ),
            new(
                @"^open\s+(www\..+)$",
                "open_url",
                m => new Dictionary<string, object> { ["url"] = m.Groups[1].Value.Trim() }
            ),
            new(
                @"^search\s+(?:google\s+)?for\s+(.+)$",
                "open_url",
                m => new Dictionary<string, object>
                {
                    ["url"] = $"https://www.google.com/search?q={Uri.EscapeDataString(m.Groups[1].Value.Trim())}"
                }
            ),

            // VS Code
            new(
                @"^open\s+(?:vs\s*code|visual\s+studio\s+code)(?:\s+in\s+(.+))?$",
                "open_vscode",
                m => new Dictionary<string, object>
                {
                    ["folder_path"] = m.Groups[1].Success ? m.Groups[1].Value.Trim() : ""
                }
            ),

            // Screenshot
            new(
                @"^(?:take\s+a?\s*)?screenshot$",
                "take_screenshot",
                _ => new Dictionary<string, object> { ["capture_type"] = "full_screen" }
            ),
            new(
                @"^capture\s+(?:screen|full\s+screen)$",
                "take_screenshot",
                _ => new Dictionary<string, object> { ["capture_type"] = "full_screen" }
            ),
            new(
                @"^capture\s+(?:the\s+)?(?:active\s+)?window$",
                "take_screenshot",
                _ => new Dictionary<string, object> { ["capture_type"] = "active_window" }
            ),

            // System info
            new(
                @"^(?:what(?:'s| is)?|how much|how many|do i have|tell me).*\b(ram|memory)\b.*$",
                "get_system_info",
                _ => new Dictionary<string, object> { ["info_type"] = "ram" }
            ),
            new(
                @"^(?:what(?:'s| is)?|how much|how many|do i have|tell me).*\b(cpu|processor)\b.*$",
                "get_system_info",
                _ => new Dictionary<string, object> { ["info_type"] = "cpu" }
            ),
            new(
                @"^(?:what(?:'s| is)?|how much|how many|do i have|tell me).*\b(windows\s+version|os\s+version|operating\s+system)\b.*$",
                "get_system_info",
                _ => new Dictionary<string, object> { ["info_type"] = "windows" }
            ),
            new(
                @"^(?:what(?:'s| is)?|how much|how many|do i have|tell me).*\b(disk\s+space|disk|storage|free\s+space)\b.*$",
                "get_system_info",
                _ => new Dictionary<string, object> { ["info_type"] = "disk" }
            ),

            // File search
            new(
                @"^(?:find|search\s+for?)\s+(?:my\s+)?(.+?)(?:\s+files?)?$",
                "search_files",
                m => new Dictionary<string, object> { ["query"] = m.Groups[1].Value.Trim() }
            ),

            // Generic application launching — keep last among the "open" patterns
            new(
                @"^open\s+(.+)$",
                "open_application",
                m => new Dictionary<string, object> { ["app_name"] = m.Groups[1].Value.Trim() }
            ),
            new(
                @"^launch\s+(.+)$",
                "open_application",
                m => new Dictionary<string, object> { ["app_name"] = m.Groups[1].Value.Trim() }
            ),
            new(
                @"^start\s+(.+)$",
                "open_application",
                m => new Dictionary<string, object> { ["app_name"] = m.Groups[1].Value.Trim() }
            ),
        };
    }

    public Intent? TryParseLocal(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var normalizedCommand = command.Trim().ToLowerInvariant();

        foreach (var pattern in _patterns)
        {
            var match = pattern.Regex.Match(normalizedCommand);
            if (match.Success)
            {
                try
                {
                    var parameters = pattern.ParameterExtractor(match);
                    return new Intent
                    {
                        ActionName = pattern.ActionName,
                        Parameters = parameters,
                        Confidence = 0.95f
                    };
                }
                catch
                {
                    continue;
                }
            }
        }

        return null;
    }
}

internal record CommandPattern
{
    public Regex Regex { get; }
    public string ActionName { get; }
    public Func<Match, Dictionary<string, object>> ParameterExtractor { get; }

    public CommandPattern(
        string pattern,
        string actionName,
        Func<Match, Dictionary<string, object>> parameterExtractor)
    {
        Regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        ActionName = actionName;
        ParameterExtractor = parameterExtractor;
    }
}