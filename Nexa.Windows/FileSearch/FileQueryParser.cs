using System.Text.RegularExpressions;

namespace Nexa.Windows.FileSearch;

/// <summary>
/// Parses natural-language file queries like "newest pdf" or "invoice excel".
/// Pure logic so it can be unit tested without filesystem access.
/// </summary>
public static class FileQueryParser
{
    private static readonly Dictionary<string, string> ExtensionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = ".pdf",
        ["excel"] = ".xlsx",
        ["xlsx"] = ".xlsx",
        ["xls"] = ".xls",
        ["word"] = ".docx",
        ["docx"] = ".docx",
        ["doc"] = ".doc",
        ["powerpoint"] = ".pptx",
        ["pptx"] = ".pptx",
        ["image"] = ".png",
        ["photo"] = ".png",
        ["picture"] = ".png",
        ["screenshot"] = ".png",
        ["jpg"] = ".jpg",
        ["jpeg"] = ".jpg",
        ["png"] = ".png",
        ["zip"] = ".zip",
        ["mp3"] = ".mp3",
        ["mp4"] = ".mp4",
        ["txt"] = ".txt",
        ["text"] = ".txt",
        ["csv"] = ".csv",
    };

    /// <summary>
    /// Parses a query into a structured file search request.
    /// </summary>
    public static FileQuery Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new FileQuery();

        var result = new FileQuery();

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Detect the "downloaded N days ago" time window before word-level pass
        var daysAgoMatch = Regex.Match(query, @"downloaded\s+(\d+)\s+days?\s+ago", RegexOptions.IgnoreCase);
        if (daysAgoMatch.Success)
        {
            result.MinimumAge = 0;
            result.MaximumAge = int.Parse(daysAgoMatch.Groups[1].Value) + 1;
            result.RecencySort = FileQuery.RecencyMode.NewestFirst;
            var daysAgoWords = daysAgoMatch.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in daysAgoWords)
            {
                words.RemoveAll(w2 => w2.Equals(w, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Detect recency words
        for (int i = words.Count - 1; i >= 0; i--)
        {
            var word = words[i].TrimEnd('?', '!', '.', ',');

            switch (word.ToLowerInvariant())
            {
                case "newest":
                    result.RecencySort = FileQuery.RecencyMode.NewestFirst;
                    words.RemoveAt(i);
                    break;
                case "latest":
                    result.RecencySort = FileQuery.RecencyMode.NewestFirst;
                    words.RemoveAt(i);
                    break;
                case "oldest":
                    result.RecencySort = FileQuery.RecencyMode.OldestFirst;
                    words.RemoveAt(i);
                    break;
                case "yesterday":
                    result.MinimumAge = 0;
                    result.MaximumAge = 2;
                    result.RecencySort = FileQuery.RecencyMode.NewestFirst;
                    words.RemoveAt(i);
                    break;
                case "today":
                    result.MinimumAge = 0;
                    result.MaximumAge = 1;
                    result.RecencySort = FileQuery.RecencyMode.NewestFirst;
                    words.RemoveAt(i);
                    break;
            }
        }

        // Detect any remaining extension alias anywhere in the text
        foreach (var pair in ExtensionAliases)
        {
            if (words.Any(w => w.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                result.Extensions.Add(pair.Value);
                words.RemoveAll(w => w.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Detect explicit extensions like "invoice.pdf"
        foreach (var word in words.ToList())
        {
            var extMatch = Regex.Match(word, @"^[a-z0-9.]+(\.\w+)$", RegexOptions.IgnoreCase);
            if (extMatch.Success)
            {
                result.Extensions.Add(extMatch.Groups[1].Value.ToLowerInvariant());
                words.Remove(word);
            }
        }

        // Strip noise words
        var noiseWords = new[]
        {
            "the", "a", "an", "my", "find", "search", "for", "file", "files",
            "looking", "need", "want", "show", "me", "open", "of", "and", "or"
        };
        words.RemoveAll(w => noiseWords.Contains(w.ToLowerInvariant()));

        result.NameTerms.AddRange(words);

        return result;
    }
}

public class FileQuery
{
    public enum RecencyMode
    {
        None,
        NewestFirst,
        OldestFirst
    }

    public List<string> NameTerms { get; } = new();
    public List<string> Extensions { get; } = new();
    public RecencyMode RecencySort { get; set; } = RecencyMode.None;
    public int MinimumAge { get; set; }   // in days
    public int MaximumAge { get; set; } = int.MaxValue;

    public bool HasMeaningfulCriteria => NameTerms.Count > 0 || Extensions.Count > 0;
}