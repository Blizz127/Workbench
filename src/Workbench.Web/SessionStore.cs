using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Workbench.Web;

/// <summary>
/// Server-side session store backing the token exchange: a valid auth token
/// is traded once for a random session id held in an HttpOnly cookie, so the
/// token does not linger in browser history or screenshots. Sessions are
/// in-memory only — a service restart simply asks the phone to re-open its
/// bookmark. Nothing client-side ever sees the token again, and the session
/// id itself carries no information.
/// </summary>
public sealed class SessionStore(WebDashboardOptions options)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();

    public const string CookieName = "dl_session";

    public string Issue()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _sessions[id] = Expiry();
        PurgeExpired();
        return id;
    }

    public bool Validate(string? id)
    {
        if (id is null || !_sessions.TryGetValue(id, out var expires))
            return false;

        if (DateTimeOffset.UtcNow < expires)
            return true;

        _sessions.TryRemove(id, out _);
        return false;
    }

    public DateTimeOffset Expiry() =>
        DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.SessionLifetimeMinutes));

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, expires) in _sessions)
            if (expires <= now)
                _sessions.TryRemove(id, out _);
    }
}
