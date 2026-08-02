using System.Text;

namespace Workbench.Web;

/// <summary>
/// Renders the main dashboard: system metrics, tmux sessions, and compact
/// project cards that are themselves large tap targets into the detail pages.
/// The initial markup carries the same ids the refresh script updates, so the
/// first paint is complete even before the first poll.
/// </summary>
public static class DashboardPageRenderer
{
    public static string Render(WebDashboardSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 24 * 1024);
        WebPageShell.WriteHead(html, "Development Lab", "dashboard", "/api/snapshot");

        html.Append("<h1>Development Lab</h1>");
        WriteRefreshNote(html, snapshot.GeneratedAt);

        // Fleet summary: one compact text+count bar, same totals as the desktop.
        html.Append("<div class=\"summary\" id=\"summary-body\">");
        WriteSummaryRows(html, snapshot.Summary);
        html.Append("</div>");

        // System
        html.Append("<h2>System</h2><div class=\"card metrics\">");
        WriteMetric(html, "CPU", "sys-cpu-v", snapshot.System.CpuText, "sys-cpu-b", snapshot.System.CpuPercent);
        WriteMetric(html, "RAM", "sys-ram-v", snapshot.System.MemoryText, "sys-ram-b", snapshot.System.MemoryPercent);
        WriteMetric(html, "Disk", "sys-disk-v", snapshot.System.DiskText, "sys-disk-b", snapshot.System.DiskPercent);
        html.Append("<div class=\"metric\"><div class=\"muted\">Uptime</div><div class=\"value\" id=\"sys-uptime-v\">")
            .Append(WebPageShell.Encode(snapshot.System.UptimeText)).Append("</div></div>");
        html.Append("</div>");

        // tmux sessions
        html.Append("<h2>Sessions</h2><div class=\"card\" id=\"tmux-body\">");
        WriteTmuxRows(html, snapshot.Tmux);
        html.Append("</div>");

        // Projects. The filter control is static markup OUTSIDE #projects-body
        // so the polling re-render never wipes it; the script re-applies the
        // chosen filter after every render.
        html.Append("""
            <h2>Projects</h2><details class="filters"><summary>Filters</summary>
            <div class="filter-buttons"><button type="button" class="filter active" data-filter="all" aria-pressed="true">All</button><button type="button" class="filter" data-filter="attention" aria-pressed="false">Needs attention</button><button type="button" class="filter" data-filter="blocked" aria-pressed="false">Blocked</button><button type="button" class="filter" data-filter="dirty" aria-pressed="false">Dirty</button></div>
            </details><div id="projects-body">
            """);
        if (snapshot.Projects.Count == 0)
            html.Append("<div class=\"card muted\">No projects registered.</div>");
        foreach (var project in snapshot.Projects)
            WriteProjectCard(html, project);
        html.Append("</div>");

        WebPageShell.WriteScript(html);
        return html.ToString();
    }

    internal static void WriteRefreshNote(StringBuilder html, DateTimeOffset generatedAt)
    {
        html.Append("<p class=\"muted\">updated <span id=\"last-refresh\">")
            .Append(generatedAt.ToString("HH:mm:ss"))
            .Append("</span> · read-only <span class=\"badge warn\" id=\"stale\" hidden>stale — retrying</span></p>");
    }

    internal static void WriteSummaryRows(StringBuilder html, WebSummaryBlock summary)
    {
        WriteSummaryItem(html, "projects", summary.TotalProjects, null);
        WriteSummaryItem(html, "healthy", summary.HealthyCount, "ok");
        WriteSummaryItem(html, "attention", summary.AttentionCount, summary.AttentionCount > 0 ? "warn" : null);
        WriteSummaryItem(html, "blocked", summary.BlockedCount, summary.BlockedCount > 0 ? "bad" : null);
        WriteSummaryItem(html, "failing", summary.FailingCount, summary.FailingCount > 0 ? "bad" : null);
        WriteSummaryItem(html, "unavailable", summary.UnavailableCount, summary.UnavailableCount > 0 ? "bad" : null);
        WriteSummaryItem(html, "blockers", summary.ActiveBlockerCount, summary.ActiveBlockerCount > 0 ? "warn" : null);
        WriteSummaryItem(html, "running", summary.RunningServices, null);
        WriteSummaryItem(html, "failing services", summary.FailingServices, summary.FailingServices > 0 ? "bad" : null);
        WriteSummaryItem(html, "dirty", summary.DirtyProjects, summary.DirtyProjects > 0 ? "warn" : null);
    }

    private static void WriteSummaryItem(StringBuilder html, string label, int count, string? badgeClass)
    {
        // The label itself carries the badge — color is never the only signal.
        html.Append("<span class=\"summary-item\"><strong>").Append(count).Append("</strong> ");
        if (badgeClass is not null)
            html.Append("<span class=\"badge ").Append(badgeClass).Append("\">").Append(label).Append("</span>");
        else
            html.Append(label);
        html.Append("</span>");
    }

    private static void WriteMetric(
        StringBuilder html, string label, string valueId, string value, string barId, double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        html.Append("<div class=\"metric\"><div class=\"muted\">").Append(label)
            .Append("</div><div class=\"value\" id=\"").Append(valueId).Append("\">")
            .Append(WebPageShell.Encode(value))
            .Append("</div><div class=\"bar\"><div id=\"").Append(barId).Append("\" style=\"width:")
            .Append(clamped.ToString("0", System.Globalization.CultureInfo.InvariantCulture))
            .Append("%\"></div></div></div>");
    }

    private static void WriteTmuxRows(StringBuilder html, TmuxBlock tmux)
    {
        if (!tmux.TmuxAvailable)
        {
            html.Append("<span class=\"muted\">")
                .Append(WebPageShell.Encode(tmux.Error ?? "tmux is not available.")).Append("</span>");
            return;
        }

        if (!tmux.ServerRunning)
        {
            html.Append("<span class=\"muted\">")
                .Append(WebPageShell.Encode(tmux.Error ?? "No tmux server running — no sessions.")).Append("</span>");
            return;
        }

        if (tmux.Sessions.Count == 0)
        {
            html.Append("<span class=\"muted\">tmux server running, no sessions.</span>");
            return;
        }

        foreach (var session in tmux.Sessions)
        {
            html.Append("<div class=\"row\"><span class=\"mono grow\">").Append(WebPageShell.Encode(session.Name))
                .Append("</span><span class=\"badge ").Append(session.IsAttached ? "ok" : "warn")
                .Append("\">").Append(session.IsAttached ? "attached" : "detached").Append("</span>")
                .Append("<span class=\"muted\">").Append(session.WindowCount)
                .Append(session.WindowCount == 1 ? " window" : " windows");
            if (session.ActiveWindowName is { } window)
                html.Append(" · ").Append(WebPageShell.Encode(window));
            html.Append("</span></div>");
        }
    }

    private static void WriteProjectCard(StringBuilder html, WebProjectCard project)
    {
        // The card is a div so the footer can hold both the "View project"
        // tap target and the allowlist-gated "Actions" link. data-health /
        // data-dirty feed the filter control (kept in sync with the JS).
        html.Append("<div class=\"card project-card\" data-health=\"").Append(WebPageShell.Encode(project.HealthText))
            .Append("\" data-dirty=\"").Append(project.GitDirty ? "true" : "false")
            .Append("\"><div class=\"row card-head\"><strong class=\"grow\">").Append(WebPageShell.Encode(project.Name))
            .Append("</strong><span class=\"badge").Append(project.HealthBadgeClass.Length > 0 ? " " + project.HealthBadgeClass : string.Empty)
            .Append("\" title=\"").Append(WebPageShell.Encode(project.HealthReasonsText)).Append("\">")
            .Append(WebPageShell.Encode(project.HealthText)).Append("</span>")
            .Append("<span class=\"badge\">").Append(WebPageShell.Encode(project.Status)).Append("</span></div>");

        // Progress: a null projection means "not configured", never 0%.
        if (project.ProgressText is not null)
        {
            html.Append("<div class=\"row\"><span class=\"muted\">progress</span><div class=\"grow\">")
                .Append("<div class=\"bar\"><div style=\"width:")
                .Append(Math.Clamp(project.ProgressPercent, 0, 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture))
                .Append("%\"></div></div></div><span class=\"muted\">")
                .Append(WebPageShell.Encode(project.ProgressText)).Append("</span></div>");
        }
        else
        {
            html.Append("<div class=\"row\"><span class=\"muted\">progress</span><span class=\"muted grow\">")
                .Append("Progress not configured").Append("</span></div>");
        }

        html.Append("<div class=\"row\"><span class=\"muted\">frontier</span><span class=\"")
            .Append(project.FrontierText is null ? "muted grow" : "mono grow").Append("\">")
            .Append(WebPageShell.Encode(project.FrontierText ?? "no frontier set")).Append("</span></div>");

        html.Append("<div class=\"row\"><span class=\"muted\">blockers</span><span class=\"grow\">")
            .Append(project.BlockerText is null
                ? "<span class=\"muted\">no active blockers</span>"
                : "<span class=\"badge bad\">" + WebPageShell.Encode(project.BlockerText) + "</span>")
            .Append("</span></div>");

        html.Append("<div class=\"row\"><span class=\"muted\">git</span><span class=\"mono grow\">")
            .Append(WebPageShell.Encode(project.GitText)).Append("</span>");
        if (project.GitDirty)
            html.Append("<span class=\"badge warn\">dirty</span>");
        if (project.GitUnavailable)
            html.Append("<span class=\"badge bad\">repo missing</span>");
        html.Append("</div>");

        if (project.ResultsText is not null)
            html.Append("<div class=\"row\"><span class=\"muted\">results</span><span class=\"mono grow\">")
                .Append(WebPageShell.Encode(project.ResultsText)).Append("</span></div>");
        else
            html.Append("<div class=\"row\"><span class=\"muted\">results</span><span class=\"muted grow\">")
                .Append("no build/test history").Append("</span></div>");

        if (project.DockerSummary is not null)
            html.Append("<div class=\"row\"><span class=\"muted\">docker</span><span class=\"mono grow\">")
                .Append(WebPageShell.Encode(project.DockerSummary)).Append("</span></div>");

        if (project.TmuxText is not null)
            html.Append("<div class=\"row\"><span class=\"muted\">tmux</span><span class=\"mono grow\">")
                .Append(WebPageShell.Encode(project.TmuxText)).Append("</span></div>");

        if (project.LastActivityText is not null)
            html.Append("<div class=\"row\"><span class=\"muted\">activity</span><span class=\"muted grow\">last ")
                .Append(WebPageShell.Encode(project.LastActivityText)).Append("</span></div>");

        html.Append("<div class=\"row\"><a class=\"view-link\" href=\"/projects/").Append(project.Id)
            .Append("\">View project →</a>");
        if (project.HasMobileActions)
            html.Append("<a class=\"view-link\" href=\"/projects/").Append(project.Id).Append("#actions\">Actions →</a>");
        html.Append("</div></div>");
    }
}
