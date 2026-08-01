using Workbench.Core.Models;

namespace Workbench.Tests;

/// <summary>Shared fixture data for the web tests — mirrors the live Banshee stack.</summary>
internal static class WebFixtures
{
    /// <summary>A directory that really exists — the "repo missing" check is filesystem-based.</summary>
    public static readonly string BansheeRepoDir =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "workbench-tests", "web-fixture-banshee")).FullName;

    public static Project Banshee() => new()
    {
        Id = 7,
        Name = "Banshee Realm 11",
        Status = ProjectStatus.Active,
        RepositoryPath = BansheeRepoDir,
        ComposeFile = "docker-compose.docker.yml",
        TmuxSession = "banshee",
    };

    public static Project Xenogears() => new()
    {
        Id = 9,
        Name = "Xenogears Decompilation",
        Status = ProjectStatus.Research,
        RepositoryPath = "/repo/xenogears-does-not-exist",
    };

    public static RepositorySnapshot BansheeGit() => new(
        IsValid: true,
        BranchName: "main",
        LastCommit: new CommitInfo("ea407af123456", "ea407af", "Add cancelled terminal state to DIT telemetry",
            "blizz", new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)),
        RecentCommits:
        [
            new CommitInfo("ea407af123456", "ea407af", "Add cancelled terminal state", "blizz",
                new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)),
            new CommitInfo("b2c3d4e123456", "b2c3d4e", "Fix login packet ordering", "blizz",
                new DateTimeOffset(2026, 7, 31, 20, 0, 0, TimeSpan.Zero)),
        ],
        ModifiedFiles: [new FileChange("login.cs", "modified"), new FileChange("world.cs", "added")],
        IsDirty: true,
        Error: null,
        AheadBy: 2,
        BehindBy: 1);

    /// <summary>The six expected Banshee services, db-update completed as a one-shot success.</summary>
    public static ComposeProjectStatus BansheeCompose() => new(
        ProjectName: "banshee-realm-11",
        ComposeFilePath: "/repo/banshee/docker-compose.docker.yml",
        Services:
        [
            new ComposeServiceStatus("database", "banshee-db-1", ComposeServiceState.Running, ComposeHealth.Healthy, 0, "Up 2 hours (healthy)"),
            new ComposeServiceStatus("db-update", "banshee-db-update-1", ComposeServiceState.Completed, ComposeHealth.None, 0, "Exited (0) 2 hours ago"),
            new ComposeServiceStatus("connect", "banshee-connect-1", ComposeServiceState.Running, ComposeHealth.None, 0, "Up 2 hours"),
            new ComposeServiceStatus("search", "banshee-search-1", ComposeServiceState.Running, ComposeHealth.None, 0, "Up 2 hours"),
            new ComposeServiceStatus("world", "banshee-world-1", ComposeServiceState.Running, ComposeHealth.None, 0, "Up 2 hours"),
            new ComposeServiceStatus("map", "banshee-map-1", ComposeServiceState.Running, ComposeHealth.None, 0, "Up 2 hours"),
        ]);

    public static TmuxServerStatus TmuxRunning() => new(
        TmuxAvailable: true,
        ServerRunning: true,
        Sessions: [new TmuxSession("banshee", new DateTimeOffset(2026, 8, 1, 18, 53, 37, TimeSpan.Zero), false, 1, "bash")],
        Error: null);

    public static TimelineEntry SecretJournalEntry() => new()
    {
        ProjectId = 7,
        Timestamp = new DateTimeOffset(2026, 8, 1, 21, 0, 0, TimeSpan.Zero),
        Category = TimelineCategory.Run,
        Message = "run failed",
        Detail = "connecting with MARIADB_PASSWORD=sup3rsecret refused",
    };
}
