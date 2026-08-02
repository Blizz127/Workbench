using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Workbench.Web;

/// <summary>
/// One-time confirmation nonces for the mobile action flow. A nonce is bound
/// to (projectId, actionId, purpose) — the purpose separates the first
/// confirmation step from the final execute step, so a double-confirmation
/// action can never be executed with its step-one nonce. Nonces expire after
/// a short TTL (2 minutes by default) and are consumed on use: replaying one
/// fails.
///
/// Each nonce also carries a correlation id (a fresh Guid at issue) so the
/// persistent audit trail can link every state transition of one execution;
/// the double-confirmation flow hands the step-one correlation id forward
/// into the execute nonce.
/// </summary>
public sealed class ConfirmationNonceService
{
    /// <summary>First confirmation page → second confirmation page.</summary>
    public const string PurposeConfirm2 = "confirm2";

    /// <summary>Final confirmation → execute.</summary>
    public const string PurposeExecute = "execute";

    private sealed record Entry(
        long ProjectId, string ActionId, string Purpose, DateTimeOffset Expires, string CorrelationId);

    private readonly ConcurrentDictionary<string, Entry> _nonces = new();
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;

    public ConfirmationNonceService() : this(TimeSpan.FromMinutes(2))
    {
    }

    public ConfirmationNonceService(TimeSpan ttl, Func<DateTimeOffset>? now = null)
    {
        _ttl = ttl;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Issues a nonce. <paramref name="correlationId"/> carries an existing
    /// execution's correlation id forward (confirm2 → execute); null mints a
    /// fresh one.
    /// </summary>
    public string Issue(long projectId, string actionId, string purpose, string? correlationId = null)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _nonces[nonce] = new Entry(
            projectId, actionId, purpose, _now().Add(_ttl), correlationId ?? NewCorrelationId());
        PurgeExpired();
        return nonce;
    }

    /// <summary>Consumes the nonce: true exactly once, only for the binding it was issued for.</summary>
    public bool Validate(long projectId, string actionId, string purpose, string? nonce) =>
        TryValidate(projectId, actionId, purpose, nonce, out _);

    /// <summary>
    /// Like <see cref="Validate"/>, plus the correlation id the nonce was
    /// issued with (null when validation fails).
    /// </summary>
    public bool TryValidate(
        long projectId, string actionId, string purpose, string? nonce, out string? correlationId)
    {
        correlationId = null;
        if (nonce is null || !_nonces.TryRemove(nonce, out var entry))
            return false;

        if (entry.ProjectId != projectId
            || entry.ActionId != actionId
            || entry.Purpose != purpose
            || _now() >= entry.Expires)
            return false;

        correlationId = entry.CorrelationId;
        return true;
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    private void PurgeExpired()
    {
        var now = _now();
        foreach (var (nonce, entry) in _nonces)
            if (entry.Expires <= now)
                _nonces.TryRemove(nonce, out _);
    }
}
