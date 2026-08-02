namespace Workbench.Core.Models;

/// <summary>Lifecycle of a blocker. Resolved and Dismissed both count as closed.</summary>
public enum BlockerStatus
{
    Active,
    Monitoring,
    Resolved,
    Dismissed,
}

/// <summary>
/// Something preventing progress. Resolved/dismissed blockers are kept as
/// history. The rich fields (stable id, severity, status, owner, tags…) are
/// what the shared state file syncs across machines; <see cref="StableId"/> is
/// the cross-machine identity. <see cref="IsResolved"/> is kept in sync with
/// <see cref="Status"/> (Resolved/Dismissed → resolved) so older queries that
/// only know the flag stay correct.
/// </summary>
public sealed class Blocker
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>Stable cross-machine identity; generated on insert when missing.</summary>
    public string StableId { get; set; } = string.Empty;

    /// <summary>Short headline; falls back to <see cref="Description"/> for older rows.</summary>
    public string? Title { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? BlockerClass { get; set; }

    public string? Severity { get; set; }

    public BlockerStatus Status { get; set; } = BlockerStatus.Active;

    public string? Evidence { get; set; }

    public string? NextSafestStep { get; set; }

    public string? Owner { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>Where the blocker came from (ticket, log path, upstream issue…).</summary>
    public string? SourceReference { get; set; }

    public bool IsResolved { get; set; }

    /// <summary>How the blocker was resolved, once it is. Local-only detail — not shared.</summary>
    public string? Resolution { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Display headline: the title when present, otherwise the description.</summary>
    public string TitleOrDescription => Title is { Length: > 0 } title ? title : Description;
}
