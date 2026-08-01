using System.Text;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Safe, explicit tmux session management behind <see cref="IProcessRunner"/>.</summary>
public interface ITmuxService
{
    /// <summary>Read-only server/session inspection; never changes any tmux state.</summary>
    Task<TmuxServerStatus> InspectAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether one session currently exists (false when no server runs).</summary>
    Task<bool> SessionExistsAsync(string sessionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The session name for a project: the explicit <c>tmuxSession</c> from
    /// workbench.json when set (and valid), otherwise a safe slug synthesized
    /// from the project name.
    /// </summary>
    string ProjectSessionName(Project project);

    /// <summary>
    /// Runs one user-initiated operation. Operations flagged
    /// <see cref="TmuxOperation.RequiresConfirmation"/> are refused unless
    /// <paramref name="confirmed"/> is true — the confirmation gate lives
    /// here, not only in the UI.
    /// </summary>
    Task<TmuxOperationResult> ExecuteAsync(
        TmuxOperation operation,
        bool confirmed = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// tmux operations. Every command comes from <see cref="TmuxCommandBuilder"/>
/// (single-session primitives only — never kill-server, never kill-all),
/// nothing runs at startup, and nothing mutating runs without an explicit
/// user action carrying it here.
/// </summary>
public sealed class TmuxService(IProcessRunner runner) : ITmuxService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public async Task<TmuxServerStatus> InspectAsync(CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(
            TmuxCommandBuilder.ListSessionsCommand, workingDirectory: null, ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode == 0)
            return new TmuxServerStatus(true, true, TmuxListParser.Parse(result.Output), null);

        // The process never started — tmux is almost certainly not installed.
        if (result.ExitCode is null)
            return TmuxServerStatus.Unavailable(
                $"tmux is not available: {result.Output.Trim()}");

        // tmux exits non-zero when no server is running; that is a normal
        // state (no sessions yet), not an error.
        if (IsNoServerOutput(result.Output))
            return TmuxServerStatus.NoServer();

        return new TmuxServerStatus(true, false, [], result.Output.Trim());
    }

    public async Task<bool> SessionExistsAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        if (!TmuxCommandBuilder.IsValidSessionName(sessionName))
            return false;

        var result = await runner.RunAsync(
            TmuxCommandBuilder.HasSessionCommand(sessionName), workingDirectory: null, ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public string ProjectSessionName(Project project)
    {
        if (project.TmuxSession is { Length: > 0 } explicitName && TmuxCommandBuilder.IsValidSessionName(explicitName))
            return explicitName;

        return Slugify(project.Name);
    }

    /// <summary>Lowercase slug: non-alphanumeric runs become '-', edges trimmed.</summary>
    internal static string Slugify(string name)
    {
        var slug = new StringBuilder(name.Length);
        var pendingDash = false;

        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-')
            {
                if (pendingDash && slug.Length > 0)
                    slug.Append('-');
                pendingDash = false;
                slug.Append(c);
            }
            else
            {
                pendingDash = true;
            }
        }

        return slug.Length > 0 ? slug.ToString() : "project";
    }

    public async Task<TmuxOperationResult> ExecuteAsync(
        TmuxOperation operation,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        if (operation.RequiresConfirmation && !confirmed)
        {
            return TmuxOperationResult.Blocked(operation,
                "This operation kills or renames a session and requires explicit confirmation.");
        }

        var result = await runner.RunAsync(
            operation.Command, workingDirectory: null, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        // tmux output is session bookkeeping text only — nothing secret-shaped
        // passes through here, so unlike Docker logs there is no redaction step.
        return new TmuxOperationResult(
            operation, result.ExitCode == 0, WasBlocked: false, result.ExitCode, result.Output);
    }

    /// <summary>tmux's own "nothing is running" messages across versions/platforms.</summary>
    private static bool IsNoServerOutput(string output) =>
        output.Contains("no server running", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("error connecting to", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("can't find socket", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase);
}
