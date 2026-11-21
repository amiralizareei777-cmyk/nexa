namespace Nexa.Windows.Actions;

/// <summary>Remembers the last file Nexa created for follow-up requests in this run.</summary>
public sealed class RecentArtifactStore
{
    public string? LastCreatedPath { get; set; }
}
