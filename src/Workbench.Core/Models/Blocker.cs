namespace Workbench.Core.Models;

/// <summary>Something preventing progress. Resolved blockers are kept as history.</summary>
public sealed class Blocker
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsResolved { get; set; }

    /// <summary>How the blocker was resolved, once it is.</summary>
    public string? Resolution { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
