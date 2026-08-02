using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

public sealed class DashboardPageRendererTests
{
    private static WebDashboardSnapshot Snapshot() => new(
        GeneratedAt: new DateTimeOffset(2026, 8, 1, 22, 0, 0, TimeSpan.Zero),
        System: new SystemHealthBlock("25%", 25, "4 GB / 16 GB", 25, "100 GB / 512 GB", 20, "1d 6h"),
        Tmux: new TmuxBlock(true, true, null,
            [new WebTmuxSession("banshee", false, 1, "bash"), new WebTmuxSession("dev", true, 3, "vim")]),
        Summary: new WebSummaryBlock(2, 1, 0, 1, 0, 1, 0, 3, 1, 0, 1),
        Projects:
        [
            new WebProjectCard(7, "Banshee Realm 11", "Active",
                "main * ↑2 · ea407af Add cancelled terminal state", GitDirty: true, GitUnavailable: false,
                "5/6 running · 1 completed", "detached",
                HealthText: "Blocked", HealthBadgeClass: "bad",
                HealthReasonsText: "Active blocker with critical severity",
                ProgressText: "1/2 milestones · 75%", ProgressPercent: 75,
                FrontierText: "1/2 frontiers · lobby protocol",
                BlockerText: "2 active · highest critical",
                ResultsText: "build OK · test FAIL · run OK",
                LastActivityText: "08-01 21:00",
                HasMobileActions: true, PrimaryAction: "RunOrStart"),
            new WebProjectCard(9, "Xenogears Decompilation", "Research",
                "repo missing", GitDirty: false, GitUnavailable: true, null, null,
                HealthText: "Unavailable", HealthBadgeClass: "bad",
                HealthReasonsText: "Repository is missing on this machine"),
        ]);

    [Fact]
    public void Page_is_mobile_first_dark_self_contained_and_branded()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("width=device-width", html);
        Assert.Contains("viewport-fit=cover", html);
        Assert.Contains("color-scheme: dark", html);
        Assert.Contains("#0f1115", html);
        Assert.Contains("<title>Development Lab</title>", html);
        Assert.Contains("<h1>Development Lab</h1>", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
    }

    [Fact]
    public void Cards_are_compact_tap_targets_into_detail_pages()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("href=\"/projects/7\"", html);
        Assert.Contains("href=\"/projects/9\"", html);
        Assert.Contains("View project", html);
        Assert.Contains("Banshee Realm 11", html);
        Assert.Contains("5/6 running", html);
        Assert.Contains(">detached<", html);
        Assert.Contains(">dirty<", html);
        Assert.Contains("repo missing", html);
        // Compact means no per-service rows on the main page.
        Assert.DoesNotContain("db-update", html);
    }

    [Fact]
    public void Refresh_furniture_is_present_for_the_polling_script()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("data-endpoint=\"/api/snapshot\"", html);
        Assert.Contains("id=\"last-refresh\"", html);
        Assert.Contains("id=\"stale\" hidden", html);
        Assert.Contains("setTimeout(poll, 10000)", html);
        Assert.Contains("visibilitychange", html);
    }

    [Fact]
    public void Hostile_text_is_html_encoded()
    {
        var snapshot = Snapshot() with
        {
            Projects =
            [
                new WebProjectCard(3, "<script>alert(1)</script>", "Active", "main",
                    false, false, null, null),
            ],
        };

        var html = DashboardPageRenderer.Render(snapshot);

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Empty_states_render_honestly()
    {
        var html = DashboardPageRenderer.Render(Snapshot() with
        {
            Tmux = new TmuxBlock(true, false, null, []),
            Projects = [],
        });

        Assert.Contains("No tmux server running", html);
        Assert.Contains("No projects registered", html);
    }

    [Fact]
    public void Summary_bar_renders_fleet_totals_as_text_and_counts()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("id=\"summary-body\"", html);
        Assert.Contains("<strong>2</strong>", html);        // projects
        Assert.Contains("<strong>1</strong>", html);        // healthy/blocked/etc.
        Assert.Contains(">healthy</span>", html);
        Assert.Contains("badge bad\">blocked</span>", html);
        Assert.Contains(">blockers</span>", html);
        Assert.Contains(" running</span>", html);
        Assert.Contains(">dirty</span>", html);
    }

    [Fact]
    public void Cards_render_the_shared_projection_fields()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        // Health badge: state as text, reasons in the tooltip — never color alone.
        Assert.Contains(">Blocked</span>", html);
        Assert.Contains("Active blocker with critical severity", html);
        Assert.Contains(">Unavailable</span>", html);

        // Progress with a bar for the configured project…
        Assert.Contains("1/2 milestones", html);
        Assert.Contains("75%", html);
        Assert.Contains("width:75%", html);
        // …and the honest empty state for the unconfigured one.
        Assert.Contains("Progress not configured", html);

        Assert.Contains("1/2 frontiers", html);
        Assert.Contains("lobby protocol", html);
        Assert.Contains("no frontier set", html);       // Xenogears has none
        Assert.Contains("2 active", html);
        Assert.Contains("highest critical", html);
        Assert.Contains("no active blockers", html);
        Assert.Contains("build OK", html);
        Assert.Contains("test FAIL", html);
        Assert.Contains("no build/test history", html); // Xenogears never ran anything
        Assert.Contains("last 08-01 21:00", html);
    }

    [Fact]
    public void Actions_link_appears_only_where_the_allowlist_has_entries()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("href=\"/projects/7#actions\"", html);
        Assert.DoesNotContain("href=\"/projects/9#actions\"", html);
        Assert.Contains("href=\"/projects/7\"", html); // the View project tap target survives
        Assert.Contains("href=\"/projects/9\"", html);
    }
}
