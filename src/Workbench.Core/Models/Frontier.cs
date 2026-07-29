namespace Workbench.Core.Models;

public enum FrontierStatus
{
    /// <summary>Queued up after the current frontier.</summary>
    Next,

    /// <summary>The edge of progress being pushed right now.</summary>
    Current,

    Completed,
}

/// <summary>
/// A frontier is the current edge of progress — e.g. "func_8001FBE4" in a
/// decompilation, or "lobby protocol" in a server revival.
/// </summary>
public sealed class Frontier
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public FrontierStatus Status { get; set; } = FrontierStatus.Next;

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
