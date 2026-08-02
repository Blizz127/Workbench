using System.Text;

namespace Workbench.Web;

/// <summary>
/// Renders one project's detail page: a sticky header (back link, name,
/// live status badge), a wrapping tab nav, and four anchor sections —
/// #overview (path + Git), #run (runtime health, Docker, sessions, actions),
/// #progress (milestones, frontier, blockers), #history (recent activity).
/// Docker services are explicit rows with text badges (state AND health in
/// words, never color alone); a one-shot service that exited 0 reads
/// "completed", never failed. The markup carries the ids the refresh script
/// updates; the tab nav, actions, and blocker sections stay outside every
/// id-tagged container so polling never wipes them.
/// </summary>
public static class ProjectDetailPageRenderer
{
    public static string Render(ProjectDetailSnapshot detail) =>
        Render(detail, [], []);

    public static string Render(
        ProjectDetailSnapshot detail,
        IReadOnlyList<MobileActionDescriptor> actions,
        IReadOnlyList<ActionJournalEntry> recentActions,
        bool actionsEnabled = true)
    {
        var html = new StringBuilder(capacity: 24 * 1024);
        WebPageShell.WriteHead(html, $"{detail.Name} — Development Lab", "detail", $"/api/projects/{detail.Id}");

        // Sticky header: back link, name, and the live status badge (the
        // refresh script updates ov-status here). Static — never polled away.
        html.Append("<div class=\"project-header\">")
            .Append("<a class=\"back-link\" href=\"/\">← All projects</a>")
            .Append("<div class=\"row\"><h1 class=\"grow\">").Append(WebPageShell.Encode(detail.Name))
            .Append("</h1><span class=\"badge\" id=\"ov-status\">")
            .Append(WebPageShell.Encode(detail.Status)).Append("</span></div>");
        DashboardPageRenderer.WriteRefreshNote(html, detail.GeneratedAt);
        html.Append("</div>");

        html.Append("<nav class=\"tabs\">")
            .Append("<a href=\"#overview\">Overview</a>")
            .Append("<a href=\"#run\">Run</a>")
            .Append("<a href=\"#progress\">Progress</a>")
            .Append("<a href=\"/projects/").Append(detail.Id).Append("/files\">Code</a>")
            .Append("<a href=\"#history\">History</a>")
            .Append("<a href=\"/audit?project=").Append(detail.Id).Append("\">Audit</a>")
            .Append("</nav>");

        // ---------- #overview: path + Git ----------
        html.Append("<h2 id=\"overview\">Overview</h2><div class=\"card\">")
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

        // ---------- #run: runtime health, Docker, sessions, actions ----------
        WriteRuntimeHealthCard(html, detail.RuntimeHealth);

        // Docker: failing/unhealthy services are called out at the top of the
        // card before the summary — a "N failing" badge with the names.
        var failing = detail.Docker.Services
            .Where(s => s.StateText is "failed" or "restarting" || s.HealthText == "unhealthy")
            .ToList();
        html.Append("<h2>Docker</h2><div class=\"card\">");
        html.Append("<span class=\"muted\" id=\"docker-none\"").Append(detail.Docker.HasComposeFile ? " hidden" : "")
            .Append(">No compose file for this project.</span>");
        html.Append("<div id=\"docker-body\"").Append(detail.Docker.HasComposeFile ? "" : " hidden").Append(">")
            .Append("<div class=\"row\" id=\"docker-failing\"").Append(failing.Count > 0 ? "" : " hidden")
            .Append("><span class=\"badge bad\" id=\"docker-failing-n\">").Append(failing.Count)
            .Append(" failing</span><span class=\"muted grow\" id=\"docker-failing-v\">")
            .Append(WebPageShell.Encode(string.Join(", ", failing.Select(s => s.Name)))).Append("</span></div>")
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

        // Actions + their journal: static, server-rendered, and deliberately
        // outside every id-tagged container so the polling re-render of the
        // detail endpoint never wipes them.
        ActionPageRenderer.WriteActionsSection(html, detail.Id, actions, actionsEnabled);
        ActionPageRenderer.WriteRecentActionsSection(html, recentActions);
        html.Append("<p><a class=\"view-link\" href=\"/audit?project=").Append(detail.Id)
            .Append("\">Full action audit →</a></p>");

        // ---------- #progress: milestones, frontier, blockers ----------
        WriteProgressCard(html, detail.Progress);

        // Blockers: read-only summary, server-rendered and deliberately outside
        // every id-tagged container so the polling re-render never wipes it.
        WriteBlockersSection(html, detail.Blockers, detail.Progress.ResolvedBlockers);

        // ---------- #history: recent activity (already secret-redacted) ----------
        html.Append("<h2 id=\"history\">Recent Activity</h2><div class=\"card\">")
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

    /// <summary>The #run runtime-health card: static, never polled.</summary>
    internal static void WriteRuntimeHealthCard(StringBuilder html, RuntimeHealthBlock? health)
    {
        html.Append("<h2 id=\"run\">Runtime Health</h2><div class=\"card\">");
        if (health is null)
        {
            html.Append("<span class=\"muted\">No runtime health records.</span></div>");
            return;
        }

        html.Append("<div class=\"row\"><span class=\"muted\">verdict</span><span class=\"badge")
            .Append(health.VerdictBadgeClass.Length > 0 ? " " + health.VerdictBadgeClass : "")
            .Append("\">").Append(WebPageShell.Encode(health.VerdictText)).Append("</span>");
        if (health.ComponentText is { } component)
            html.Append("<span class=\"mono grow\">").Append(WebPageShell.Encode(component)).Append("</span>");
        html.Append("</div>")
            .Append("<div class=\"row\"><span class=\"muted\">checked</span><span class=\"mono grow\">")
            .Append(WebPageShell.Encode(health.CheckedAtText)).Append("</span></div></div>");
    }

    /// <summary>
    /// The #progress card: weighted progress bar (or the honest "not
    /// configured" line — never an implied 0%), the milestone checklist with
    /// status badges (done carries a ✓ text marker), and the frontier line.
    /// Static, server-rendered.
    /// </summary>
    internal static void WriteProgressCard(StringBuilder html, ProgressBlock progress)
    {
        html.Append("<h2 id=\"progress\">Progress</h2><div class=\"card\">");
        if (progress.ProgressText is { } progressText)
        {
            html.Append("<div class=\"row\"><div class=\"grow\"><div class=\"bar\"><div style=\"width:")
                .Append(Math.Clamp(progress.ProgressPercent, 0, 100)
                    .ToString("0", System.Globalization.CultureInfo.InvariantCulture))
                .Append("%\"></div></div></div><span class=\"muted\">")
                .Append(WebPageShell.Encode(progressText)).Append("</span></div>");
        }
        else
        {
            html.Append("<span class=\"muted\">Progress not configured</span>");
        }

        foreach (var milestone in progress.Milestones)
        {
            html.Append("<div class=\"row\"><span class=\"grow\">").Append(WebPageShell.Encode(milestone.Title))
                .Append("</span>");
            if (milestone.Done)
                html.Append("<span class=\"badge ok\">done ✓</span>");
            else
                html.Append("<span class=\"badge").Append(milestone.StatusText == "blocked" ? " warn" : "")
                    .Append("\">").Append(WebPageShell.Encode(milestone.StatusText)).Append("</span>");
            html.Append("</div>");
        }

        html.Append("<div class=\"row\"><span class=\"muted\">frontier</span><span class=\"mono grow\">")
            .Append(WebPageShell.Encode(progress.FrontierText)).Append("</span></div></div>");
    }

    internal static void WriteBlockersSection(
        StringBuilder html, BlockerSummaryBlock blockers, IReadOnlyList<WebBlockerEntry> resolved)
    {
        html.Append("<h2>Blockers</h2><div class=\"card\">");
        if (blockers.Open.Count == 0 && blockers is { Active: 0, Monitoring: 0, Resolved: 0, Dismissed: 0 })
        {
            html.Append("<span class=\"muted\">No blockers recorded.</span></div>");
            return;
        }

        html.Append("<div class=\"row\"><span class=\"muted\">counts</span><span class=\"mono grow\">")
            .Append(WebPageShell.Encode(
                $"{blockers.Active} active · {blockers.Monitoring} monitoring · " +
                $"{blockers.Resolved} resolved · {blockers.Dismissed} dismissed"))
            .Append("</span></div>");

        if (blockers.Open.Count == 0)
            html.Append("<span class=\"muted\">Nothing active right now.</span>");

        foreach (var blocker in blockers.Open)
        {
            html.Append("<div class=\"row\"><span class=\"grow\">").Append(WebPageShell.Encode(blocker.Title)).Append("</span>");
            if (blocker.Severity is { } severity)
                html.Append("<span class=\"badge warn\">").Append(WebPageShell.Encode(severity)).Append("</span>");
            html.Append("<span class=\"badge\">").Append(WebPageShell.Encode(blocker.Status)).Append("</span></div>");
            if (blocker.Evidence is { } evidence)
                html.Append("<div class=\"muted mono\">evidence: ").Append(WebPageShell.Encode(evidence)).Append("</div>");
            if (blocker.NextSafestStep is { } nextStep)
                html.Append("<div class=\"muted\">next safest step: ").Append(WebPageShell.Encode(nextStep)).Append("</div>");
            if (blocker.Owner is { } owner)
                html.Append("<div class=\"muted\">owner: ").Append(WebPageShell.Encode(owner)).Append("</div>");
        }

        // Resolved/dismissed blockers collapse away behind one compact line.
        if (resolved.Count > 0)
        {
            html.Append("<details class=\"resolved\"><summary>")
                .Append(WebPageShell.Encode($"{blockers.Resolved} resolved · {blockers.Dismissed} dismissed"))
                .Append("</summary>");
            foreach (var blocker in resolved)
                html.Append("<div class=\"row\"><span class=\"grow\">").Append(WebPageShell.Encode(blocker.Title))
                    .Append("</span><span class=\"badge\">").Append(WebPageShell.Encode(blocker.Status))
                    .Append("</span></div>");
            html.Append("</details>");
        }

        html.Append("</div>");
    }

    internal static void WriteServiceRow(StringBuilder html, WebComposeService service)
    {        var badgeClass = service.StateText is "running" or "paused" ? "ok"
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
