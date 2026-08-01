using System.Net;
using System.Text;

namespace Workbench.Web;

/// <summary>
/// Renders the dashboard snapshot as a single self-contained HTML page:
/// dark, phone-first, no JavaScript, no external assets. Service lists sit
/// behind <c>&lt;details&gt;</c> so every interactive element is a large tap
/// target. All dynamic text is HTML-encoded.
/// </summary>
public static class DashboardPageRenderer
{
    public static string Render(WebDashboardSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 16 * 1024);
        html.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
            <meta name="color-scheme" content="dark">
            <title>Workbench — Development Lab</title>
            <style>
            :root { color-scheme: dark; }
            * { box-sizing: border-box; margin: 0; }
            body { background: #0f1115; color: #e6e8ee; font-family: system-ui, -apple-system, sans-serif;
                   font-size: 16px; line-height: 1.45; padding: 12px; max-width: 640px; margin: 0 auto; }
            h1 { font-size: 1.25rem; margin: 8px 0 2px; }
            h2 { font-size: 1.0rem; color: #9aa3b2; margin-bottom: 8px; text-transform: uppercase;
                 letter-spacing: .05em; font-weight: 600; }
            .muted { color: #9aa3b2; font-size: .85rem; }
            .card { background: #1a1d24; border: 1px solid #2a2e38; border-radius: 12px;
                    padding: 14px; margin: 12px 0; }
            .metrics { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
            .metric { background: #12141a; border-radius: 8px; padding: 10px 12px; }
            .metric .value { font-size: 1.1rem; font-weight: 600; font-variant-numeric: tabular-nums; }
            .bar { height: 4px; background: #2a2e38; border-radius: 2px; margin-top: 6px; overflow: hidden; }
            .bar > div { height: 100%; background: #4f8ff7; border-radius: 2px; }
            .row { display: flex; align-items: center; gap: 8px; min-height: 44px; }
            .grow { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
            .badge { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: .75rem;
                     background: #2a2e38; color: #9aa3b2; white-space: nowrap; }
            .badge.ok { background: #14361f; color: #5fd68a; }
            .badge.warn { background: #3a2c12; color: #f0b45c; }
            .badge.bad { background: #3d1a1a; color: #f47c7c; }
            .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem; }
            details { margin-top: 8px; }
            summary { min-height: 44px; display: flex; align-items: center; cursor: pointer;
                      color: #9aa3b2; font-size: .9rem; list-style: none; }
            summary::before { content: "▸"; margin-right: 8px; transition: transform .15s; }
            details[open] summary::before { transform: rotate(90deg); }
            .services { border-top: 1px solid #2a2e38; padding-top: 4px; }
            .activity { border-top: 1px solid #2a2e38; margin-top: 8px; padding-top: 4px; }
            .activity li { list-style: none; padding: 6px 0; border-bottom: 1px solid #22262f; }
            .activity li:last-child { border-bottom: 0; }
            </style>
            </head>
            <body>
            """);

        html.Append("<h1>Development Lab</h1>");
        html.Append("<p class=\"muted\">").Append(Encode(snapshot.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")))
            .Append(" · read-only</p>");

        RenderSystem(html, snapshot.System);
        RenderTmux(html, snapshot.Tmux);

        html.Append("<h2>Projects</h2>");
        if (snapshot.Projects.Count == 0)
            html.Append("<div class=\"card muted\">No projects registered.</div>");

        foreach (var project in snapshot.Projects)
            RenderProject(html, project);

        html.Append("</body>\n</html>");
        return html.ToString();
    }

    private static void RenderSystem(StringBuilder html, SystemHealthBlock system)
    {
        html.Append("<h2>System</h2><div class=\"card metrics\">");
        RenderMetric(html, "CPU", system.CpuText, system.CpuPercent);
        RenderMetric(html, "RAM", system.MemoryText, system.MemoryPercent);
        RenderMetric(html, "Disk", system.DiskText, system.DiskPercent);
        html.Append("<div class=\"metric\"><div class=\"muted\">Uptime</div><div class=\"value\">")
            .Append(Encode(system.UptimeText)).Append("</div></div>");
        html.Append("</div>");
    }

    private static void RenderMetric(StringBuilder html, string label, string value, double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        html.Append("<div class=\"metric\"><div class=\"muted\">").Append(label)
            .Append("</div><div class=\"value\">").Append(Encode(value))
            .Append("</div><div class=\"bar\"><div style=\"width:")
            .Append(clamped.ToString("0", System.Globalization.CultureInfo.InvariantCulture))
            .Append("%\"></div></div></div>");
    }

    private static void RenderTmux(StringBuilder html, TmuxBlock tmux)
    {
        html.Append("<h2>Sessions</h2><div class=\"card\">");
        if (!tmux.TmuxAvailable)
        {
            html.Append("<span class=\"muted\">").Append(Encode(tmux.Error ?? "tmux is not available.")).Append("</span>");
        }
        else if (!tmux.ServerRunning)
        {
            html.Append("<span class=\"muted\">")
                .Append(Encode(tmux.Error ?? "No tmux server running — no sessions.")).Append("</span>");
        }
        else if (tmux.Sessions.Count == 0)
        {
            html.Append("<span class=\"muted\">tmux server running, no sessions.</span>");
        }
        else
        {
            foreach (var session in tmux.Sessions)
            {
                html.Append("<div class=\"row\"><span class=\"mono grow\">").Append(Encode(session.Name))
                    .Append("</span><span class=\"badge ").Append(session.IsAttached ? "ok" : "warn")
                    .Append("\">").Append(session.IsAttached ? "attached" : "detached").Append("</span>")
                    .Append("<span class=\"muted\">").Append(session.WindowCount)
                    .Append(session.WindowCount == 1 ? " window" : " windows");
                if (session.ActiveWindowName is { } window)
                    html.Append(" · ").Append(Encode(window));
                html.Append("</span></div>");
            }
        }

        html.Append("</div>");
    }

    private static void RenderProject(StringBuilder html, WebProjectCard project)
    {
        html.Append("<div class=\"card\"><div class=\"row\"><strong class=\"grow\">")
            .Append(Encode(project.Name)).Append("</strong><span class=\"badge\">")
            .Append(Encode(project.Status)).Append("</span></div>");

        // Git
        html.Append("<div class=\"row\"><span class=\"muted\">git</span><span class=\"mono grow\">")
            .Append(Encode(project.GitText)).Append("</span>");
        if (project.GitDirty)
            html.Append("<span class=\"badge warn\">dirty</span>");
        html.Append("</div>");

        // Docker
        if (project.DockerSummary is not null || project.DockerError is not null || project.Services.Count > 0)
        {
            html.Append("<div class=\"row\"><span class=\"muted\">docker</span><span class=\"mono grow\">")
                .Append(Encode(project.DockerError is not null ? "unavailable" : project.DockerSummary ?? ""))
                .Append("</span></div>");

            if (project.DockerError is not null)
                html.Append("<div class=\"muted\">").Append(Encode(project.DockerError)).Append("</div>");

            if (project.Services.Count > 0)
            {
                html.Append("<details><summary>").Append(project.Services.Count).Append(" services</summary>")
                    .Append("<div class=\"services\">");
                foreach (var service in project.Services)
                {
                    var badgeClass = service.StateText is "running" or "paused" ? "ok"
                        : service.StateText is "completed" ? ""
                        : service.StateText is "failed" or "restarting" ? "bad"
                        : "warn";
                    html.Append("<div class=\"row\"><span class=\"mono grow\">").Append(Encode(service.Name))
                        .Append("</span><span class=\"badge ").Append(badgeClass).Append("\">")
                        .Append(Encode(service.StateText)).Append("</span>");
                    if (service.HealthText is { } health)
                        html.Append("<span class=\"badge ")
                            .Append(health == "healthy" ? "ok" : health == "unhealthy" ? "bad" : "warn")
                            .Append("\">").Append(Encode(health)).Append("</span>");
                    html.Append("</div>");
                }

                html.Append("</div></details>");
            }
        }

        // Recent journal (redacted at the source)
        if (project.RecentActivity.Count > 0)
        {
            html.Append("<details><summary>recent activity</summary><ul class=\"activity\">");
            foreach (var entry in project.RecentActivity)
            {
                html.Append("<li><span class=\"muted\">")
                    .Append(Encode(entry.Timestamp.ToString("MM-dd HH:mm"))).Append(" · ")
                    .Append(Encode(entry.Category)).Append("</span><br>").Append(Encode(entry.Message));
                if (entry.Detail is { } detail)
                    html.Append("<br><span class=\"muted mono\">").Append(Encode(detail)).Append("</span>");
                html.Append("</li>");
            }

            html.Append("</ul></details>");
        }

        html.Append("</div>");
    }

    private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
