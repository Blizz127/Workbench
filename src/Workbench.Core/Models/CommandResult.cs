namespace Workbench.Core.Models;

/// <summary>
/// Outcome of the launch <em>command itself</em> — process start, exit code,
/// cancellation. Deliberately separate from <see cref="RuntimeHealth"/>:
/// a detached launcher (<c>podman compose up -d</c>, <c>setsid</c>) exits 0
/// even when a service it started crashes seconds later.
/// </summary>
public enum CommandResult
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
}
