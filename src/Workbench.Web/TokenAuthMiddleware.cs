using System.Security.Cryptography;
using System.Text;

namespace Workbench.Web;

/// <summary>
/// The dashboard's only access control. Three ways in, checked in order:
///
/// 1. <c>X-Workbench-Token</c> header — API clients and tests; the token is
///    compared in constant time on every request.
/// 2. <c>?token=</c> query on a GET page load — the phone's bookmark. A valid
///    token is exchanged once for a random session id in an HttpOnly,
///    SameSite=Strict cookie (Secure only under HTTPS), then the request is
///    redirected to the same URL without the token so it does not linger in
///    browser history or screenshots. Non-GET requests with a valid query
///    token pass through untouched (routing answers 405 — there are no
///    mutation endpoints).
/// 3. the <c>dl_session</c> cookie issued by that exchange.
///
/// An empty configured token means "not set up": every route answers 503 so
/// a fresh install can never serve the lab unauthenticated.
/// </summary>
public sealed class TokenAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, WebDashboardOptions options, SessionStore sessions)
    {
        if (options.AuthToken.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(
                "Workbench web dashboard is not configured. Set AuthToken (Web section or " +
                "WORKBENCH_WEB_AuthToken) before serving — refusing all requests until then.");
            return;
        }

        var headerToken = context.Request.Headers["X-Workbench-Token"].FirstOrDefault();
        if (Matches(headerToken, options.AuthToken))
        {
            await next(context);
            return;
        }

        var queryToken = context.Request.Query["token"].FirstOrDefault();
        if (Matches(queryToken, options.AuthToken))
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                // A valid token on a mutation verb still finds no endpoint — let routing 405 it.
                await next(context);
                return;
            }

            context.Response.Cookies.Append(SessionStore.CookieName, sessions.Issue(), new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = "/",
                Expires = sessions.Expiry(),
            });
            context.Response.Redirect(CleanUrl(context.Request));
            return;
        }

        if (sessions.Validate(context.Request.Cookies[SessionStore.CookieName]))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized.");
    }

    /// <summary>The request URL minus the token parameter — where the exchange redirects to.</summary>
    private static string CleanUrl(HttpRequest request)
    {
        var query = QueryString.Empty;
        foreach (var (key, values) in request.Query)
        {
            if (key.Equals("token", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var value in values)
                query = query.Add(key, value ?? string.Empty);
        }

        return request.PathBase + request.Path + query;
    }

    private static bool Matches(string? presented, string expected)
    {
        if (presented is null)
            return false;

        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return presentedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }
}
