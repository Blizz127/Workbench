using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

public sealed class ProjectDetailPageRendererTests
{
    private static ProjectDetailSnapshot BansheeDetail() => new(
        Id: 7,
        Name: "Banshee Realm 11",
        Status: "Active",
        RepositoryPath: "/repo/banshee",
        GeneratedAt: new DateTimeOffset(2026, 8, 1, 22, 0, 0, TimeSpan.Zero),
        Git: new GitDetailBlock(true, null, "main", true, 2, 2, 1,
            "ea407af · Add cancelled terminal state (08-01 12:00)",
            [new WebCommit("ea407af", "Add cancelled terminal state", "08-01 12:00")]),
        Docker: new DockerDetailBlock(true, "5/6 running · 1 completed", null,
        [
            new WebComposeService("database", "running", "healthy"),
            new WebComposeService("db-update", "completed", null),
            new WebComposeService("connect", "running", null),
            new WebComposeService("search", "running", null),
            new WebComposeService("world", "running", null),
            new WebComposeService("map", "running", null),
        ]),
        Session: new ProjectSessionBlock("banshee", true, true, true, false, 1, "bash"),
        Blockers: new BlockerSummaryBlock(0, 0, 0, 0, []),
        RecentActivity:
        [
            new WebActivityEntry("08-01 21:00", "Run", "run failed", "MARIADB_PASSWORD=***"),
        ]);

    [Fact]
    public void Detail_page_has_all_sections_and_branding()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail());

        Assert.Contains("<title>Banshee Realm 11 — Development Lab</title>", html);
        Assert.Contains("width=device-width", html);
        Assert.Contains(">Overview</h2>", html);
        Assert.Contains(">Git</h2>", html);
        Assert.Contains(">Docker</h2>", html);
        Assert.Contains(">Sessions</h2>", html);
        Assert.Contains(">Recent Activity</h2>", html);
        Assert.Contains("href=\"/\"", html); // back to the dashboard
        Assert.Contains("data-endpoint=\"/api/projects/7\"", html);
        Assert.Contains("/repo/banshee", html);
    }

    [Fact]
    public void All_six_banshee_services_render_as_text_badges()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail());

        foreach (var name in new[] { "database", "db-update", "connect", "search", "world", "map" })
            Assert.Contains($">{name}<", html);

        Assert.Contains(">running<", html);
        Assert.Contains(">healthy<", html);
        // The one-shot service reads completed — never failed or unhealthy.
        Assert.Contains(">completed<", html);
        Assert.DoesNotContain("badge bad\">completed", html);
        Assert.DoesNotContain(">failed<", html);
        Assert.DoesNotContain(">unhealthy<", html);
    }

    [Fact]
    public void Dirty_git_state_shows_branch_counts_and_commits()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail());

        Assert.Contains(">main<", html);
        Assert.Contains(">dirty<", html);
        Assert.Contains("2 dirty files", html);
        Assert.Contains("ea407af", html);
        Assert.Contains("Add cancelled terminal state", html);
    }

    [Fact]
    public void Redacted_values_render_but_the_secret_never_does()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail());

        Assert.Contains("MARIADB_PASSWORD=***", html);
        Assert.DoesNotContain("sup3rsecret", html);
    }

    [Fact]
    public void No_repository_no_compose_and_no_session_are_explicit_states()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail() with
        {
            Git = new GitDetailBlock(false, "Path does not exist: /repo/x", null, false, 0, null, null, null, []),
            Docker = new DockerDetailBlock(false, null, null, []),
            Session = new ProjectSessionBlock("xenogears", true, false, false, false, 0, null),
            RecentActivity = [],
        });

        Assert.Contains("Path does not exist: /repo/x", html);
        Assert.Contains("No compose file for this project.", html);
        Assert.Contains("no tmux server", html);
        Assert.Contains("No recent activity.", html);
    }

    [Fact]
    public void Hostile_text_is_html_encoded()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail() with
        {
            Name = "<img src=x onerror=alert(1)>",
            Git = BansheeDetail().Git with { Branch = "<b>main</b>" },
        });

        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<b>main</b>", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public void Unknown_projects_get_a_safe_404_page()
    {
        var html = ProjectDetailPageRenderer.RenderNotFound(999);

        Assert.Contains("Project not found", html);
        Assert.Contains("999", html);
        Assert.Contains("href=\"/\"", html);
    }

    [Fact]
    public void Blocker_summary_shows_counts_and_open_entries()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail() with
        {
            Blockers = new BlockerSummaryBlock(1, 1, 3, 2,
            [
                new WebBlockerEntry("Map server NAT drops zone traffic", "critical", "Active",
                    "FFXI-3001 after character select", "Packet capture on host network", "blizz"),
                new WebBlockerEntry("Intermittent search crash", null, "Monitoring", null, null, null),
            ]),
        });

        Assert.Contains(">Blockers</h2>", html);
        // (· is entity-encoded by HtmlEncode; assert the counts piecewise)
        Assert.Contains("1 active", html);
        Assert.Contains("1 monitoring", html);
        Assert.Contains("3 resolved", html);
        Assert.Contains("2 dismissed", html);
        Assert.Contains("Map server NAT drops zone traffic", html);
        Assert.Contains(">critical<", html);
        Assert.Contains("FFXI-3001 after character select", html);
        Assert.Contains("Packet capture on host network", html);
        Assert.Contains("owner: blizz", html);
        Assert.Contains("Intermittent search crash", html);
        // Entries without optional fields render no empty badges or labels.
        Assert.DoesNotContain("owner: <", html);
    }

    [Fact]
    public void Blocker_summary_is_html_encoded_and_static()
    {
        var html = ProjectDetailPageRenderer.Render(BansheeDetail() with
        {
            Blockers = new BlockerSummaryBlock(1, 0, 0, 0,
            [
                new WebBlockerEntry("<script>alert(1)</script>", "<b>critical</b>", "Active",
                    "MARIADB_PASSWORD=***", "<img src=x>", null),
            ]),
        });

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<b>critical</b>", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.Contains("&lt;script&gt;", html);
        // The section stays outside the JS-polled containers: no id hooks at all.
        var section = html.Split(">Blockers</h2>")[1].Split("<h2>")[0];
        Assert.DoesNotContain("id=\"", section);
    }

    [Fact]
    public void No_blockers_and_only_closed_blockers_are_explicit_states()
    {
        var empty = ProjectDetailPageRenderer.Render(BansheeDetail());
        Assert.Contains("No blockers recorded.", empty);

        var closedOnly = ProjectDetailPageRenderer.Render(BansheeDetail() with
        {
            Blockers = new BlockerSummaryBlock(0, 0, 2, 1, []),
        });
        Assert.Contains("0 active", closedOnly);
        Assert.Contains("2 resolved", closedOnly);
        Assert.Contains("1 dismissed", closedOnly);
        Assert.Contains("Nothing active right now.", closedOnly);
    }
}
