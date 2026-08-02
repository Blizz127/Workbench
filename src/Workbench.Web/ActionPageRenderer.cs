using System.Text;

namespace Workbench.Web;

/// <summary>
/// Renders the mobile action pages: the Actions and Recent Actions sections
/// on the project detail page (static, outside every JS-polled container),
/// the one- and two-step confirmation pages with the exact command preview,
/// and the result page. Everything user- or config-sourced passes through
/// <see cref="WebPageShell.Encode"/>.
/// </summary>
public static class ActionPageRenderer
{
    // ---------- detail page sections (fragments, no page furniture) ----------

    /// <summary>Large tap-target buttons; read-only actions look different from mutating ones.</summary>
    public static void WriteActionsSection(
        StringBuilder html, long projectId, IReadOnlyList<MobileActionDescriptor> actions)
    {
        html.Append("<h2>Actions</h2><div class=\"card\">");
        if (actions.Count == 0)
            html.Append("<span class=\"muted\">No actions available for this project.</span>");

        foreach (var action in actions)
        {
            var css = "action-btn"
                + (action.IsMutating ? " mutating" : "")
                + (action.DisabledReason is not null ? " disabled" : "");

            if (action.DisabledReason is { } disabledReason)
            {
                html.Append("<div class=\"").Append(css).Append("\"><span class=\"grow\">")
                    .Append(WebPageShell.Encode(action.Label))
                    .Append("</span><span class=\"badge bad\">unavailable</span></div>")
                    .Append("<div class=\"muted action-reason\">")
                    .Append(WebPageShell.Encode(disabledReason)).Append("</div>");
                continue;
            }

            html.Append("<a class=\"").Append(css).Append("\" href=\"/projects/").Append(projectId)
                .Append("/actions/").Append(WebPageShell.Encode(action.Id)).Append("/confirm\">")
                .Append("<span class=\"grow\">").Append(WebPageShell.Encode(action.Label)).Append("</span>")
                .Append(SafetyBadge(action.Safety)).Append("</a>");
        }

        html.Append("</div>");
    }

    /// <summary>The in-memory action journal, newest first.</summary>
    public static void WriteRecentActionsSection(StringBuilder html, IReadOnlyList<ActionJournalEntry> entries)
    {
        html.Append("<h2>Recent Actions</h2><div class=\"card\">");
        if (entries.Count == 0)
        {
            html.Append("<span class=\"muted\">No actions attempted yet.</span></div>");
            return;
        }

        html.Append("<ul class=\"lined\">");
        foreach (var entry in entries)
        {
            html.Append("<li><span class=\"muted\">")
                .Append(WebPageShell.Encode(entry.Timestamp.ToString("MM-dd HH:mm:ss")))
                .Append(" · ").Append(WebPageShell.Encode(entry.ActionId)).Append("</span> ")
                .Append(OutcomeBadge(entry.Outcome));
            if (entry.Detail.Length > 0)
                html.Append("<br><span class=\"muted\">").Append(WebPageShell.Encode(entry.Detail)).Append("</span>");
            html.Append("</li>");
        }

        html.Append("</ul></div>");
    }

    // ---------- full pages ----------

    /// <summary>
    /// Step one: the exact command preview plus the safety badge. Read-only
    /// and single-confirmation actions post straight to execute; double-
    /// confirmation actions post to the second confirmation page. Blocked
    /// actions show the reason and no form at all.
    /// </summary>
    public static string RenderConfirmation(MobileActionPreview preview, string csrfToken, string nonce)
    {
        var html = new StringBuilder(capacity: 8 * 1024);
        WebPageShell.WriteHead(html, $"{preview.Action.Label} — Development Lab", "none", "");
        WritePageHeader(html, preview.ProjectId, preview.ProjectName, preview.Action.Label, preview.Safety);

        if (preview.BlockReason is { } blockReason)
        {
            html.Append("<div class=\"card\"><span class=\"badge warn\">not available</span>")
                .Append("<p>").Append(WebPageShell.Encode(blockReason)).Append("</p></div>");
            return Finish(html);
        }

        WriteCommandPreview(html, preview.CommandPreview);
        WriteHint(html, preview.Safety);

        var isDouble = preview.Safety is MobileActionSafety.RequiresDoubleConfirmation;
        WriteConfirmForm(html, preview.ProjectId, preview.Action.Id,
            isDouble ? "confirm2" : "execute", csrfToken, nonce,
            buttonText: isDouble ? "Continue" : preview.Safety is MobileActionSafety.ReadOnly ? "Run" : "Confirm",
            danger: false);
        return Finish(html);
    }

    /// <summary>Step two for requiresDoubleConfirmation actions, with a fresh nonce.</summary>
    public static string RenderSecondConfirmation(MobileActionPreview preview, string csrfToken, string nonce)
    {
        var html = new StringBuilder(capacity: 8 * 1024);
        WebPageShell.WriteHead(html, $"Are you sure? — Development Lab", "none", "");
        WritePageHeader(html, preview.ProjectId, preview.ProjectName, preview.Action.Label, preview.Safety);

        html.Append("<div class=\"card\"><span class=\"badge bad\">final step</span>")
            .Append("<p>This will restart containers / launch processes. Are you sure?</p></div>");
        WriteCommandPreview(html, preview.CommandPreview);
        WriteConfirmForm(html, preview.ProjectId, preview.Action.Id, "execute", csrfToken, nonce,
            buttonText: "Execute", danger: true);
        return Finish(html);
    }

    /// <summary>The outcome: badge, duration, exit code, redacted output.</summary>
    public static string RenderResult(MobileActionExecution execution, long projectId)
    {
        var html = new StringBuilder(capacity: 12 * 1024);
        WebPageShell.WriteHead(html, $"{execution.Action.Label} — Development Lab", "none", "");

        html.Append("<a class=\"back-link\" href=\"/projects/").Append(projectId).Append("\">← Back to project</a>");
        html.Append("<h1>").Append(WebPageShell.Encode(execution.Action.Label)).Append("</h1>");

        html.Append("<div class=\"card\">");
        if (execution.Blocked)
            html.Append("<span class=\"badge warn\">blocked</span>");
        else if (execution.Succeeded)
            html.Append("<span class=\"badge ok\">success</span>");
        else
            html.Append("<span class=\"badge bad\">failed</span>");

        if (!execution.Blocked)
        {
            html.Append("<div class=\"row\"><span class=\"muted\">duration</span><span class=\"mono grow\">")
                .Append(execution.Duration.TotalSeconds.ToString("0.#"))
                .Append("s</span></div>")
                .Append("<div class=\"row\"><span class=\"muted\">exit code</span><span class=\"mono grow\">")
                .Append(execution.ExitCode?.ToString() ?? "n/a").Append("</span></div>");
        }

        html.Append("</div>");

        html.Append("<h2>").Append(execution.Blocked ? "Reason" : "Output").Append("</h2>");
        html.Append("<pre class=\"output\">")
            .Append(WebPageShell.Encode(execution.Output.Length > 0 ? execution.Output : "(no output)"))
            .Append("</pre>");
        return Finish(html);
    }

    /// <summary>Refusal page for CSRF/nonce validation failures (served with 403).</summary>
    public static string RenderRejection(long projectId, string reason)
    {
        var html = new StringBuilder(capacity: 4 * 1024);
        WebPageShell.WriteHead(html, "Rejected — Development Lab", "none", "");
        html.Append("<a class=\"back-link\" href=\"/projects/").Append(projectId).Append("\">← Back to project</a>");
        html.Append("<div class=\"card\"><h1>Request rejected</h1>")
            .Append("<span class=\"badge bad\">rejected</span><p>")
            .Append(WebPageShell.Encode(reason)).Append("</p></div>");
        return Finish(html);
    }

    // ---------- pieces ----------

    private static void WritePageHeader(
        StringBuilder html, long projectId, string projectName, string actionLabel, MobileActionSafety safety)
    {
        html.Append("<a class=\"back-link\" href=\"/projects/").Append(projectId).Append("\">← ")
            .Append(WebPageShell.Encode(projectName)).Append("</a>");
        html.Append("<h1>").Append(WebPageShell.Encode(actionLabel)).Append("</h1>")
            .Append("<p>").Append(SafetyBadge(safety)).Append("</p>");
    }

    private static void WriteCommandPreview(StringBuilder html, string commandPreview)
    {
        html.Append("<h2>Exact command</h2>")
            .Append("<pre class=\"output\">").Append(WebPageShell.Encode(commandPreview)).Append("</pre>");
    }

    private static void WriteHint(StringBuilder html, MobileActionSafety safety)
    {
        var hint = safety switch
        {
            MobileActionSafety.ReadOnly => "This action only reads state — nothing on the server changes.",
            MobileActionSafety.RequiresConfirmation => "This action changes state on the server.",
            _ => "This action changes state on the server and asks twice.",
        };
        html.Append("<p class=\"muted\">").Append(hint).Append("</p>");
    }

    private static void WriteConfirmForm(
        StringBuilder html, long projectId, string actionId, string step,
        string csrfToken, string nonce, string buttonText, bool danger)
    {
        html.Append("<form method=\"post\" action=\"/projects/").Append(projectId)
            .Append("/actions/").Append(WebPageShell.Encode(actionId)).Append('/').Append(step).Append("\">")
            .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(WebPageShell.Encode(csrfToken)).Append("\">")
            .Append("<input type=\"hidden\" name=\"nonce\" value=\"").Append(WebPageShell.Encode(nonce)).Append("\">")
            .Append("<button class=\"action-submit").Append(danger ? " danger" : "").Append("\" type=\"submit\">")
            .Append(WebPageShell.Encode(buttonText)).Append("</button></form>");
    }

    private static string SafetyBadge(MobileActionSafety safety) => safety switch
    {
        MobileActionSafety.ReadOnly => "<span class=\"badge ok\">read-only</span>",
        MobileActionSafety.RequiresConfirmation => "<span class=\"badge warn\">confirmation</span>",
        _ => "<span class=\"badge bad\">double confirmation</span>",
    };

    private static string OutcomeBadge(ActionOutcome outcome) => outcome switch
    {
        ActionOutcome.Succeeded => "<span class=\"badge ok\">succeeded</span>",
        ActionOutcome.Failed => "<span class=\"badge bad\">failed</span>",
        ActionOutcome.Blocked => "<span class=\"badge warn\">blocked</span>",
        ActionOutcome.Rejected => "<span class=\"badge bad\">rejected</span>",
        _ => "<span class=\"badge\">attempted</span>",
    };

    private static string Finish(StringBuilder html)
    {
        html.Append("\n</body>\n</html>");
        return html.ToString();
    }
}
