namespace Nexa.Windows.AppLauncher;

/// <summary>
/// Pure fuzzy-matching logic for application names. Separated out so it can be
/// unit tested without touching the Windows COM/shell layer.
/// </summary>
public static class AppMatcher
{
    /// <summary>
    /// Ranks installed applications against a query and returns the best match.
    /// Returns null when no application qualifies.
    /// </summary>
    public static InstalledApp? FindBestMatch(IEnumerable<InstalledApp> apps, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var lowerQuery = query.Trim().ToLowerInvariant();

        InstalledApp? best = null;
        int bestScore = int.MinValue;

        foreach (var app in apps)
        {
            var score = Score(app.Name, lowerQuery);
            var winsOnScore = score > bestScore;
            var winsOnTie = score == bestScore && score > 0 && best != null &&
                            string.Compare(app.Name, best.Name, StringComparison.OrdinalIgnoreCase) < 0;

            if (winsOnScore || winsOnTie)
            {
                bestScore = score;
                best = app;
            }
        }

        return bestScore > 0 ? best : null;
    }

    /// <summary>
    /// Scores how well an application name matches a (lowercased) query.
    /// Higher is better; 0 means no meaningful match.
    /// </summary>
    public static int Score(string appName, string lowerQuery)
    {
        var name = appName?.Trim().ToLowerInvariant() ?? "";
        if (name.Length == 0) return 0;

        // Exact match
        if (name == lowerQuery) return 1000;

        // Name starts with the query
        if (name.StartsWith(lowerQuery)) return 800 + (lowerQuery.Length * 2);

        // Query starts with the name (abbreviation, e.g. "vs" for "visual studio code")
        if (lowerQuery.StartsWith(name)) return 700 + (name.Length * 2);

        // Whole-word match
        var queryWords = lowerQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length > 1 && queryWords.All(w => name.Contains(w)))
            return 600 + (queryWords.Length * 10);

        // Contains match
        if (name.Contains(lowerQuery)) return 500 + lowerQuery.Length;

        // Acronym match: "vsc" -> "visual studio code"
        var acronym = new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w[0]).ToArray());
        if (acronym == lowerQuery) return 650;

        // Greedy subsequence match (typo-tolerant): "visualstudiocode"
        if (IsSubsequence(name.Replace(" ", ""), lowerQuery.Replace(" ", ""))) return 200;

        return 0;
    }

    private static bool IsSubsequence(string text, string query)
    {
        int qi = 0;
        foreach (var ch in text)
        {
            if (qi < query.Length && char.ToLowerInvariant(ch) == query[qi])
            {
                qi++;
            }
        }
        return qi == query.Length;
    }
}