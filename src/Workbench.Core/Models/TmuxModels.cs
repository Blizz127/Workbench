namespace Workbench.Core.Models;

/// <summary>One tmux session, parsed from <c>tmux list-sessions</c>.</summary>
public sealed record TmuxSession(
    string Name,
    DateTimeOffset? Created,
    bool IsAttached,
    int WindowCount,
    string? ActiveWindowName);

/// <summary>
/// Read-only snapshot of the tmux server. Distinguishes the three honest
/// states: tmux itself missing (<see cref="TmuxAvailable"/> false), tmux
/// installed but no server running (<see cref="ServerRunning"/> false, no
/// error — a normal state), and a running server with zero or more sessions.
/// </summary>
public sealed record TmuxServerStatus(
    bool TmuxAvailable,
    bool ServerRunning,
    IReadOnlyList<TmuxSession> Sessions,
    string? Error)
{
    public static TmuxServerStatus Unavailable(string error) => new(false, false, [], error);
    public static TmuxServerStatus NoServer() => new(true, false, [], null);
}

/// <summary>The tmux mutations Phase 1D supports. Attach is a terminal launch, not a mutation.</summary>
public enum TmuxOperationKind
{
    CreateSession,
    RenameSession,
    KillSession,
}

/// <summary>
/// A fully-built tmux operation: the exact command to run, whether it must be
/// confirmed first, and a plain-English description for the preview panel.
/// </summary>
public sealed record TmuxOperation(
    TmuxOperationKind Kind,
    string SessionName,
    string? NewName,
    string Command,
    bool RequiresConfirmation,
    string Description);

/// <summary>Outcome of executing a tmux operation.</summary>
public sealed record TmuxOperationResult(
    TmuxOperation Operation,
    bool Succeeded,
    bool WasBlocked,
    int? ExitCode,
    string Output)
{
    public static TmuxOperationResult Blocked(TmuxOperation operation, string reason) =>
        new(operation, Succeeded: false, WasBlocked: true, ExitCode: null, Output: reason);
}
