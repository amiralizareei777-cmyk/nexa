using Microsoft.Extensions.Logging;
using Nexa.Core.Parsing;

namespace Nexa.Core.AI;

/// <summary>
/// Coordinates the full command flow: AI tool calling when configured, with local
/// intent parsing as an offline fallback. Exposes extant actions
/// to the model as structured tools.
/// </summary>
public class AIAssistant
{
    /// <summary>
    /// Maximum number of provider round-trips (tool-call loops) per user request.
    /// </summary>
    public const int MaxToolCallsPerRequest = 5;

    private readonly ActionRouter _router;
    private readonly AIProviderFactory _providerFactory;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<AIAssistant>? _logger;

    public AIAssistant(
        ActionRouter router,
        AIProviderFactory providerFactory,
        ISettingsService settings,
        ISecretStore secretStore,
        ILogger<AIAssistant>? logger)
    {
        _router = router;
        _providerFactory = providerFactory;
        _settings = settings;
        _secretStore = secretStore;
        _logger = logger;
    }

    /// <summary>
    /// Processes a user command through the AI provider when configured.
    /// Returns an ActionResult and optionally a text answer for display.
    /// </summary>
    public async Task<(ActionResult Result, string? AnswerText)> ProcessAsync(
        string command,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        byte[]? audioWav = null,
        Func<string, Dictionary<string, object>, Task<bool>>? confirmToolCall = null)
    {
        // AI-first: any command goes to the AI when a provider is configured, or
        // whenever raw audio is supplied (only the AI can transcribe audio). The
        // local parser only runs as an offline fallback when there is no provider.
        var aiWanted = audioWav != null || await IsProviderConfiguredAsync();

        if (aiWanted)
        {
            try
            {
                // The "ask ai:" prefix is redundant now but stripping keeps it invisible to the model.
                var (cleanCommand, _) = StripAskPrefix(command);

                var tools = BuildToolDefinitions();
                var provider = _providerFactory.CreateProvider();
                cancellationToken.ThrowIfCancellationRequested();

                StringBuilder? toolMessages = null;
                ActionResult? lastToolResult = null;
                var response = await provider.ProcessQueryAsync(cleanCommand, tools, conversationHistory, cancellationToken, audioWav);
                var recognizedRequest = response.RecognizedUserText;

                for (int callIndex = 0; callIndex < MaxToolCallsPerRequest && response.HasToolCalls; callIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    toolMessages ??= new StringBuilder();
                    foreach (var toolCall in response.ToolCalls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((toolCall.ToolName == "take_screenshot" && !_settings.Settings.Privacy.AllowScreenshotsToAI) ||
                            (toolCall.ToolName == "search_files" && !_settings.Settings.Privacy.AllowFileNamesToAI))
                        {
                            toolMessages.AppendLine($"'{toolCall.ToolName}' is disabled in Nexa Privacy settings.");
                            continue;
                        }
                        var action = _router.GetAction(toolCall.ToolName);
                        if (action == null)
                        {
                            toolMessages.AppendLine($"Tool '{toolCall.ToolName}' is not available.");
                            continue;
                        }

                        if (!action.CanExecute(toolCall.Arguments))
                        {
                            toolMessages.AppendLine($"Invalid parameters for '{toolCall.ToolName}'.");
                            continue;
                        }

                        // Guarded tools (e.g. run_command) require explicit user approval.
                        // If no confirmation provider is wired, refuse by default.
                        var confirmed = false;
                        if (action.RiskLevel == ActionRiskLevel.ConfirmationRequired)
                        {
                            confirmed = confirmToolCall == null
                                ? false
                                : await confirmToolCall(toolCall.ToolName, toolCall.Arguments);
                            if (!confirmed)
                            {
                                toolMessages.AppendLine(
                                    $"The user declined to run '{toolCall.ToolName}'. Do not retry it; " +
                                    "offer a different approach or ask what the user would like instead.");
                                continue;
                            }
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        var intent = new Intent
                        {
                            ActionName = toolCall.ToolName,
                            Parameters = toolCall.Arguments
                        };
                        var result = confirmed
                            ? await _router.ExecuteConfirmedIntentAsync(intent, cancellationToken)
                            : await _router.ExecuteIntentAsync(intent, cancellationToken);

                        lastToolResult = result;
                        toolMessages.AppendLine(result.Success ? result.Message : $"Failed: {result.Message}");
                    }

                    var retryAudio = audioWav != null && string.IsNullOrWhiteSpace(recognizedRequest)
                        ? audioWav : null;
                    response = await provider.ProcessQueryAsync(
                        BuildRetryPrompt(recognizedRequest ?? cleanCommand, retryAudio, toolMessages),
                        tools, conversationHistory, cancellationToken, retryAudio);
                    recognizedRequest ??= response.RecognizedUserText;
                }

                if (response.HasToolCalls)
                    return (ActionResult.Failed("Too many steps. The operation was limited."), response.TextResponse);
                Dictionary<string, object>? resultData = lastToolResult?.Data is null
                    ? null : new Dictionary<string, object>(lastToolResult.Data);
                if (!string.IsNullOrWhiteSpace(recognizedRequest))
                {
                    resultData ??= new Dictionary<string, object>();
                    resultData["spoken_request"] = recognizedRequest;
                }
                if (toolMessages is { Length: > 0 })
                {
                    resultData ??= new Dictionary<string, object>();
                    resultData["tool_log"] = toolMessages.ToString();
                }
                if (!string.IsNullOrWhiteSpace(response.TextResponse))
                    return (new ActionResult
                    {
                        Success = lastToolResult?.Success ?? true,
                        Message = lastToolResult?.Message ?? "Answer from AI",
                        Data = resultData
                    }, response.TextResponse);
                if (lastToolResult != null)
                    lastToolResult.Data = resultData;
                return lastToolResult != null
                    ? (lastToolResult, null)
                    : (ActionResult.Failed("The AI returned no answer."), null);
            }
            catch (OperationCanceledException)
            {
                // User-initiated cancellation must propagate so the caller can stay
                // ready for the next command instead of treating it as a failure.
                if (cancellationToken.IsCancellationRequested) throw;
                _logger?.LogWarning("AI request was aborted.");
                return (ActionResult.Failed("The AI request was cancelled or timed out."), null);
            }
            catch (NotSupportedException ex)
            {
                return (ActionResult.Failed(ex.Message), null);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith("Gemini error (TooManyRequests)", StringComparison.Ordinal))
            {
                return (ActionResult.Failed(
                    "Gemini's request limit was reached for this model. Wait a moment before speaking again, or check your project's rate limits in Google AI Studio."), null);
            }
            catch (InvalidOperationException ex) when (
                ex.Message == "Gemini API key is not configured.")
            {
                return (ActionResult.Failed("Gemini API key is missing. Add it in Nexa Settings → AI."), null);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith("OpenRouter ", StringComparison.Ordinal))
            {
                return (ActionResult.Failed(ex.Message), null);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AI processing failed");
                return (ActionResult.Failed("Nexa couldn't reach the configured AI provider."), null);
            }
        }

        // Offline fallback: no AI provider, so use the local regex parser for basics.
        var localIntent = _router.ParseLocal(command);
        if (localIntent != null)
        {
            var result = await _router.ExecuteIntentAsync(localIntent, cancellationToken);
            return (result, null);
        }

        return (ActionResult.Failed("I couldn't understand that. Try 'open Notepad', 'volume 30', or configure an AI provider for complex commands."), null);
    }

    private static string BuildRetryPrompt(string cleanCommand, byte[]? audioWav, StringBuilder toolMessages)
        => audioWav != null
            ? $"Listen to the attached spoken request again. Continue and complete that same request using these tool results; do not repeat successful actions.\n\nTool results:\n{toolMessages}"
            : $"{cleanCommand}\n\nTool results:\n{toolMessages}\nContinue with the conversation or complete the request. Do not repeat actions that succeeded.";

    private (string Command, bool ForceAsk) StripAskPrefix(string command)
    {
        var lower = command.TrimStart().ToLowerInvariant();
        var prefixes = new[] { "ask ai:", "ask ai ", "ask the ai:", "ask the ai " };
        foreach (var prefix in prefixes)
        {
            if (lower.StartsWith(prefix))
            {
                return (command.Trim().Substring(prefix.Length).Trim(), true);
            }
        }
        return (command.Trim(), false);
    }

    private async Task<bool> IsProviderConfiguredAsync()
    {
        try
        {
            var settings = _settings.Settings;
            return !string.IsNullOrEmpty(settings.AI.BaseUrl) ||
                   await _secretStore.SecretExistsAsync(ProviderKeyStore.KeyFor(settings.AI.Provider));
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<AITool> BuildToolDefinitions()
    {
        var tools = new List<AITool>();

        void Add(string name, string description, params (string key, string type, string desc, bool required, string[]? en)[] ps)
        {
            var tool = new AITool { Name = name, Description = description };
            foreach (var (key, type, desc, required, en) in ps)
            {
                tool.Parameters[key] = new AIToolParameter { Type = type, Description = desc, Required = required, Enum = en?.ToList() };
            }
            tools.Add(tool);
        }

        Add("open_application", "Opens an installed Windows application by name.",
            ("app_name", "string", "The application name, e.g. 'Chrome', 'Notepad', 'Telegram'", true, null));

        Add("open_folder", "Opens a folder in Windows Explorer.",
            ("path", "string", "Folder name ('Downloads', 'Desktop', 'Documents') or a full path like C:\\Projects", true, null));

        if (_settings.Settings.Privacy.AllowFileNamesToAI)
            Add("search_files", "Searches for files in common user directories.",
                ("query", "string", "File name or keywords, e.g. 'invoice', or 'newest pdf'", true, null),
                ("max_results", "integer", "Maximum number of results", false, null));

        Add("set_volume", "Sets the system volume to a percentage between 0 and 100.",
            ("level", "integer", "Volume level 0-100", true, null));

        Add("mute_volume", "Mutes or unmutes the system volume.",
            ("mute", "boolean", "True to mute, false to unmute", true, null));

        if (_settings.Settings.Privacy.AllowScreenshotsToAI)
            Add("take_screenshot", "Takes a screenshot of the full screen or the active window.",
                ("capture_type", "string", "full_screen or active_window", false, new[] { "full_screen", "active_window" }));

        Add("open_url", "Opens a URL in the default browser.",
            ("url", "string", "The URL to open (http/https accepted)", true, null));

        Add("write_file", "Creates a NEW text or website file and saves it directly. Use this for requests to write HTML/code in Notepad; do not simulate typing or use run_command. The default folder for a relative name is Desktop\\Nexa Files. Choose a new name if the file already exists. Follow with open_file to preview it.",
            ("path", "string", "File name such as index.html, or an absolute path in Desktop, Documents or Downloads", true, null),
            ("content", "string", "Complete UTF-8 file content, including all website code", true, null));

        Add("open_file", "Opens a local saved file. Omit path, or use 'last', to open the file Nexa most recently created. Use mode 'browser' for an HTML website, 'notepad' for editing, or 'default' otherwise.",
            ("path", "string", "Absolute path, relative name in Desktop\\Nexa Files, or 'last'", false, null),
            ("mode", "string", "default, browser or notepad", false, new[] { "default", "browser", "notepad" }));

        Add("get_system_info", "Gets local system information (CPU, RAM, Windows version, disk space).",
            ("info_type", "string", "cpu, ram, windows, disk, or all", false, new[] { "cpu", "ram", "windows", "disk", "all" }));

        Add("open_vscode", "Opens Visual Studio Code, optionally in a given folder.",
            ("folder_path", "string", "Optional folder to open in VS Code", false, null));

        Add("run_command", "Runs an arbitrary shell command on the user's machine to accomplish tasks no dedicated tool covers. The user is prompted to confirm before it runs. Prefer dedicated tools (open_application, set_volume, search_files, etc.) when available.",
            ("command", "string", "The exact command line to execute, e.g. 'start msedge https://example.com' or 'powershell Get-Date -Format U'", true, null));

        return tools;
    }
}
