using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data;
using Workbench.Data.Repositories;
using Workbench.Data.SampleData;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Registration guarantees for Earth &amp; Beyond DIT and Ragnarok Online DIT:
/// seed idempotency, missing-repo health is Unavailable (not Failing), and
/// launch commands are only present when typed into project config.
/// </summary>
public sealed class DitProjectRegistrationTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "workbench-tests", $"dit-reg-{Guid.NewGuid():N}.db");

    private readonly WorkbenchDatabase _database;

    public DitProjectRegistrationTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _database = new WorkbenchDatabase(_dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public void Earth_and_Beyond_seed_is_idempotent()
    {
        SeedBoth();
        SeedBoth();

        var projects = new ProjectRepository(_database).GetAll();
        Assert.Equal(1, projects.Count(p => p.Name == EarthAndBeyondSeeder.ProjectName));

        var project = projects.Single(p => p.Name == EarthAndBeyondSeeder.ProjectName);
        var milestones = new MilestoneRepository(_database).GetForProject(project.Id);
        Assert.Equal(9, milestones.Count);
        Assert.DoesNotContain(milestones, m => string.IsNullOrWhiteSpace(m.Title));
    }

    [Fact]
    public void Ragnarok_seed_is_idempotent()
    {
        SeedBoth();
        SeedBoth();

        var projects = new ProjectRepository(_database).GetAll();
        Assert.Equal(1, projects.Count(p => p.Name == RagnarokOnlineSeeder.ProjectName));

        var project = projects.Single(p => p.Name == RagnarokOnlineSeeder.ProjectName);
        var milestones = new MilestoneRepository(_database).GetForProject(project.Id);
        Assert.Equal(6, milestones.Count);
    }

    [Fact]
    public void SampleDataSeeder_registers_both_without_duplicates()
    {
        SampleDataSeeder.SeedAll(_database, projectsFolder: CreateSamplesFolder());
        SampleDataSeeder.SeedAll(_database, projectsFolder: CreateSamplesFolder());

        var names = new ProjectRepository(_database).GetAll().Select(p => p.Name).ToList();
        Assert.Contains(EarthAndBeyondSeeder.ProjectName, names);
        Assert.Contains(RagnarokOnlineSeeder.ProjectName, names);
        Assert.Equal(1, names.Count(n => n == EarthAndBeyondSeeder.ProjectName));
        Assert.Equal(1, names.Count(n => n == RagnarokOnlineSeeder.ProjectName));
    }

    [Fact]
    public void Missing_repository_is_Unavailable_not_Failing()
    {
        var assessment = ProjectHealthCalculator.Assess(new ProjectHealthInput(
            RepositoryAvailable: false,
            LatestRuntimeHealth: null,
            LastBuildSucceeded: null,
            LastTestPassed: null,
            ActiveBlockerSeverities: [],
            IsDirty: false,
            AheadBy: 0,
            BehindBy: 0));

        Assert.Equal(ProjectHealthState.Unavailable, assessment.State);
        Assert.NotEqual(ProjectHealthState.Failing, assessment.State);
        Assert.Contains(assessment.Reasons, r => r.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_runtime_signal_stays_Unknown_not_Healthy()
    {
        var assessment = ProjectHealthCalculator.Assess(new ProjectHealthInput(
            RepositoryAvailable: true,
            LatestRuntimeHealth: null,
            LastBuildSucceeded: null,
            LastTestPassed: null,
            ActiveBlockerSeverities: [],
            IsDirty: false,
            AheadBy: 0,
            BehindBy: 0));

        Assert.Equal(ProjectHealthState.Unknown, assessment.State);
        Assert.NotEqual(ProjectHealthState.Healthy, assessment.State);
    }

    [Fact]
    public void Seeded_projects_only_expose_real_commands()
    {
        SeedBoth();
        var projects = new ProjectRepository(_database).GetAll();

        var enb = projects.Single(p => p.Name == EarthAndBeyondSeeder.ProjectName);
        Assert.False(string.IsNullOrWhiteSpace(enb.BuildCommand));
        Assert.False(string.IsNullOrWhiteSpace(enb.RunCommand));
        Assert.False(string.IsNullOrWhiteSpace(enb.TestCommand));
        Assert.Contains("enb-e2", enb.RunCommand, StringComparison.Ordinal);
        Assert.Contains("validate-schema", enb.TestCommand, StringComparison.Ordinal);
        Assert.Contains("13306", enb.Launch.StatusCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("password", enb.BuildCommand ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", enb.RunCommand ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://github.com/Blizz127/Earth-and-Beyond-DIT", enb.GitHubRepoUrl);

        var ro = projects.Single(p => p.Name == RagnarokOnlineSeeder.ProjectName);
        Assert.Contains("cmake", ro.BuildCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../tooling/start-all.sh", ro.RunCommand, StringComparison.Ordinal);
        Assert.Contains("../tooling/health-check.sh", ro.TestCommand, StringComparison.Ordinal);
        Assert.Contains("6900", ro.Launch.StatusCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("password", ro.RunCommand ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://github.com/Blizz127/Ragnarok-Online-DIT", ro.GitHubRepoUrl);
    }

    [Fact]
    public void Sample_workbench_json_files_parse_as_project_config()
    {
        var service = new ProjectConfigService();
        var repoRoot = FindRepoRoot();

        var enbDir = Path.Combine(repoRoot, "samples", "earth-and-beyond-dit");
        var roDir = Path.Combine(repoRoot, "samples", "ragnarok-online-dit");
        Assert.True(Directory.Exists(enbDir), $"Missing sample dir: {enbDir}");
        Assert.True(Directory.Exists(roDir), $"Missing sample dir: {roDir}");

        var enb = service.Load(enbDir);
        var ro = service.Load(roDir);
        Assert.NotNull(enb);
        Assert.NotNull(ro);
        Assert.Equal(EarthAndBeyondSeeder.ProjectName, enb.Name);
        Assert.Equal(RagnarokOnlineSeeder.ProjectName, ro.Name);
        Assert.False(string.IsNullOrWhiteSpace(enb.BuildCommand));
        Assert.False(string.IsNullOrWhiteSpace(ro.BuildCommand));
        Assert.NotNull(enb.LaunchProfile);
        Assert.NotNull(ro.LaunchProfile);
        Assert.Contains(enb.LaunchProfile!.RequiredServices, s => s == "enb-database");
        Assert.Contains(ro.LaunchProfile!.RequiredServices, s => s == "login-server");
    }

    private void SeedBoth()
    {
        var projects = new ProjectRepository(_database);
        var existing = projects.GetAll().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;
        var milestones = new MilestoneRepository(_database);
        var frontiers = new FrontierRepository(_database);
        var blockers = new BlockerRepository(_database);
        var timeline = new TimelineRepository(_database);
        var notes = new NoteRepository(_database);

        if (!existing.Contains(EarthAndBeyondSeeder.ProjectName))
            EarthAndBeyondSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now);
        if (!existing.Contains(RagnarokOnlineSeeder.ProjectName))
            RagnarokOnlineSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now);
    }

    private static string CreateSamplesFolder()
    {
        // SampleDataSeeder only needs the folder to exist; seeder dispatch is by name map.
        var dir = Path.Combine(Path.GetTempPath(), "workbench-tests", $"samples-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Workbench.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Workbench.sln from test base directory.");
    }
}
