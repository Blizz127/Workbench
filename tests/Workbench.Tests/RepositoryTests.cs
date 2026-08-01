using Workbench.Core.Models;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class RepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public RepositoryTests(DatabaseFixture fixture) => _fixture = fixture;

    private long AddProject(string name = "Test Project")
    {
        var repo = new ProjectRepository(_fixture.Database);
        return repo.Add(new Project { Name = name });
    }

    [Fact]
    public void Project_roundtrips_all_fields_including_tags_and_paths()
    {
        var repo = new ProjectRepository(_fixture.Database);
        var project = new Project
        {
            Name = "Xenogears",
            Status = ProjectStatus.Research,
            Description = "desc",
            RepositoryPath = "/tmp/xeno",
            BuildCommand = "make",
            RunCommand = "./run",
            TestCommand = "pytest",
            DocumentationPaths = ["docs/", "notes.md"],
            AiContextPaths = ["include/"],
            PreferredAiProvider = "none-yet",
            Tags = ["Reverse Engineering", "PSX"],
            ActiveTask = "task",
            CurrentObjective = "objective",
        };

        var id = repo.Add(project);
        var loaded = repo.GetById(id);

        Assert.NotNull(loaded);
        Assert.Equal("Xenogears", loaded.Name);
        Assert.Equal(ProjectStatus.Research, loaded.Status);
        Assert.Equal("/tmp/xeno", loaded.RepositoryPath);
        Assert.Equal(["docs/", "notes.md"], loaded.DocumentationPaths);
        Assert.Equal(["include/"], loaded.AiContextPaths);
        Assert.Equal(2, loaded.Tags.Count);
        Assert.Contains("PSX", loaded.Tags);
        Assert.Equal("task", loaded.ActiveTask);
        Assert.Equal("objective", loaded.CurrentObjective);
    }

    [Fact]
    public void Project_launch_profile_roundtrips_through_add_and_update()
    {
        var repo = new ProjectRepository(_fixture.Database);
        var project = new Project
        {
            Name = $"launch-{Guid.NewGuid():N}",
            Launch = new LaunchProfile
            {
                StartupGraceSeconds = 15,
                StatusCommand = "podman ps",
                RequiredServices = ["database", "map"],
                OptionalServices = ["metrics"],
                HealthyPatterns = ["up"],
                FailedPatterns = ["Segmentation fault"],
                ServiceLogCommand = "podman logs {service}",
                HealthCheckRetries = 4,
                HealthCheckIntervalSeconds = 8,
                StopCommand = "podman compose down",
                LogsCommand = "podman compose logs",
                UpdateCommand = "git pull --ff-only",
                MetricsCommand = "echo 'Players: 0'",
            },
        };

        var id = repo.Add(project);
        var loaded = repo.GetById(id)!;
        Assert.Equal(15, loaded.Launch.StartupGraceSeconds);
        Assert.Equal("podman ps", loaded.Launch.StatusCommand);
        Assert.Equal(["database", "map"], loaded.Launch.RequiredServices);
        Assert.Equal(["metrics"], loaded.Launch.OptionalServices);
        Assert.Equal(["up"], loaded.Launch.HealthyPatterns);
        Assert.Equal(["Segmentation fault"], loaded.Launch.FailedPatterns);
        Assert.Equal("podman logs {service}", loaded.Launch.ServiceLogCommand);
        Assert.Equal(4, loaded.Launch.HealthCheckRetries);
        Assert.Equal(8, loaded.Launch.HealthCheckIntervalSeconds);
        Assert.Equal("podman compose down", loaded.Launch.StopCommand);
        Assert.Equal("podman compose logs", loaded.Launch.LogsCommand);
        Assert.Equal("git pull --ff-only", loaded.Launch.UpdateCommand);
        Assert.Equal("echo 'Players: 0'", loaded.Launch.MetricsCommand);

        loaded.Launch.RequiredServices = ["database"];
        loaded.Launch.StatusCommand = null;
        repo.Update(loaded);

        var reloaded = repo.GetById(id)!;
        Assert.Null(reloaded.Launch.StatusCommand);
        Assert.Equal(["database"], reloaded.Launch.RequiredServices);
    }

    [Fact]
    public void Project_update_persists_and_bumps_updated_at()
    {
        var repo = new ProjectRepository(_fixture.Database);
        var id = AddProject("Before");
        var project = repo.GetById(id)!;
        var firstUpdate = project.UpdatedAt;

        project.Name = "After";
        project.Status = ProjectStatus.Broken;
        repo.Update(project);

        var loaded = repo.GetById(id)!;
        Assert.Equal("After", loaded.Name);
        Assert.Equal(ProjectStatus.Broken, loaded.Status);
        Assert.True(loaded.UpdatedAt >= firstUpdate);
    }

    [Fact]
    public void Deleting_a_project_cascades_to_children()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var notes = new NoteRepository(_fixture.Database);
        var timeline = new TimelineRepository(_fixture.Database);

        var id = AddProject("Doomed");
        notes.Add(new Note { ProjectId = id, Title = "n", Content = "c" });
        timeline.Add(new TimelineEntry { ProjectId = id, Message = "m" });

        projects.Delete(id);

        Assert.Null(projects.GetById(id));
        Assert.Empty(notes.GetForProject(id));
        Assert.Empty(timeline.GetForProject(id));
    }

    [Fact]
    public void Frontier_current_and_next_are_queryable()
    {
        var frontiers = new FrontierRepository(_fixture.Database);
        var id = AddProject();

        frontiers.Add(new Frontier { ProjectId = id, Title = "done", Status = FrontierStatus.Completed });
        frontiers.Add(new Frontier { ProjectId = id, Title = "now", Status = FrontierStatus.Current });
        frontiers.Add(new Frontier { ProjectId = id, Title = "later", Status = FrontierStatus.Next });

        Assert.Equal("now", frontiers.GetCurrent(id)?.Title);
        Assert.Equal("later", frontiers.GetNext(id)?.Title);
        Assert.Equal(3, frontiers.GetForProject(id).Count);
    }

    [Fact]
    public void Blockers_separate_open_from_resolved()
    {
        var blockers = new BlockerRepository(_fixture.Database);
        var id = AddProject();

        blockers.Add(new Blocker { ProjectId = id, Description = "open one" });
        blockers.Add(new Blocker { ProjectId = id, Description = "closed one", IsResolved = true, ResolvedAt = DateTimeOffset.Now });

        var open = blockers.GetOpen(id);
        Assert.Single(open);
        Assert.Equal("open one", open[0].Description);
        Assert.Equal(2, blockers.GetForProject(id).Count);
    }

    [Fact]
    public void Timeline_returns_newest_first_and_respects_limit()
    {
        var timeline = new TimelineRepository(_fixture.Database);
        var id = AddProject();
        var now = DateTimeOffset.Now;

        timeline.Add(new TimelineEntry { ProjectId = id, Message = "oldest", Timestamp = now.AddDays(-2) });
        timeline.Add(new TimelineEntry { ProjectId = id, Message = "newest", Timestamp = now });
        timeline.Add(new TimelineEntry { ProjectId = id, Message = "middle", Timestamp = now.AddDays(-1) });

        var entries = timeline.GetForProject(id, 2);
        Assert.Equal(2, entries.Count);
        Assert.Equal("newest", entries[0].Message);
        Assert.Equal("middle", entries[1].Message);
    }

    [Fact]
    public void Build_history_is_append_only_and_latest_is_by_time()
    {
        var builds = new BuildHistoryRepository(_fixture.Database);
        var id = AddProject();
        var now = DateTimeOffset.Now;

        builds.Add(new BuildRecord { ProjectId = id, Command = "make", StartedAt = now.AddMinutes(-10), ExitCode = 1, OutputLog = "boom" });
        builds.Add(new BuildRecord { ProjectId = id, Command = "make", StartedAt = now, ExitCode = 0, OutputLog = "ok" });

        var latest = builds.GetLatest(id);
        Assert.NotNull(latest);
        Assert.True(latest.Succeeded);
        Assert.Equal(2, builds.GetForProject(id).Count);
    }

    [Fact]
    public void Test_history_roundtrips_result_and_commit()
    {
        var tests = new TestHistoryRepository(_fixture.Database);
        var id = AddProject();

        tests.Add(new TestRecord
        {
            ProjectId = id,
            Command = "pytest",
            Result = TestResult.Failed,
            StartedAt = DateTimeOffset.Now,
            Duration = TimeSpan.FromSeconds(3),
            Notes = "2 failures",
            CommitSha = "abcdef1234567890",
        });

        var latest = tests.GetLatest(id);
        Assert.NotNull(latest);
        Assert.Equal(TestResult.Failed, latest.Result);
        Assert.Equal("2 failures", latest.Notes);
        Assert.Equal("abcdef1234567890", latest.CommitSha);
    }

    [Fact]
    public void Milestones_keep_sort_order()
    {
        var milestones = new MilestoneRepository(_fixture.Database);
        var id = AddProject();

        milestones.Add(new Milestone { ProjectId = id, Title = "second", SortOrder = 1 });
        milestones.Add(new Milestone { ProjectId = id, Title = "first", SortOrder = 0 });

        var items = milestones.GetForProject(id);
        Assert.Equal("first", items[0].Title);
        Assert.Equal("second", items[1].Title);
    }
}
