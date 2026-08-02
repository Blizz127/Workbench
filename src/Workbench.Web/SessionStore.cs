using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Workbench.Web;

/// <summary>
/// Server-side session store backing the token exchange: a valid auth token
/// is traded once for a random session id held in an HttpOnly cookie, so the
/// token does not linger in browser history or screenshots. Sessions are
/// in-memory only — a service restart simply asks the phone to re-open its
/// bookmark. Nothing client-side ever sees the token again, and the session
/// id itself carries no information.
///
/// Each session also owns a random CSRF token. Page renderers embed it in
/// action forms as a hidden field; POST handlers compare it against the
/// session's token before anything else runs, so a cross-site form (which
/// cannot read the token) is rejected outright.
/// </summary>
public sealed class SessionStore(WebDashboardOptions options)
{
    private sealed record SessionInfo(DateTimeOffset Expires, string CsrfToken);

    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

    public const string CookieName = "dl_session";

    public string Issue()
    {
        var id = NewId();
        _sessions[id] = new SessionInfo(Expiry(), NewId());
        PurgeExpired();
        return id;
    }

    /// <summary>
    /// A stable session for requests authenticated by the
    /// <c>X-Workbench-Token</c> header (API clients, tests) instead of a
    /// cookie. The id is a keyed hash of the token — deterministic so every
    /// request with the same header shares one session (and one CSRF token),
    /// and irreversible so it leaks nothing about the token itself.
    /// </summary>
    public string IssueForHeaderToken(string headerToken)
    {
        var id = "hdr-" + Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("workbench-web-header-session"),
                Encoding.UTF8.GetBytes(headerToken))).ToLowerInvariant();

        // Refresh the expiry on each use, keep the existing CSRF token.
        _sessions.AddOrUpdate(
            id,
            _ => new SessionInfo(Expiry(), NewId()),
            (_, existing) => existing with { Expires = Expiry() });
        PurgeExpired();
        return id;
    }

    public bool Validate(string? id)
    {
        if (id is null || !_sessions.TryGetValue(id, out var session))
            return false;

        if (DateTimeOffset.UtcNow < session.Expires)
            return true;

        _sessions.TryRemove(id, out _);
        return false;
    }

    /// <summary>The session's current CSRF token, or null for an unknown/expired session.</summary>
    public string? GetCsrfToken(string? id) =>
        id is not null && Validate(id) && _sessions.TryGetValue(id, out var session)
            ? session.CsrfToken
            : null;

    /// <summary>Rotates the session's CSRF token; null when the session is unknown/expired.</summary>
    public string? RegenerateCsrfToken(string? id)
    {
        if (id is null || !Validate(id))
            return null;

        var token = NewId();
        _sessions.AddOrUpdate(
            id,
            _ => new SessionInfo(Expiry(), token),
            (_, existing) => existing with { CsrfToken = token });
        return token;
    }

    /// <summary>Constant-time CSRF comparison for a presented form token.</summary>
    public bool ValidateCsrf(string? id, string? presented)
    {
        var expected = GetCsrfToken(id);
        if (expected is null || presented is null)
            return false;

        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return presentedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }

    public DateTimeOffset Expiry() =>
        DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.SessionLifetimeMinutes));

    private static string NewId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, session) in _sessions)
            if (session.Expires <= now)
                _sessions.TryRemove(id, out _);
    }
}
