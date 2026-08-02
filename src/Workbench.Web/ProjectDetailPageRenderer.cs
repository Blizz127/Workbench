using System.Text;

namespace Workbench.Web;

/// <summary>
/// Renders one project's detail page in five sections — Overview, Git,
/// Docker, Sessions, Recent Activity — sized for an iPhone screen. Docker
/// services are explicit rows with text badges (state AND health in words,
/// never color alone); a one-shot service that exited 0 reads "completed",
/// never failed. The markup carries the ids the refresh script updates.
/// </summary>
public static class ProjectDetailPageRenderer
{
    public static string Render(ProjectDetailSnapshot detail) =>
        Render(detail, [], []);

    public static string Render(
        ProjectDetailSnapshot detail,
        IReadOnlyList<MobileActionDescriptor> actions,
        IReadOnlyList<ActionJournalEntry> recentActions)
    {
        var html = new StringBuilder(capacity: 24 * 1024);
        WebPageShell.WriteHead(html, $"{detail.Name} — Development Lab", "detail", $"/api/projects/{detail.Id}");

        html.Append("<a class=\"back-link\" href=\"/\">← All projects</a>");
        html.Append("<h1>").Append(WebPageShell.Encode(detail.Name)).Append("</h1>");
        DashboardPageRenderer.WriteRefreshNote(html, detail.GeneratedAt);

        // Static entry into the code browser — deliberately outside every
        // id-tagged container, so the polling re-render never wipes it.
        html.Append("<nav class=\"code-links\">")
            .Append("<a href=\"/projects/").Append(detail.Id).Append("/files\">Code</a>")
            .Append("<a href=\"/projects/").Append(detail.Id).Append("/changes\">Changes</a>")
            .Append("<a href=\"/projects/").Append(detail.Id).Append("/search\">Search</a>")
            .Append("</nav>");

        // Overview
        html.Append("<h2>Overview</h2><div class=\"card\">")
            .Append("<div class=\"row\"><span class=\"muted\">status</span><span class=\"badge\" id=\"ov-status\">")
            .Append(WebPageShell.Encode(detail.Status)).Append("</span></div>")
            .Append("<div class=\"row\"><span class=\"muted\">path</span><span class=\"path grow\" id=\"ov-path\">")
            .Append(WebPageShell.Encode(detail.RepositoryPath ?? "no repository path")).Append("</span></div>")
            .Append("</div>");

        // Git
        html.Append("<h2>Git</h2><div class=\"card\">");
        html.Append("<span class=\"muted\" id=\"git-error\"").Append(detail.Git.Available ? " hidden" : "")
            .Append(">").Append(WebPageShell.Encode(detail.Git.Error ?? "repository unavailable")).Append("</span>");
        html.Append("<div id=\"git-body\"").Append(detail.Git.Available ? "" : " hidden").Append(">")
            .Append("<div class=\"row\"><span class=\"muted\">branch</span><span class=\"mono grow\" id=\"git-branch\">")
            .Append(WebPageShell.Encode(detail.Git.Branch ?? "?")).Append("</span>")
            .Append("<span class=\"badge warn\" id=\"git-dirty\"").Append(detail.Git.Dirty ? "" : " hidden")
            .Append(">dirty</span></div>")
            .Append("<div class=\"row\"><span class=\"muted\">state</span><span class=\"mono grow\" id=\"git-counts\">")
            .Append(WebPageShell.Encode(CountsText(detail.Git))).Append("</span></div>")
            .Append("<div class=\"row\"><span class=\"muted\">HEAD</span><span class=\"mono grow\" id=\"git-last\">")
            .Append(WebPageShell.Encode(detail.Git.LastCommitText ?? "no commits")).Append("</span></div>");
        if (detail.Git.RecentCommits.Count > 0)
        {
            html.Append("<ul class=\"lined\" id=\"git-commits\">");
            foreach (var commit in detail.Git.RecentCommits)
                html.Append("<li><a class=\"commit-link\" href=\"/projects/").Append(detail.Id)
                    .Append("/commits/").Append(WebPageShell.Encode(commit.ShortSha)).Append("\"><span class=\"mono\">")
                    .Append(WebPageShell.Encode($"{commit.ShortSha} {commit.Subject} "))
                    .Append("</span><span class=\"muted\">").Append(WebPageShell.Encode(commit.When)).Append("</span></a></li>");
            html.Append("</ul>");
        }
        else
        {
            html.Append("<ul class=\"lined\" id=\"git-commits\"></ul>");
        }

        html.Append("</div></div>");

        // Docker
        html.Append("<h2>Docker</h2><div class=\"card\">");
        html.Append("<span class=\"muted\" id=\"docker-none\"").Append(detail.Docker.HasComposeFile ? " hidden" : "")
            .Append(">No compose file for this project.</span>");
        html.Append("<div id=\"docker-body\"").Append(detail.Docker.HasComposeFile ? "" : " hidden").Append(">")
            .Append("<div class=\"row\"><span class=\"muted\">summary</span><span class=\"mono grow\" id=\"docker-summary\">")
            .Append(WebPageShell.Encode(detail.Docker.Summary ?? string.Empty)).Append("</span></div>")
            .Append("<div class=\"muted\" id=\"docker-error\"").Append(detail.Docker.Error is null ? " hidden" : "")
            .Append(">").Append(WebPageShell.Encode(detail.Docker.Error)).Append("</div>")
            .Append("<div id=\"docker-services\"").Append(detail.Docker.Services.Count > 0 ? "" : " hidden").Append(">");
        foreach (var service in detail.Docker.Services)
            WriteServiceRow(html, service);
        html.Append("</div></div></div>");

        // Sessions
        html.Append("<h2>Sessions</h2><div class=\"card\">")
            .Append("<div class=\"row\"><span class=\"muted\">session</span><span class=\"mono grow\" id=\"session-name\">")
            .Append(WebPageShell.Encode(detail.Session.SessionName)).Append("</span>")
            .Append("<span class=\"badge").Append(detail.Session.Exists ? detail.Session.IsAttached ? " ok" : " warn" : "")
            .Append("\" id=\"session-state\">").Append(WebPageShell.Encode(SessionStateText(detail.Session))).Append("</span></div>")
            .Append("<div class=\"row\" id=\"session-windows\"").Append(detail.Session.Exists ? "" : " hidden")
            .Append("><span class=\"muted\">windows</span><span class=\"mono grow\" id=\"session-windows-v\">")
            .Append(detail.Session.WindowCount)
            .Append(detail.Session.WindowCount == 1 ? " window" : " windows")
            .Append(detail.Session.ActiveWindowName is { } window ? $" · {WebPageShell.Encode(window)}" : "")
            .Append("</span></div>")
            .Append("</div>");

        // Recent activity (already secret-redacted at the source)
        html.Append("<h2>Recent Activity</h2><div class=\"card\">")
            .Append("<span class=\"muted\" id=\"activity-none\"").Append(detail.RecentActivity.Count > 0 ? " hidden" : "")
            .Append(">No recent activity.</span>")
            .Append("<ul class=\"lined\" id=\"activity-list\">");
        foreach (var entry in detail.RecentActivity)
        {
            html.Append("<li><span class=\"muted\">").Append(WebPageShell.Encode($"{entry.When} · {entry.Category}"))
                .Append("</span><br>").Append(WebPageShell.Encode(entry.Message));
            if (entry.Detail is { } activityDetail)
                html.Append("<br><span class=\"muted mono\">").Append(WebPageShell.Encode(activityDetail)).Append("</span>");
            html.Append("</li>");
        }

        html.Append("</ul></div>");

        // Actions + their journal: static, server-rendered, and deliberately
        // outside every id-tagged container so the polling re-render of the
        // detail endpoint never wipes them.
        ActionPageRenderer.WriteActionsSection(html, detail.Id, actions);
        ActionPageRenderer.WriteRecentActionsSection(html, recentActions);
        html.Append("<p><a class=\"view-link\" href=\"/audit?project=").Append(detail.Id)
            .Append("\">Full action audit →</a></p>");

        WebPageShell.WriteScript(html);
        return html.ToString();
    }

    /// <summary>Safe 404 page for unknown or deleted project ids.</summary>
    public static string RenderNotFound(long projectId)
    {
        var html = new StringBuilder(capacity: 4 * 1024);
        WebPageShell.WriteHead(html, "Not found — Development Lab", "none", "");
        html.Append("<a class=\"back-link\" href=\"/\">← All projects</a>")
            .Append("<div class=\"card\"><h1>Project not found</h1>")
            .Append("<p class=\"muted\">No project with id ").Append(projectId)
            .Append(" exists. It may have been removed — the list behind “All projects” is always current.</p>")
            .Append("</div>\n</body>\n</html>");
        return html.ToString();
    }

    internal static void WriteServiceRow(StringBuilder html, WebComposeService service)
    {
        var badgeClass = service.StateText is "running" or "paused" ? "ok"
            : service.StateText is "completed" ? ""
            : service.StateText is "failed" or "restarting" ? "bad"
            : "warn";
        html.Append("<div class=\"row\"><span class=\"mono grow\">").Append(WebPageShell.Encode(service.Name))
            .Append("</span><span class=\"badge ").Append(badgeClass).Append("\">")
            .Append(WebPageShell.Encode(service.StateText)).Append("</span>");
        if (service.HealthText is { } health)
            html.Append("<span class=\"badge ")
                .Append(health == "healthy" ? "ok" : health == "unhealthy" ? "bad" : "warn")
                .Append("\">").Append(WebPageShell.Encode(health)).Append("</span>");
        html.Append("</div>");
    }

    private static string CountsText(GitDetailBlock git)
    {
        var parts = new List<string> { $"{git.DirtyCount} dirty file{(git.DirtyCount == 1 ? "" : "s")}" };
        if (git.AheadBy is int ahead and > 0)
            parts.Add($"↑{ahead}");
        if (git.BehindBy is int behind and > 0)
            parts.Add($"↓{behind}");
        return string.Join(" · ", parts);
    }

    private static string SessionStateText(ProjectSessionBlock session)
    {
        var window = session.ActiveWindowName is { } name ? $" · {name}" : string.Empty;
        return !session.TmuxAvailable ? "tmux unavailable"
            : !session.ServerRunning ? "no tmux server"
            : !session.Exists ? "no session"
            : session.IsAttached ? $"attached{window}" : $"detached{window}";
    }
}
