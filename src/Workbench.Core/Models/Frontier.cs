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

    /// <summary>The exact function, symbol, opcode, or subsystem this frontier targets.</summary>
    public string? Symbol { get; set; }

    /// <summary>What supports the current understanding — match %, log excerpt, capture reference.</summary>
    public string? Evidence { get; set; }

    /// <summary>Free-text classification of what's blocking progress (e.g. "Linker: undefined reference", "Unknown ABI").</summary>
    public string? BlockerClass { get; set; }

    /// <summary>The smallest safe action to try next, to hand off cleanly.</summary>
    public string? NextSafestStep { get; set; }

    /// <summary>Which model/person is nominally assigned — plain metadata, not an API call.</summary>
    public string? AssignedModel { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
