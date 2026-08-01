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
            new WebProjectCard(
                Name: "Banshee Realm 11",
                Status: "Active",
                GitText: "main * ↑2 · abcdef1 Fix login packet",
                GitDirty: true,
                GitError: null,
                DockerSummary: "5/6 running · 1 completed",
                Services:
                [
                    new WebComposeService("database", "running", "healthy"),
                    new WebComposeService("db-update", "completed", null),
                    new WebComposeService("world", "failed", "unhealthy"),
                ],
                DockerError: null,
                RecentActivity:
                [
                    new WebActivityEntry(
                        new DateTimeOffset(2026, 8, 1, 21, 0, 0, TimeSpan.Zero),
                        "Run", "run failed", "MARIADB_PASSWORD=***"),
                ]),
        ]);

    [Fact]
    public void Page_is_mobile_first_dark_and_self_contained()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("width=device-width", html);
        Assert.Contains("viewport-fit=cover", html);
        Assert.Contains("color-scheme: dark", html);
        Assert.Contains("#0f1115", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
    }

    [Fact]
    public void Page_shows_health_tmux_and_the_project_card()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("4 GB / 16 GB", html);
        Assert.Contains("1d 6h", html);
        Assert.Contains("Banshee Realm 11", html);
        Assert.Contains("5/6 running", html);
        Assert.Contains("1 completed", html);
        Assert.Contains(">detached<", html);
        Assert.Contains(">attached<", html);
        Assert.Contains("main *", html);
        Assert.Contains("abcdef1", html);
        Assert.Contains("db-update", html);
    }

    [Fact]
    public void Redacted_values_render_but_the_secret_never_does()
    {
        var html = DashboardPageRenderer.Render(Snapshot());

        Assert.Contains("MARIADB_PASSWORD=***", html);
        Assert.DoesNotContain("sup3rsecret", html);
    }

    [Fact]
    public void Hostile_text_is_html_encoded()
    {
        var snapshot = Snapshot() with
        {
            Projects =
            [
                new WebProjectCard("<script>alert(1)</script>", "Active", "main", false,
                    null, null, [], null, []),
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
