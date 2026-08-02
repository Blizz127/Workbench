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
    public IRuntimeHealthRepository RuntimeHealth { get; }

    public IGitService Git { get; }
    public ITodoScanner Todos { get; }
    public ISearchService Search { get; }
    public IHandoffService Handoff { get; }
    public IProcessRunner Runner { get; }
    public IProjectConfigService Config { get; }
    public ExecutionService Execution { get; }
    public HealthCheckService Health { get; }
    public IAppSettingsService AppSettings { get; }
    public ISharedStateService SharedState { get; }
    public IGitSyncService GitSync { get; }
    public IProjectDiscoveryService Discovery { get; }
    public ISystemHealthService SystemHealth { get; }
    public IDockerComposeService Docker { get; }
    public ITmuxService Tmux { get; }
    public ITerminalLauncher Terminal { get; }

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
        RuntimeHealth = new RuntimeHealthRepository(Database);

        Git = new GitService();
        Todos = new TodoScanner();
        Search = new SearchService(Database);
        Handoff = new HandoffService();
        Runner = new ProcessRunner();
        Config = new ProjectConfigService();
        Execution = new ExecutionService(Builds, Tests, Timeline, Git, Runner);
        Health = new HealthCheckService(RuntimeHealth, Timeline, Runner);
        AppSettings = new AppSettingsService();
        SharedState = new SharedStateService(Projects, Milestones, Frontiers, Blockers);
        GitSync = new GitSyncService(Git, Timeline, Runner);
        SystemHealth = new SystemHealthService();
        Docker = new DockerComposeService(Runner);
        Tmux = new TmuxService(Runner);
        Terminal = new TerminalLauncher();

        // The sample seeders no longer run at startup — samples/ is inert demo
        // data (tests still reference it). Projects are discovered on disk instead.
        Discovery = new ProjectDiscoveryService(Projects, Config, Timeline, AppSettings);
        try
        {
            Discovery.Scan();
        }
        catch (Exception)
        {
            // A bad discovery directory must never break startup.
        }
    }

    /// <summary>Gathers everything the handoff generator needs for one project.</summary>
    public HandoffContext BuildHandoffContext(Core.Models.Project project)
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
}
