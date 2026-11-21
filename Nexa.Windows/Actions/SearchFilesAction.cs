using Nexa.Windows.FileSearch;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to search for files.
/// </summary>
public class SearchFilesAction : IAction
{
    private readonly FileSearchService _search;

    public string Name => "search_files";
    public string Description => "Searches for files in user directories";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public SearchFilesAction(FileSearchService search)
    {
        _search = search;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("query") && parameters["query"] is string;
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
        {
            return ActionResult.Failed("Missing required parameter: query");
        }

        var query = parameters["query"]?.ToString() ?? "";
        var maxResults = 10;

        if (parameters.TryGetValue("max_results", out var maxResultsObj))
        {
            if (maxResultsObj is int mi) maxResults = mi;
            else if (maxResultsObj is long ml) maxResults = (int)Math.Min(ml, int.MaxValue);
        }

        // If the query describes a "newest/recent" style request, use the structured
        // file query to search by extension and recency rather than by name contains.
        var parsed = FileQueryParser.Parse(query);
        List<FileSearchResult> results;

        if (parsed.RecencySort != FileQuery.RecencyMode.None && parsed.Extensions.Count > 0)
        {
            // e.g. "newest pdf", "excel downloaded yesterday"
            results = await _search.GetRecentFilesAsync(maxResults, parsed.Extensions.ToArray());
        }
        else if (parsed.HasMeaningfulCriteria)
        {
            var nameQuery = string.Join(" ", parsed.NameTerms);
            if (parsed.NameTerms.Count == 0 && parsed.Extensions.Count > 0)
            {
                // Search by extension only via full scan with filter term
                nameQuery = "*";
            }
            results = await _search.SearchAsync(nameQuery, maxResults);
        }
        else
        {
            results = await _search.GetRecentFilesAsync(maxResults);
        }

        if (results.Count == 0)
        {
            return ActionResult.Failed($"No files found matching: {query}");
        }

        var data = new Dictionary<string, object>
        {
            ["files"] = results.Select(f => new FileSearchItem
            {
                Name = f.FileName,
                Path = f.FullPath,
                Modified = f.ModifiedDate.ToString("g"),
                Size = f.FormattedSize,
                Extension = f.Extension
            }).ToList()
        };

        return ActionResult.Successful($"Found {results.Count} file(s)", data);
    }
}