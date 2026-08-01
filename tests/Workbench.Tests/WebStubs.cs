using Workbench.Core.Models;
using Workbench.Core.Services;

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

    public sealed class Git(Func<string?, RepositorySnapshot> snapshotFor) : IGitService
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
        public string? GetWorkingTreeDiff(string? repositoryPath) => null;
    }

    public sealed class Docker(
        Func<Project, string?> resolve,
        Func<Project, ComposeProjectStatus?> inspect) : IDockerComposeService
    {
        public Docker(string? composeFile, ComposeProjectStatus? status)
            : this(_ => composeFile, _ => status)
        {
        }

        public int InspectCount { get; private set; }

        public string? ResolveComposeFile(Project project) => resolve(project);

        public Task<ComposeProjectStatus?> InspectAsync(Project project, CancellationToken cancellationToken = default)
        {
            InspectCount++;
            return Task.FromResult(inspect(project));
        }

        public Task<ComposeOperationResult> ExecuteAsync(
            Project project, ComposeOperation operation, bool confirmed = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The web dashboard never executes compose operations.");
    }

    public sealed class Tmux(TmuxServerStatus status) : ITmuxService
    {
        public Task<TmuxServerStatus> InspectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<bool> SessionExistsAsync(string sessionName, CancellationToken cancellationToken = default) =>
            Task.FromResult(status.Sessions.Any(s => s.Name == sessionName));

        public string ProjectSessionName(Project project) => project.TmuxSession ?? "slug";

        public Task<TmuxOperationResult> ExecuteAsync(
            TmuxOperation operation, bool confirmed = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The web dashboard never executes tmux operations.");
    }
}
