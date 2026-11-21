using System.IO;

namespace Nexa.Windows.FileSearch;

/// <summary>
/// Searches for files in common user directories.
/// </summary>
public class FileSearchService
{
    private readonly string[] _searchDirectories;

    public FileSearchService()
    {
        _searchDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
        };
    }

    public Task<List<FileSearchResult>> SearchAsync(string query, int maxResults = 20)
    {
        return Task.Run(() =>
        {
            var results = new List<FileSearchResult>();
            var lowerQuery = query.ToLowerInvariant();

            foreach (var directory in _searchDirectories)
            {
                if (!Directory.Exists(directory)) continue;

                try
                {
                    var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                        .Take(500); // Limit to prevent slow scans

                    foreach (var file in files)
                    {
                        if (results.Count >= maxResults) break;

                        var fileName = Path.GetFileName(file).ToLowerInvariant();

                        // Check if query matches filename
                        if (MatchesQuery(fileName, lowerQuery))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                results.Add(new FileSearchResult
                                {
                                    FileName = Path.GetFileName(file),
                                    FullPath = file,
                                    Directory = Path.GetDirectoryName(file) ?? "",
                                    Size = fileInfo.Length,
                                    ModifiedDate = fileInfo.LastWriteTime,
                                    Extension = fileInfo.Extension
                                });
                            }
                            catch { /* Skip inaccessible files */ }
                        }
                    }
                }
                catch { /* Skip inaccessible directories */ }

                if (results.Count >= maxResults) break;
            }

            // Sort by modified date (newest first)
            return results.OrderByDescending(r => r.ModifiedDate).ToList();
        });
    }

    public Task<List<FileSearchResult>> GetRecentFilesAsync(int maxResults = 20, string[]? extensions = null)
    {
        return Task.Run(() =>
        {
            var results = new List<FileSearchResult>();
            var cutoffDate = DateTime.Now.AddDays(-7); // Last 7 days

            foreach (var directory in _searchDirectories)
            {
                if (!Directory.Exists(directory)) continue;

                try
                {
                    var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                        .Where(f => File.GetLastWriteTime(f) >= cutoffDate);

                    foreach (var file in files)
                    {
                        if (results.Count >= maxResults * 2) break;

                        try
                        {
                            var fileInfo = new FileInfo(file);

                            if (extensions != null && extensions.Length > 0)
                            {
                                var ext = fileInfo.Extension.ToLowerInvariant();
                                if (!extensions.Contains(ext)) continue;
                            }

                            results.Add(new FileSearchResult
                            {
                                FileName = fileInfo.Name,
                                FullPath = file,
                                Directory = fileInfo.DirectoryName ?? "",
                                Size = fileInfo.Length,
                                ModifiedDate = fileInfo.LastWriteTime,
                                Extension = fileInfo.Extension
                            });
                        }
                        catch { /* Skip inaccessible files */ }
                    }
                }
                catch { /* Skip inaccessible directories */ }

                if (results.Count >= maxResults * 2) break;
            }

            return results
                .OrderByDescending(r => r.ModifiedDate)
                .Take(maxResults)
                .ToList();
        });
    }

    private bool MatchesQuery(string fileName, string query)
    {
        // Direct contains match
        if (fileName.Contains(query)) return true;

        // Word match
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return queryWords.Length > 0 && queryWords.All(w => fileName.Contains(w));
    }
}

public class FileSearchResult
{
    public required string FileName { get; set; }
    public required string FullPath { get; set; }
    public required string Directory { get; set; }
    public long Size { get; set; }
    public DateTime ModifiedDate { get; set; }
    public required string Extension { get; set; }

    public string FormattedSize
    {
        get
        {
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
        }
    }
}