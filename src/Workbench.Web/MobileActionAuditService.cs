using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Web;

/// <summary>
/// The persistent audit trail for mobile actions — the ONLY writer and
/// reader of <see cref="IMobileActionAuditRepository"/> in the web layer.
/// Every state transition (attempted / blocked / rejected / terminal) is
/// appended as its own row, transitions of one execution linked by the
/// correlation id carried through the confirmation nonces.
///
/// Safety invariants:
/// - Command previews and output are secret-redacted BEFORE storage; output
///   is capped at <see cref="OutputSummaryLimit"/> chars with the truncation
///   flag set on the row (the state itself is unaffected by truncation).
/// - The session is stored only as a SHA-256 hex hash — never the raw
///   session id, and never the auth token, CSRF token, or nonce.
/// - Persistence failures are swallowed with a warning and counted on
///   <see cref="WriteFailures"/>; an audit failure must never re-execute,
///   block, or fail the action itself.
///
/// The in-memory <see cref="ActionJournal"/> stays as the cache behind the
/// detail page's "Recent Actions"; this repository is the source of truth.
/// </summary>
public sealed class MobileActionAuditService(
    IMobileActionAuditRepository repository,
    ILogger<MobileActionAuditService> logger)
{
    /// <summary>Max characters of redacted output kept on a terminal record.</summary>
    public const int OutputSummaryLimit = 4000;

    /// <summary>Max characters of the failure-reason tail on a terminal record.</summary>
    private const int FailureReasonLimit = 300;

    private int _writeFailures;

    /// <summary>Number of audit writes that failed since startup (observability).</summary>
    public int WriteFailures => _writeFailures;

    /// <summary>SHA-256 hex of a session id — the only session fingerprint that may be stored.</summary>
    public static string HashSessionId(string sessionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId))).ToLowerInvariant();

    /// <summary>A fresh correlation id for executions without a nonce context.</summary>
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    // ---------- writers (called from MobileActionService only) ----------

    /// <summary>Execution is starting: redacted command preview, safety, affected services.</summary>
    public void RecordAttempted(
        MobileActionAuditContext? context, MobileActionPreview preview,
        IReadOnlyList<string> services, string correlationId)
    {
        var record = BaseRecord(context, preview, services, correlationId, MobileActionAuditState.Attempted);
        record.CommandPreview = SecretRedactor.Redact(preview.CommandPreview);
        Write(record);
    }

    /// <summary>The action was refused — blocked (guard/lock/unconfigured) or rejected (not allowlisted).</summary>
    public void RecordRefused(
        MobileActionAuditContext? context, MobileActionPreview preview, MobileActionAuditState state,
        string reason, IReadOnlyList<string> services, string correlationId)
    {
        if (state is not (MobileActionAuditState.Blocked or MobileActionAuditState.Rejected))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Refusals are blocked or rejected.");

        var record = BaseRecord(context, preview, services, correlationId, state);
        record.FailureReason = SecretRedactor.Redact(reason);
        Write(record);
    }

    /// <summary>
    /// The terminal record: succeeded/failed from the execution result, or
    /// cancelled/timedOut via <paramref name="stateOverride"/> when the caller
    /// (not the typed services, which don't surface it) knows the run ended
    /// that way. Output is redacted then truncated to
    /// <see cref="OutputSummaryLimit"/>; the stored state is correct either way.
    /// </summary>
    public void RecordTerminal(
        MobileActionAuditContext? context, MobileActionPreview preview, MobileActionExecution execution,
        IReadOnlyList<string> services, string correlationId, MobileActionAuditState? stateOverride = null)
    {
        var state = stateOverride
            ?? (execution.Succeeded ? MobileActionAuditState.Succeeded : MobileActionAuditState.Failed);
        var redacted = SecretRedactor.Redact(execution.Output);
        var truncated = redacted.Length > OutputSummaryLimit;

        var record = BaseRecord(context, preview, services, correlationId, state);
        record.OutputSummary = truncated ? redacted[..OutputSummaryLimit] : redacted;
        record.OutputTruncated = truncated;
        record.ExitCode = execution.ExitCode;
        record.DurationMs = (long)execution.Duration.TotalMilliseconds;
        record.FailureReason = state is MobileActionAuditState.Succeeded ? null : Tail(redacted);
        Write(record);
    }

    // ---------- readers (routes/pages) ----------

    /// <summary>Newest first; read failures degrade to an empty page, never a 500.</summary>
    public IReadOnlyList<MobileActionAuditRecord> Query(MobileActionAuditFilter filter)
    {
        try
        {
            return repository.Query(filter);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading the mobile action audit failed — serving an empty result.");
            return [];
        }
    }

    /// <summary>Read failures degrade to a 404-style page, never a 500.</summary>
    public MobileActionAuditRecord? GetById(long id)
    {
        try
        {
            return repository.GetById(id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading mobile action audit record {Id} failed.", id);
            return null;
        }
    }

    /// <summary>
    /// Startup-only retention sweep (see the repository contract for the exact
    /// keep semantics). Never called per-write or during an action. A failure
    /// is logged and reported as zero deletions — retention must not take the
    /// host down.
    /// </summary>
    public int EnforceRetention(int maxRecords, int maxAgeDays)
    {
        try
        {
            var deleted = repository.EnforceRetention(maxRecords, maxAgeDays);
            if (deleted > 0)
            {
                logger.LogInformation(
                    "Mobile action audit retention deleted {Deleted} record(s) (caps: {MaxRecords} records, {MaxAgeDays} days).",
                    deleted, maxRecords, maxAgeDays);
            }
            return deleted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mobile action audit retention failed — history is left untouched.");
            return 0;
        }
    }

    // ---------- internals ----------

    private MobileActionAuditRecord BaseRecord(
        MobileActionAuditContext? context, MobileActionPreview preview,
        IReadOnlyList<string> services, string correlationId, MobileActionAuditState state) =>
        new()
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ProjectId = preview.ProjectId,
            ProjectName = preview.ProjectName,
            ActionId = preview.Action.Id,
            ActionLabel = preview.Action.Label,
            ActionType = preview.Action.Type,
            Safety = SafetyToDb(preview.Safety),
            State = state,
            SessionHash = context?.SessionId is { Length: > 0 } sessionId ? HashSessionId(sessionId) : null,
            Services = services,
            CorrelationId = correlationId,
        };

    private void Write(MobileActionAuditRecord record)
    {
        try
        {
            repository.Add(record);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeFailures);
            logger.LogWarning(ex,
                "Failed to persist a mobile action audit record (action {ActionId}, state {State}) — the action itself is unaffected.",
                record.ActionId, record.State);
        }
    }

    /// <summary>The workbench.json safety vocabulary, camelCase.</summary>
    private static string SafetyToDb(MobileActionSafety safety) => safety switch
    {
        MobileActionSafety.ReadOnly => "readOnly",
        MobileActionSafety.RequiresConfirmation => "requiresConfirmation",
        _ => "requiresDoubleConfirmation",
    };

    private static string Tail(string output) =>
        output.Length <= FailureReasonLimit ? output : "…" + output[^FailureReasonLimit..];
}
