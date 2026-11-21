using System.IO;
using System.Text;

namespace Nexa.Windows.Actions;

/// <summary>Creates a user file directly instead of relying on simulated Notepad keystrokes.</summary>
public sealed class WriteFileAction : IAction
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".css", ".js", ".json", ".txt", ".md", ".svg", ".py", ".cs"
    };
    private readonly RecentArtifactStore _recent;

    public WriteFileAction(RecentArtifactStore recent) => _recent = recent;
    public string Name => "write_file";
    public string Description => "Creates and saves a new text or website file in the user's folders";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public bool CanExecute(Dictionary<string, object> parameters)
        => parameters.TryGetValue("path", out var path) && path is string p && !string.IsNullOrWhiteSpace(p)
           && parameters.TryGetValue("content", out var content) && content is string;

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
            return ActionResult.Failed("A file path and its text content are required.");

        try
        {
            var path = ResolvePath((string)parameters["path"]);
            if (!AllowedExtensions.Contains(Path.GetExtension(path)))
                return ActionResult.Failed("Nexa can create text and website files only (.html, .css, .js, .txt, etc.).");

            var content = (string)parameters["content"];
            if (Encoding.UTF8.GetByteCount(content) > 250_000)
                return ActionResult.Failed("The file is too large for a single voice request.");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                await writer.WriteAsync(content.AsMemory(), cancellationToken);

            _recent.LastCreatedPath = path;
            return ActionResult.Successful($"Saved {path}", new Dictionary<string, object>
            {
                ["path"] = path
            });
        }
        catch (IOException ex) when (File.Exists(ResolvePath((string)parameters["path"])))
        {
            return ActionResult.Failed("That file already exists. Choose a new name before saving.", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ActionResult.Failed($"Could not save the file: {ex.Message}", ex);
        }
    }

    internal static string ResolvePath(string supplied)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var defaultFolder = Path.Combine(desktop, "Nexa Files");
        var candidate = Path.IsPathFullyQualified(supplied)
            ? Path.GetFullPath(supplied)
            : Path.GetFullPath(supplied, defaultFolder);
        var roots = new[]
        {
            desktop,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        if (!roots.Any(root => candidate.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                                           + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Choose Desktop, Documents, or Downloads for the new file.");
        return candidate;
    }
}
