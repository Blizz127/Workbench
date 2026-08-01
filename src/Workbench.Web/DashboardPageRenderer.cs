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

        // Projects
        html.Append("<h2>Projects</h2><div id=\"projects-body\">");
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
        html.Append("<a class=\"card project-card\" href=\"/projects/").Append(project.Id).Append("\">")
            .Append("<div class=\"row\"><strong class=\"grow\">").Append(WebPageShell.Encode(project.Name))
            .Append("</strong><span class=\"badge\">").Append(WebPageShell.Encode(project.Status)).Append("</span></div>");

        html.Append("<div class=\"row\"><span class=\"muted\">git</span><span class=\"mono grow\">")
            .Append(WebPageShell.Encode(project.GitText)).Append("</span>");
        if (project.GitDirty)
            html.Append("<span class=\"badge warn\">dirty</span>");
        if (project.GitUnavailable)
            html.Append("<span class=\"badge bad\">repo missing</span>");
        html.Append("</div>");

        if (project.DockerSummary is not null)
            html.Append("<div class=\"row\"><span class=\"muted\">docker</span><span class=\"mono grow\">")
                .Append(WebPageShell.Encode(project.DockerSummary)).Append("</span></div>");

        if (project.TmuxText is not null)
            html.Append("<div class=\"row\"><span class=\"muted\">tmux</span><span class=\"mono grow\">")
                .Append(WebPageShell.Encode(project.TmuxText)).Append("</span></div>");

        html.Append("<div class=\"row\"><span class=\"view-link\">View project →</span></div></a>");
    }
}
