using System.Security.Cryptography;
using System.Text;

namespace Workbench.Web;

/// <summary>
/// The dashboard's only access control: a shared-secret token on every
/// request, compared in constant time. Accepted as the
/// <c>X-Workbench-Token</c> header (API clients) or the <c>?token=</c> query
/// parameter (a phone bookmark over Tailscale — the query string stays inside
/// the Tailnet; note it can appear in browser history).
///
/// An empty configured token means "not set up": every route answers 503 so
/// a fresh install can never serve the lab unauthenticated.
/// </summary>
public sealed class TokenAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, WebDashboardOptions options)
    {
        if (options.AuthToken.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(
                "Workbench web dashboard is not configured. Set AuthToken (Web section or " +
                "WORKBENCH_WEB_AuthToken) before serving — refusing all requests until then.");
            return;
        }

        var presented = context.Request.Headers["X-Workbench-Token"].FirstOrDefault()
            ?? context.Request.Query["token"].FirstOrDefault();

        if (presented is null || !Matches(presented, options.AuthToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized.");
            return;
        }

        await next(context);
    }

    private static bool Matches(string presented, string expected)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return presentedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }
}
