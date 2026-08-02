using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Web;

namespace Workbench.Tests;

/// <summary>
/// Stub implementations of every service the web dashboard composes. Nothing
/// here touches a process, a database, Docker, git, or tmux — the web tests
/// run entirely in memory.
/// </summary>
internal static class WebStubs
{
    public sealed class SystemHealth : ISystemHealthService
    {
        public Task<SystemHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemHealthSnapshot(
                CpuUsagePercent: 25,
                UsedMemoryBytes: 4L << 30, TotalMemoryBytes: 16L << 30,
                UsedDiskBytes: 100L << 30, TotalDiskBytes: 512L << 30,
                Uptime: TimeSpan.FromHours(30)));
    }

    public sealed class Projects(params Project[] projects) : IProjectRepository
    {
        public IReadOnlyList<Project> GetAll() => projects;
        public Project? GetById(long id) => projects.FirstOrDefault(p => p.Id == id);
        public long Add(Project project) => throw new NotSupportedException();
        public void Update(Project project) => throw new NotSupportedException();
        public void Delete(long id) => throw new NotSupportedException();
        public void UpdateSharedSync(Project project) => throw new NotSupportedException();
    }

    public sealed class Timeline(params TimelineEntry[] entries) : ITimelineRepository
    {
        public IReadOnlyList<TimelineEntry> GetForProject(long projectId, int limit = 200) =>
            entries.Where(e => e.ProjectId == projectId).Take(limit).ToList();

        public IReadOnlyList<TimelineEntry> GetLatestByCategory(long projectId, TimelineCategory category, int limit = 50) =>
            entries.Where(e => e.ProjectId == projectId && e.Category == category).Take(limit).ToList();

        public long Add(TimelineEntry entry) => throw new NotSupportedException();
    }

    /// <summary>In-memory blockers for the read-only detail summary; mutations are not supported.</summary>
    public sealed class Blockers(params Blocker[] blockers) : IBlockerRepository
    {
        public IReadOnlyList<Blocker> GetForProject(long projectId) =>
            blockers.Where(b => b.ProjectId == projectId).ToList();

        public IReadOnlyList<Blocker> GetOpen(long projectId) =>
            blockers.Where(b => b.ProjectId == projectId && !b.IsResolved).ToList();

        public long Add(Blocker blocker) => throw new NotSupportedException();
        public void Update(Blocker blocker) => throw new NotSupportedException();
        public void Delete(long id) => throw new NotSupportedException();
    }

    public sealed class Git(
        Func<string?, RepositorySnapshot> snapshotFor,
        Func<string?, string?>? workingTreeDiffFor = null,
        Func<string?, string, IReadOnlyList<CommitFileChange>>? commitFilesFor = null,
        Func<string?, string, string, string?>? fileContentAtFor = null) : IGitService
    {
        public Git(RepositorySnapshot snapshot) : this(_ => snapshot)
        {
        }

        public int CallCount { get; private set; }

        public RepositorySnapshot GetSnapshot(string? repositoryPath, int recentCommitCount = 10)
        {
            CallCount++;
            return snapshotFor(repositoryPath);
        }

        public string? GetHeadSha(string? repositoryPath) => snapshotFor(repositoryPath).LastCommit?.Sha;

        public string? GetWorkingTreeDiff(string? repositoryPath) =>
            workingTreeDiffFor?.Invoke(repositoryPath);

        public IReadOnlyList<CommitFileChange> GetCommitFiles(string? repositoryPath, string sha) =>
            commitFilesFor?.Invoke(repositoryPath, sha) ?? [];

        public string? GetFileContentAtCommit(string? repositoryPath, string sha, string filePath) =>
            fileContentAtFor?.Invoke(repositoryPath, sha, filePath);
    }

    public sealed class Docker(
        Func<Project, string?> resolve,
        Func<Project, ComposeProjectStatus?> inspect,
        Func<Project, ComposeOperation, bool, Task<ComposeOperationResult>>? execute = null) : IDockerComposeService
    {
        public Docker(string? composeFile, ComposeProjectStatus? status)
            : this(_ => composeFile, _ => status)
        {
        }

        public int InspectCount { get; private set; }

        /// <summary>Every operation passed to ExecuteAsync, in order.</summary>
        public List<(ComposeOperation Operation, bool Confirmed)> Executed { get; } = [];

        public string? ResolveComposeFile(Project project) => resolve(project);

        public Task<ComposeProjectStatus?> InspectAsync(Project project, CancellationToken cancellationToken = default)
        {
            InspectCount++;
            return Task.FromResult(inspect(project));
        }

        public Task<ComposeOperationResult> ExecuteAsync(
            Project project, ComposeOperation operation, bool confirmed = false, CancellationToken cancellationToken = default)
        {
            if (execute is null)
                throw new NotSupportedException("The web dashboard never executes compose operations.");

            Executed.Add((operation, confirmed));
            return execute(project, operation, confirmed);
        }
    }

    public sealed class Tmux(
        TmuxServerStatus status,
        Func<TmuxOperation, bool, Task<TmuxOperationResult>>? execute = null,
        Func<string, bool>? sessionExists = null) : ITmuxService
    {
        /// <summary>Every operation passed to ExecuteAsync, in order.</summary>
        public List<TmuxOperation> Executed { get; } = [];

        public Task<TmuxServerStatus> InspectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<bool> SessionExistsAsync(string sessionName, CancellationToken cancellationToken = default) =>
            Task.FromResult(sessionExists?.Invoke(sessionName)
                ?? status.Sessions.Any(s => s.Name == sessionName));

        public string ProjectSessionName(Project project) => project.TmuxSession ?? "slug";

        public Task<TmuxOperationResult> ExecuteAsync(
            TmuxOperation operation, bool confirmed = false, CancellationToken cancellationToken = default)
        {
            if (execute is null)
                throw new NotSupportedException("The web dashboard never executes tmux operations.");

            Executed.Add(operation);
            return execute(operation, confirmed);
        }
    }

    /// <summary>workbench.json contents per repository path — no filesystem involved.</summary>
    public sealed class ProjectConfigs(Func<string, ProjectConfig?> load) : IProjectConfigService
    {
        public string GetConfigPath(string repositoryPath) => Path.Combine(repositoryPath, "workbench.json");
        public ProjectConfig? Load(string repositoryPath) => load(repositoryPath);
        public void Save(Project project) => throw new NotSupportedException();
        public void ApplyTo(ProjectConfig config, Project project) => throw new NotSupportedException();
        public ProjectConfig FromProject(Project project) => throw new NotSupportedException();
    }

    /// <summary>Scripted predefined-command runs; records every call.</summary>
    public sealed class PredefinedCommands(
        Func<Project, PredefinedCommandKind, string, Task<PredefinedCommandResult?>> run) : IPredefinedCommandRunner
    {
        public static PredefinedCommands NeverCalled() =>
            new((_, _, _) => throw new NotSupportedException("No predefined command should run."));

        public List<(PredefinedCommandKind Kind, string Command)> Calls { get; } = [];

        public Task<PredefinedCommandResult?> RunAsync(
            Project project, PredefinedCommandKind kind, string command, CancellationToken cancellationToken = default)
        {
            Calls.Add((kind, command));
            return run(project, kind, command);
        }
    }

    /// <summary>In-memory audit repository — captures every record the service writes.</summary>
    public sealed class MobileActionAudits : IMobileActionAuditRepository
    {
        public List<MobileActionAuditRecord> Records { get; } = [];
        private long _nextId;

        public long Add(MobileActionAuditRecord record)
        {
            record.Id = ++_nextId;
            Records.Add(record);
            return record.Id;
        }

        public IReadOnlyList<MobileActionAuditRecord> Query(MobileActionAuditFilter filter) =>
            Records.Where(r =>
                    (filter.ProjectId is null || r.ProjectId == filter.ProjectId)
                    && (filter.ActionId is null || r.ActionId == filter.ActionId)
                    && (filter.State is null || r.State == filter.State)
                    && (filter.FromUtc is null || r.TimestampUtc >= filter.FromUtc)
                    && (filter.ToUtc is null || r.TimestampUtc <= filter.ToUtc))
                .OrderByDescending(r => r.TimestampUtc).ThenByDescending(r => r.Id)
                .Take(filter.Limit).ToList();

        public MobileActionAuditRecord? GetById(long id) => Records.FirstOrDefault(r => r.Id == id);

        public int EnforceRetention(int maxRecords, int maxAgeDays) => 0;
    }

    /// <summary>Audit repository whose every member throws — persistence-failure simulation.</summary>
    public sealed class ThrowingAudits : IMobileActionAuditRepository
    {
        public long Add(MobileActionAuditRecord record) => throw new InvalidOperationException("db is down");
        public IReadOnlyList<MobileActionAuditRecord> Query(MobileActionAuditFilter filter) =>
            throw new InvalidOperationException("db is down");
        public MobileActionAuditRecord? GetById(long id) => throw new InvalidOperationException("db is down");
        public int EnforceRetention(int maxRecords, int maxAgeDays) =>
            throw new InvalidOperationException("db is down");
    }
}
