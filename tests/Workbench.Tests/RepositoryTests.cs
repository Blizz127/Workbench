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
            MatchingBuildCommand = "make -B build",
            DocumentationPaths = ["docs/", "notes.md"],
            AiContextPaths = ["include/"],
            PreferredAiProvider = "none-yet",
            PromptRules = "Never touch asm/",
            CustomLogPatterns = [new LogPatternDefinition("RUN_RC=", "RUN_RC=", Category: "TestHarness")],
            Tags = ["Reverse Engineering", "PSX"],
            ActiveTask = "task",
            CurrentObjective = "objective",
            LastKnownBranch = "main",
            LastKnownCommitSha = "abc123",
            LastKnownIsDirty = true,
            LastKnownAheadBy = 2,
            LastKnownBehindBy = 1,
            LastRefreshedAt = DateTimeOffset.Now,
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
        Assert.Equal("make -B build", loaded.MatchingBuildCommand);
        Assert.Equal("Never touch asm/", loaded.PromptRules);
        Assert.Single(loaded.CustomLogPatterns);
        Assert.Equal("RUN_RC=", loaded.CustomLogPatterns[0].Name);
        Assert.Equal("main", loaded.LastKnownBranch);
        Assert.Equal("abc123", loaded.LastKnownCommitSha);
        Assert.True(loaded.LastKnownIsDirty);
        Assert.Equal(2, loaded.LastKnownAheadBy);
        Assert.Equal(1, loaded.LastKnownBehindBy);
        Assert.NotNull(loaded.LastRefreshedAt);
        Assert.Equal("task", loaded.ActiveTask);
        Assert.Equal("objective", loaded.CurrentObjective);
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
    public void Frontier_roundtrips_symbol_evidence_blocker_class_next_step_and_assigned_model()
    {
        var frontiers = new FrontierRepository(_fixture.Database);
        var id = AddProject();

        var frontier = new Frontier
        {
            ProjectId = id,
            Title = "func_8001FBE4",
            Status = FrontierStatus.Current,
            Symbol = "func_8001FBE4",
            Evidence = "55% matched per matching-build",
            BlockerClass = "Unknown ABI",
            NextSafestStep = "Add a diag breakpoint",
            AssignedModel = "claude-sonnet-5",
        };
        frontiers.Add(frontier);

        var loaded = frontiers.GetCurrent(id)!;
        Assert.Equal("func_8001FBE4", loaded.Symbol);
        Assert.Equal("55% matched per matching-build", loaded.Evidence);
        Assert.Equal("Unknown ABI", loaded.BlockerClass);
        Assert.Equal("Add a diag breakpoint", loaded.NextSafestStep);
        Assert.Equal("claude-sonnet-5", loaded.AssignedModel);

        loaded.BlockerClass = "Resolved";
        frontiers.Update(loaded);
        Assert.Equal("Resolved", frontiers.GetCurrent(id)!.BlockerClass);
    }

    [Fact]
    public void ProjectCommand_roundtrips_and_orders_by_sort_order()
    {
        var commands = new ProjectCommandRepository(_fixture.Database);
        var id = AddProject();

        commands.Add(new ProjectCommand { ProjectId = id, Name = "second", CommandText = "echo 2", SortOrder = 1 });
        commands.Add(new ProjectCommand { ProjectId = id, Name = "first", CommandText = "echo 1", SortOrder = 0 });

        var items = commands.GetForProject(id);
        Assert.Equal("first", items[0].Name);
        Assert.Equal("second", items[1].Name);

        var toUpdate = items[0];
        toUpdate.CommandText = "echo updated";
        commands.Update(toUpdate);
        Assert.Equal("echo updated", commands.GetForProject(id)[0].CommandText);

        commands.Delete(toUpdate.Id);
        Assert.Single(commands.GetForProject(id));
    }

    [Fact]
    public void CommandRun_history_is_append_only_excludes_logs_from_lists_and_supports_get_latest_by_name()
    {
        var runs = new CommandRunRepository(_fixture.Database);
        var id = AddProject();
        var now = DateTimeOffset.Now;

        runs.Add(new CommandRun
        {
            ProjectId = id, CommandName = "matching-build", CommandText = "make -B build",
            StartedAt = now.AddMinutes(-5), ExitCode = 0, OutputLog = "old log", GitHeadSha = "aaa111",
        });
        var latestId = runs.Add(new CommandRun
        {
            ProjectId = id, CommandName = "matching-build", CommandText = "make -B build",
            StartedAt = now, ExitCode = 1, OutputLog = "newest log", RegressionDetected = true,
        });
        runs.Add(new CommandRun
        {
            ProjectId = id, CommandName = "field-repro", CommandText = "field-repro-cmd",
            StartedAt = now, ExitCode = 0, OutputLog = "unrelated command log",
        });

        var latest = runs.GetLatest(id, "matching-build");
        Assert.NotNull(latest);
        Assert.False(latest.Succeeded);
        Assert.True(latest.RegressionDetected);
        Assert.Equal(string.Empty, latest.OutputLog); // excluded from list/latest queries
        Assert.Equal("newest log", runs.GetOutputLog(latestId));

        Assert.Equal(2, runs.GetForProject(id, commandName: "matching-build").Count);
        Assert.Equal(3, runs.GetForProject(id).Count);
        Assert.Null(runs.GetOutputLog(999_999));
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
    public void History_list_queries_omit_logs_which_are_fetched_per_record()
    {
        var builds = new BuildHistoryRepository(_fixture.Database);
        var tests = new TestHistoryRepository(_fixture.Database);
        var id = AddProject();

        var buildId = builds.Add(new BuildRecord
        {
            ProjectId = id, Command = "make", StartedAt = DateTimeOffset.Now,
            ExitCode = 0, OutputLog = "big build log",
        });
        var testId = tests.Add(new TestRecord
        {
            ProjectId = id, Command = "pytest", Result = TestResult.Passed,
            StartedAt = DateTimeOffset.Now, OutputLog = "big test log",
        });

        // List and latest queries leave OutputLog empty.
        Assert.All(builds.GetForProject(id), r => Assert.Equal(string.Empty, r.OutputLog));
        Assert.Equal(string.Empty, builds.GetLatest(id)!.OutputLog);
        Assert.All(tests.GetForProject(id), r => Assert.Equal(string.Empty, r.OutputLog));
        Assert.Equal(string.Empty, tests.GetLatest(id)!.OutputLog);

        // Per-record fetch returns the stored log.
        Assert.Equal("big build log", builds.GetOutputLog(buildId));
        Assert.Equal("big test log", tests.GetOutputLog(testId));
        Assert.Null(builds.GetOutputLog(999_999));
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
