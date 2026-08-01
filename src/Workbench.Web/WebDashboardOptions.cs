using System.Net;

namespace Workbench.Web;

/// <summary>
/// Web dashboard configuration, bound from the <c>Web</c> section of
/// appsettings.json and overridden by <c>WORKBENCH_WEB_*</c> environment
/// variables (<c>WORKBENCH_WEB_BindAddress</c>, <c>WORKBENCH_WEB_Port</c>,
/// <c>WORKBENCH_WEB_AuthToken</c>, <c>WORKBENCH_WEB_DatabasePath</c>).
/// The defaults listen on loopback only; the token stays empty (dashboard
/// refuses to serve) until the operator sets one — nothing secret is ever
/// committed.
/// </summary>
public sealed class WebDashboardOptions
{
    /// <summary>Address Kestrel binds to. Loopback by default; a Tailscale
    /// interface address (100.64.0.0/10) for phone access.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>Port Kestrel binds to.</summary>
    public int Port { get; set; } = 8471;

    /// <summary>
    /// Shared-secret token required on every request (X-Workbench-Token header
    /// or ?token= query). Empty means "not configured" — every route answers
    /// 503 rather than serving anything unauthenticated.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>SQLite database override; empty uses the standard Workbench path.</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Lifetime of a browser session issued by the token exchange. After
    /// expiry the phone re-authenticates via its bookmarked token URL.
    /// </summary>
    public int SessionLifetimeMinutes { get; set; } = 480;

    public static WebDashboardOptions Load(IConfiguration configuration)
    {
        var options = new WebDashboardOptions();
        configuration.GetSection("Web").Bind(options);
        // Prefixed environment variables land at the configuration root
        // (AddEnvironmentVariables("WORKBENCH_WEB_")) and win over the file.
        configuration.Bind(options);
        return options;
    }

    /// <summary>
    /// Whether the bind address is a non-public address: loopback, RFC 1918,
    /// link-local, or the Tailscale CGNAT range (100.64.0.0/10). Anything else
    /// means the dashboard would be reachable from the public internet.
    /// </summary>
    public bool BindAddressIsPrivate
    {
        get
        {
            if (BindAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!IPAddress.TryParse(BindAddress, out var address))
                return false;

            if (IPAddress.IsLoopback(address))
                return true;

            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return false; // IPv6 other than ::1: require an explicit, informed choice.

            return bytes[0] switch
            {
                10 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                100 when bytes[1] is >= 64 and <= 127 => true, // Tailscale CGNAT
                _ => false,
            };
        }
    }
}
