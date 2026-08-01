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
        Projects:
        [
            new WebProjectCard(7, "Banshee Realm 11", "Active",
                "main * ↑2 · ea407af Add cancelled terminal state", GitDirty: true, GitUnavailable: false,
                "5/6 running · 1 completed", "detached"),
            new WebProjectCard(9, "Xenogears Decompilation", "Research",
                "repo missing", GitDirty: false, GitUnavailable: true, null, null),
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
}
