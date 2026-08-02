using System.Text;
using Workbench.Core.Models;

namespace Workbench.Web;

/// <summary>
/// Renders the persistent mobile action audit pages: a newest-first compact
/// list with query-string filters (GET only, behind the same auth as every
/// other page) and a per-record detail view. Everything stored passes through
/// <see cref="WebPageShell.Encode"/> — the stored values are already
/// secret-redacted, but encoding is what makes them HTML-safe.
/// </summary>
public static class AuditPageRenderer
{
    /// <summary>Rows shown when no filter limit is set; a note appears when the cap bites.</summary>
    public const int ListLimit = 100;

    private static readonly (string Value, string Label)[] StateOptions =
    [
        ("attempted", "attempted"),
        ("blocked", "blocked"),
        ("rejected", "rejected"),
        ("running", "running"),
        ("succeeded", "succeeded"),
        ("failed", "failed"),
        ("cancelled", "cancelled"),
        ("timedOut", "timed out"),
    ];

    /// <summary>The newest-first list. Rows link to the per-record detail page.</summary>
    public static string RenderList(
        IReadOnlyList<MobileActionAuditRecord> records, MobileActionAuditFilter filter, bool capped)
    {
        var html = new StringBuilder(capacity: 24 * 1024);
        WebPageShell.WriteHead(html, "Action audit — Development Lab", "none", "");
        html.Append("<a class=\"back-link\" href=\"/\">← All projects</a>");
        html.Append("<h1>Action audit</h1>");

        // GET-only filter form: every control round-trips the current filter.
        html.Append("<form method=\"get\" action=\"/audit\" class=\"card audit-filters\">")
            .Append("<label>project id <input type=\"number\" name=\"project\" min=\"1\" value=\"")
            .Append(filter.ProjectId?.ToString() ?? string.Empty).Append("\"></label>")
            .Append("<label>action <input type=\"text\" name=\"action\" value=\"")
            .Append(WebPageShell.Encode(filter.ActionId)).Append("\"></label>")
            .Append("<label>state <select name=\"state\"><option value=\"\">any</option>");
        foreach (var (value, label) in StateOptions)
        {
            html.Append("<option value=\"").Append(value).Append("\"");
            if (filter.State is { } state && StateToQuery(state) == value)
                html.Append(" selected");
            html.Append('>').Append(label).Append("</option>");
        }

        html.Append("</select></label>")
            .Append("<label>from <input type=\"date\" name=\"from\" value=\"")
            .Append(filter.FromUtc?.UtcDateTime.ToString("yyyy-MM-dd") ?? string.Empty).Append("\"></label>")
            .Append("<label>to <input type=\"date\" name=\"to\" value=\"")
            .Append(filter.ToUtc?.UtcDateTime.ToString("yyyy-MM-dd") ?? string.Empty).Append("\"></label>")
            .Append("<button class=\"action-submit\" type=\"submit\">Filter</button></form>");

        if (records.Count == 0)
        {
            html.Append("<div class=\"card\"><span class=\"muted\">No audit records match.</span></div>");
            return Finish(html);
        }

        html.Append("<div class=\"card\"><ul class=\"lined\">");
        foreach (var record in records)
        {
            html.Append("<li><a class=\"commit-link\" href=\"/audit/").Append(record.Id).Append("\">")
                .Append("<span class=\"muted\">")
                .Append(WebPageShell.Encode(record.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")))
                .Append(" UTC · ").Append(WebPageShell.Encode(record.ProjectName))
                .Append("</span><br>")
                .Append("<span class=\"grow\">").Append(WebPageShell.Encode(record.ActionLabel)).Append("</span> ")
                .Append(StateBadge(record.State))
                .Append("</a></li>");
        }

        html.Append("</ul></div>");
        if (capped)
        {
            html.Append("<p class=\"muted\">Showing the newest ").Append(ListLimit)
                .Append(" records — narrow the filters to see further back.</p>");
        }

        return Finish(html);
    }

    /// <summary>Every stored field of one record, with a clear truncation indicator.</summary>
    public static string RenderDetail(MobileActionAuditRecord record)
    {
        var html = new StringBuilder(capacity: 12 * 1024);
        WebPageShell.WriteHead(html, $"{record.ActionLabel} — Action audit", "none", "");
        html.Append("<a class=\"back-link\" href=\"/audit\">← Action audit</a>");
        html.Append("<h1>").Append(WebPageShell.Encode(record.ActionLabel)).Append("</h1>");

        html.Append("<div class=\"card\">");
        WriteRow(html, "state", StateBadge(record.State));
        WriteRow(html, "time", WebPageShell.Encode(record.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")) + " UTC");
        WriteRow(html, "project", WebPageShell.Encode($"{record.ProjectName} (#{record.ProjectId})"));
        WriteRow(html, "action", WebPageShell.Encode(record.ActionId));
        WriteRow(html, "type", WebPageShell.Encode(record.ActionType));
        WriteRow(html, "safety", WebPageShell.Encode(record.Safety));
        WriteRow(html, "source", WebPageShell.Encode(record.Source));
        WriteRow(html, "correlation", WebPageShell.Encode(record.CorrelationId));
        WriteRow(html, "session", record.SessionHash is { } hash
            ? "<span class=\"mono\">" + WebPageShell.Encode(hash[..12]) + "…</span> <span class=\"muted\">(hashed)</span>"
            : "<span class=\"muted\">n/a</span>");
        if (record.DurationMs is { } durationMs)
            WriteRow(html, "duration", WebPageShell.Encode($"{durationMs / 1000.0:0.#}s"));
        if (record.ExitCode is { } exitCode)
            WriteRow(html, "exit code", WebPageShell.Encode(exitCode.ToString()));
        if (record.Services.Count > 0)
            WriteRow(html, "services", WebPageShell.Encode(string.Join(", ", record.Services)));
        html.Append("</div>");

        if (record.FailureReason is { } failureReason)
        {
            html.Append("<h2>Failure reason</h2><pre class=\"output\">")
                .Append(WebPageShell.Encode(failureReason)).Append("</pre>");
        }

        if (record.CommandPreview is { Length: > 0 } commandPreview)
        {
            html.Append("<h2>Command (redacted)</h2><pre class=\"output\">")
                .Append(WebPageShell.Encode(commandPreview)).Append("</pre>");
        }

        if (record.OutputSummary is { } outputSummary)
        {
            html.Append("<h2>Output</h2>");
            if (record.OutputTruncated)
            {
                html.Append("<p><span class=\"badge warn\">output truncated</span> ")
                    .Append("<span class=\"muted\">only the first ").Append(MobileActionAuditService.OutputSummaryLimit)
                    .Append(" characters were kept</span></p>");
            }

            html.Append("<pre class=\"output\">")
                .Append(WebPageShell.Encode(outputSummary.Length > 0 ? outputSummary : "(no output)"))
                .Append("</pre>");
        }

        return Finish(html);
    }

    /// <summary>Safe 404 page for unknown audit record ids.</summary>
    public static string RenderNotFound(long id)
    {
        var html = new StringBuilder(capacity: 4 * 1024);
        WebPageShell.WriteHead(html, "Not found — Development Lab", "none", "");
        html.Append("<a class=\"back-link\" href=\"/audit\">← Action audit</a>")
            .Append("<div class=\"card\"><h1>Audit record not found</h1>")
            .Append("<p class=\"muted\">No audit record with id ").Append(id)
            .Append(" exists. It may have been pruned by retention.</p>")
            .Append("</div>\n</body>\n</html>");
        return html.ToString();
    }

    private static void WriteRow(StringBuilder html, string label, string valueHtml) =>
        html.Append("<div class=\"row\"><span class=\"muted\">").Append(label)
            .Append("</span><span class=\"grow\">").Append(valueHtml).Append("</span></div>");

    /// <summary>The query-string vocabulary matches the stored one (lowercase, "timedOut").</summary>
    private static string StateToQuery(MobileActionAuditState state) =>
        state == MobileActionAuditState.TimedOut ? "timedOut" : state.ToString().ToLowerInvariant();

    private static string StateBadge(MobileActionAuditState state) => state switch
    {
        MobileActionAuditState.Succeeded => "<span class=\"badge ok\">succeeded</span>",
        MobileActionAuditState.Failed => "<span class=\"badge bad\">failed</span>",
        MobileActionAuditState.Blocked => "<span class=\"badge warn\">blocked</span>",
        MobileActionAuditState.Rejected => "<span class=\"badge bad\">rejected</span>",
        MobileActionAuditState.Cancelled => "<span class=\"badge warn\">cancelled</span>",
        MobileActionAuditState.TimedOut => "<span class=\"badge bad\">timed out</span>",
        MobileActionAuditState.Running => "<span class=\"badge warn\">running</span>",
        _ => "<span class=\"badge\">attempted</span>",
    };

    private static string Finish(StringBuilder html)
    {
        html.Append("\n</body>\n</html>");
        return html.ToString();
    }
}
