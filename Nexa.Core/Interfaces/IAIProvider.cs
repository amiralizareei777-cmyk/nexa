using Nexa.Core.Models;

namespace Nexa.Core.Interfaces;

public interface IAIProvider
{
    string Name { get; }
    Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<AIResponse> ProcessQueryAsync(
        string query,
        IEnumerable<AITool> availableTools,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        byte[]? audioWav = null);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);
}
