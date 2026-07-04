using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data;
using Workbench.Data.Repositories;
using Workbench.Git;

namespace Workbench.App;

/// <summary>
/// Composition root. Wires concrete implementations to the Core interfaces;
/// swapping a subsystem (e.g. a git CLI backend, or an AI provider module in
/// later phases) only touches this class.
/// </summary>
public sealed class AppServices
{
    public WorkbenchDatabase Database { get; }

    public IProjectRepository Projects { get; }
    public IMilestoneRepository Milestones { get; }
    public IFrontierRepository Frontiers { get; }
    public IBlockerRepository Blockers { get; }
    public ITimelineRepository Timeline { get; }
    public INoteRepository Notes { get; }
    public IBuildHistoryRepository Builds { get; }
    public ITestHistoryRepository Tests { get; }
    public IProjectCommandRepository ProjectCommands { get; }
    public ICommandRunRepository CommandRuns { get; }

    public IGitService Git { get; }
    public ITodoScanner Todos { get; }
    public ISearchService Search { get; }
    public IHandoffService Handoff { get; }
    public IProcessRunner Runner { get; }
    public IProjectConfigService Config { get; }
    public ILogPatternAnalyzer LogPatterns { get; }
    public IHandoffCheckpointWriter CheckpointWriter { get; }
    public IPromptGeneratorService PromptGenerator { get; }
    public ExecutionService Execution { get; }
    public CommandRunnerService CommandRunner { get; }
    public IGitSyncService GitSync { get; }

    public AppServices(string? databasePath = null)
    {
        Database = new WorkbenchDatabase(databasePath ?? WorkbenchDatabase.GetDefaultDatabasePath());

        Projects = new ProjectRepository(Database);
        Milestones = new MilestoneRepository(Database);
        Frontiers = new FrontierRepository(Database);
        Blockers = new BlockerRepository(Database);
        Timeline = new TimelineRepository(Database);
        Notes = new NoteRepository(Database);
        Builds = new BuildHistoryRepository(Database);
        Tests = new TestHistoryRepository(Database);
        ProjectCommands = new ProjectCommandRepository(Database);
        CommandRuns = new CommandRunRepository(Database);

        Git = new GitService();
        Todos = new TodoScanner();
        Search = new SearchService(Database);
        Handoff = new HandoffService();
        Runner = new ProcessRunner();
        Config = new ProjectConfigService();
        LogPatterns = new LogPatternAnalyzer();
        CheckpointWriter = new HandoffCheckpointWriter();
        PromptGenerator = new PromptGeneratorService();
        Execution = new ExecutionService(Builds, Tests, Timeline, Git, Runner);
        CommandRunner = new CommandRunnerService(CommandRuns, Timeline, Git, Runner);
        GitSync = new GitSyncService(Git, Timeline, Runner);
    }

    /// <summary>Gathers everything the handoff generator needs for one project.</summary>
    public HandoffContext BuildHandoffContext(Project project)
    {
        var milestones = Milestones.GetForProject(project.Id);
        return new HandoffContext(
            Project: project,
            CurrentFrontier: Frontiers.GetCurrent(project.Id),
            NextFrontier: Frontiers.GetNext(project.Id),
            OpenBlockers: Blockers.GetOpen(project.Id),
            CurrentMilestone: milestones.FirstOrDefault(m => !m.IsCompleted),
            Milestones: milestones,
            RecentTimeline: Timeline.GetForProject(project.Id, 20),
            LastBuild: Builds.GetLatest(project.Id),
            LastTest: Tests.GetLatest(project.Id),
            Repository: Git.GetSnapshot(project.RepositoryPath));
    }

    /// <summary>
    /// Finds the most recent failing command across every history source
    /// (build/run, test, and named command runs) for the given project, for
    /// use in the generated model prompt.
    /// </summary>
    public FailedCommandInfo? FindLastFailedCommand(long projectId)
    {
        var candidates = new List<FailedCommandInfo>();

        var build = Builds.GetForProject(projectId, limit: 20).FirstOrDefault(b => !b.Succeeded);
        if (build is not null)
        {
            candidates.Add(new FailedCommandInfo(
                build.Kind.ToString(), build.Command, build.StartedAt, build.ExitCode,
                Builds.GetOutputLog(build.Id) ?? string.Empty));
        }

        var test = Tests.GetForProject(projectId, limit: 20).FirstOrDefault(t => t.Result != TestResult.Passed);
        if (test is not null)
        {
            candidates.Add(new FailedCommandInfo(
                "test", test.Command, test.StartedAt, test.Result == TestResult.Passed ? 0 : 1,
                Tests.GetOutputLog(test.Id) ?? string.Empty));
        }

        var namedRun = CommandRuns.GetForProject(projectId, limit: 20).FirstOrDefault(r => !r.Succeeded);
        if (namedRun is not null)
        {
            candidates.Add(new FailedCommandInfo(
                namedRun.CommandName, namedRun.CommandText, namedRun.StartedAt, namedRun.ExitCode,
                CommandRuns.GetOutputLog(namedRun.Id) ?? string.Empty));
        }

        return candidates.OrderByDescending(c => c.StartedAt).FirstOrDefault();
    }
}
