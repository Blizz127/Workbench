using System.Text.RegularExpressions;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Builds the exact tmux command lines for every supported operation. This is
/// the only place tmux commands are constructed, and it deliberately offers
/// no fleet-wide primitives: no <c>kill-server</c>, no <c>kill-session -a</c>,
/// no free-form shell text — every command targets exactly one validated
/// session name.
/// </summary>
public static partial class TmuxCommandBuilder
{
    /// <summary>Read-only session listing with a stable, pipe-delimited format.</summary>
    public const string ListSessionsCommand =
        "tmux list-sessions -F '#{session_name}|#{session_created}|#{session_attached}|#{session_windows}|#{window_name}'";

    /// <summary>Session names: alphanumerics, underscore, hyphen. tmux itself
    /// forbids '.' and ':' (target separators); the tight charset also makes
    /// shell injection impossible by construction.</summary>
    public static bool IsValidSessionName(string? name) =>
        name is not null && SessionNamePattern().IsMatch(name);

    /// <summary>Read-only existence probe (exit 0 = session exists).</summary>
    public static string HasSessionCommand(string sessionName)
    {
        RequireValidName(sessionName);
        return $"tmux has-session -t {sessionName}";
    }

    /// <summary>The command a terminal runs to attach to a session.</summary>
    public static string AttachCommand(string sessionName)
    {
        RequireValidName(sessionName);
        return $"tmux attach-session -t {sessionName}";
    }

    /// <summary>Create a detached session, optionally rooted at a working directory.</summary>
    public static TmuxOperation BuildCreate(string sessionName, string? workingDirectory = null)
    {
        RequireValidName(sessionName);
        var command = $"tmux new-session -d -s {sessionName}";
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            command += $" -c \"{workingDirectory.Replace("\"", "")}\"";

        return new TmuxOperation(TmuxOperationKind.CreateSession, sessionName, null,
            command, RequiresConfirmation: false,
            workingDirectory is null
                ? $"Create a new detached tmux session named '{sessionName}'."
                : $"Create a new detached tmux session named '{sessionName}' starting in {workingDirectory}.");
    }

    /// <summary>Rename one session. Mutating; requires confirmation.</summary>
    public static TmuxOperation BuildRename(string sessionName, string newName)
    {
        RequireValidName(sessionName);
        RequireValidName(newName, nameof(newName));

        return new TmuxOperation(TmuxOperationKind.RenameSession, sessionName, newName,
            $"tmux rename-session -t {sessionName} {newName}", RequiresConfirmation: true,
            $"Rename session '{sessionName}' to '{newName}'. Anything attached keeps running.");
    }

    /// <summary>Kill exactly one session. Destructive to that session; requires confirmation.</summary>
    public static TmuxOperation BuildKill(string sessionName)
    {
        RequireValidName(sessionName);

        return new TmuxOperation(TmuxOperationKind.KillSession, sessionName, null,
            $"tmux kill-session -t {sessionName}", RequiresConfirmation: true,
            $"Kill session '{sessionName}' and every process running inside it. This cannot be undone.");
    }

    private static void RequireValidName(string? name, string paramName = "sessionName")
    {
        if (!IsValidSessionName(name))
            throw new ArgumentException(
                $"Invalid tmux session name: {name ?? "(null)"} — letters, digits, '_' and '-' only.", paramName);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex SessionNamePattern();
}
