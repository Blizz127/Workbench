using LibGit2Sharp;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Workbench.Git;
using Xunit;
using Signature = LibGit2Sharp.Signature;

namespace Workbench.Tests;

/// <summary>
/// Exercises GitSyncService's fetch/pull/push safety logic against real
/// origin+clone git repositories (temp-directory-local, no network) and the
/// real <c>git</c> CLI (via ProcessRunner) — not a mock, so a regression in
/// the actual command invocation would be caught, not just the C# logic
/// around it.
/// </summary>
public sealed class GitSyncServiceTests : IClassFixture<DatabaseFixture>, IDisposable
{
    private readonly DatabaseFixture _fixture;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));
    private readonly GitService _git = new();
    private readonly ProcessRunner _runner = new();
    private static readonly Signature Author = new("Test Author", "test@example.com", DateTimeOffset.Now);

    public GitSyncServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { Directory.Delete(_root, recursive: true); }
    }

    private GitSyncService CreateService() => new(_git, new TimelineRepository(_fixture.Database), _runner);

    private Project CreateProject(string repositoryPath)
    {
        var projects = new ProjectRepository(_fixture.Database);
        var project = new Project { Name = $"gitsync-{Guid.NewGuid():N}", RepositoryPath = repositoryPath };
        projects.Add(project);
        return project;
    }

    /// <summary>Origin + clone, one initial commit, clone tracking origin, both in sync.</summary>
    private (string OriginPath, string ClonePath) CreateOriginAndClone([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var originPath = Path.Combine(_root, $"{name}-origin");
        var clonePath = Path.Combine(_root, $"{name}-clone");

        Repository.Init(originPath);
        File.WriteAllText(Path.Combine(originPath, "shared.txt"), "v1\n");
        using (var origin = new Repository(originPath))
        {
            Commands.Stage(origin, "*");
            origin.Commit("initial commit", Author, Author);

            // "origin" is a normal (non-bare) repo with its branch checked
            // out; by default git refuses a push into whatever branch is
            // currently checked out in the receiving repo. This test double
            // stands in for a real remote (which would be bare), so relax
            // that specific safety check rather than model a full bare repo.
            origin.Config.Set("receive.denyCurrentBranch", "updateInstead");
        }

        Repository.Clone(originPath, clonePath);
        using (var clone = new Repository(clonePath))
        {
            var branch = clone.Head;
            clone.Branches.Update(branch, b => b.Remote = "origin", b => b.UpstreamBranch = branch.CanonicalName);
        }

        return (originPath, clonePath);
    }

    private static void CommitFile(string repoPath, string fileName, string content, string message)
    {
        File.WriteAllText(Path.Combine(repoPath, fileName), content);
        using var repo = new Repository(repoPath);
        Commands.Stage(repo, "*");
        repo.Commit(message, Author, Author);
    }

    [Fact]
    public async Task Fetch_updates_remote_tracking_state_and_journals_success()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        CommitFile(originPath, "new.txt", "new\n", "origin-only commit");

        var project = CreateProject(clonePath);
        var service = CreateService();

        var result = await service.FetchAsync(project);

        Assert.Equal(GitSyncStatus.Succeeded, result.Status);
        Assert.Equal(0, result.AheadByAfter);
        Assert.Equal(1, result.BehindByAfter);

        var timeline = new TimelineRepository(_fixture.Database);
        Assert.Contains(timeline.GetForProject(project.Id),
            e => e.Category == TimelineCategory.GitSync && e.Message.Contains("fetch"));
    }

    [Fact]
    public async Task Clean_fast_forward_pull_is_allowed_and_advances_head()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        var newCommitSha = CommitFileAndReturnSha(originPath, "new.txt", "new\n", "origin-only commit");
        await service.FetchAsync(project); // populate accurate ahead/behind before pulling

        var result = await service.PullFastForwardAsync(project);

        Assert.Equal(GitSyncStatus.Succeeded, result.Status);
        Assert.Equal(newCommitSha, result.HeadShaAfter);
        Assert.Equal(0, result.BehindByAfter);
        Assert.True(File.Exists(Path.Combine(clonePath, "new.txt")));
    }

    [Fact]
    public async Task Dirty_working_tree_blocks_pull_without_running_git()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        CommitFile(originPath, "new.txt", "new\n", "origin-only commit");
        await service.FetchAsync(project);

        // Make the clone's working tree dirty.
        File.WriteAllText(Path.Combine(clonePath, "shared.txt"), "locally edited, uncommitted\n");
        var headBefore = _git.GetHeadSha(clonePath);

        var result = await service.PullFastForwardAsync(project);

        Assert.Equal(GitSyncStatus.Blocked, result.Status);
        Assert.Contains("uncommitted changes", result.BlockReason);
        // Nothing was touched: HEAD didn't move and the dirty edit survives untouched.
        Assert.Equal(headBefore, _git.GetHeadSha(clonePath));
        Assert.Equal("locally edited, uncommitted\n", File.ReadAllText(Path.Combine(clonePath, "shared.txt")));
    }

    [Fact]
    public async Task Diverged_branch_blocks_pull_without_merging_or_rebasing()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        CommitFile(originPath, "origin-only.txt", "origin\n", "origin-only commit");
        CommitFile(clonePath, "local-only.txt", "local\n", "local-only commit");
        await service.FetchAsync(project);

        var headBefore = _git.GetHeadSha(clonePath);
        var result = await service.PullFastForwardAsync(project);

        Assert.Equal(GitSyncStatus.Blocked, result.Status);
        Assert.Contains("diverged", result.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manually", result.BlockReason, StringComparison.OrdinalIgnoreCase);
        // Nothing was merged or rebased: HEAD is exactly what it was before the attempt.
        Assert.Equal(headBefore, _git.GetHeadSha(clonePath));
    }

    [Fact]
    public async Task Ahead_branch_pushes_successfully()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        var localSha = CommitFileAndReturnSha(clonePath, "local.txt", "local\n", "local commit to push");

        var result = await service.PushAsync(project);

        Assert.Equal(GitSyncStatus.Succeeded, result.Status);
        Assert.Null(result.Warning);

        // Verify the commit actually landed on "origin".
        using var origin = new Repository(originPath);
        Assert.Equal(localSha, origin.Head.Tip.Sha);
    }

    [Fact]
    public async Task Dirty_but_ahead_branch_pushes_with_a_warning()
    {
        var (_, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        CommitFile(clonePath, "local.txt", "local\n", "local commit to push");
        // Uncommitted change on top of the committed one.
        File.WriteAllText(Path.Combine(clonePath, "shared.txt"), "uncommitted edit\n");

        var result = await service.PushAsync(project);

        Assert.Equal(GitSyncStatus.Succeeded, result.Status);
        Assert.NotNull(result.Warning);
        Assert.Contains("uncommitted changes", result.Warning);
        Assert.Contains("only committed changes will be pushed", result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_upstream_blocks_push_with_a_setup_warning_and_never_creates_one()
    {
        var repoPath = Path.Combine(_root, nameof(No_upstream_blocks_push_with_a_setup_warning_and_never_creates_one));
        Repository.Init(repoPath);
        CommitFile(repoPath, "a.txt", "hello\n", "initial commit");

        var project = CreateProject(repoPath);
        var service = CreateService();

        var result = await service.PushAsync(project);

        Assert.Equal(GitSyncStatus.Blocked, result.Status);
        Assert.Contains("No upstream", result.BlockReason);
        // No remote was created as a side effect of trying to push.
        using var repo = new Repository(repoPath);
        Assert.Empty(repo.Network.Remotes);
    }

    [Fact]
    public async Task No_upstream_blocks_pull_too()
    {
        var repoPath = Path.Combine(_root, nameof(No_upstream_blocks_pull_too));
        Repository.Init(repoPath);
        CommitFile(repoPath, "a.txt", "hello\n", "initial commit");

        var project = CreateProject(repoPath);
        var service = CreateService();

        var result = await service.PullFastForwardAsync(project);

        Assert.Equal(GitSyncStatus.Blocked, result.Status);
        Assert.Contains("No upstream", result.BlockReason);
    }

    [Fact]
    public async Task Blocked_and_succeeded_results_are_both_journaled_with_before_after_state()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        CommitFile(originPath, "new.txt", "new\n", "origin-only commit");
        await service.FetchAsync(project);

        // Dirty -> blocked.
        File.WriteAllText(Path.Combine(clonePath, "shared.txt"), "dirty\n");
        await service.PullFastForwardAsync(project);

        // Clean it up (restore the file to match HEAD exactly) -> should now succeed.
        File.WriteAllText(Path.Combine(clonePath, "shared.txt"), "v1\n");
        await service.PullFastForwardAsync(project);

        var timeline = new TimelineRepository(_fixture.Database);
        var entries = timeline.GetForProject(project.Id);

        Assert.Contains(entries, e => e.Category == TimelineCategory.GitSync && e.Message.Contains("blocked"));
        Assert.Contains(entries, e => e.Category == TimelineCategory.GitSync && e.Message.Contains("succeeded"));
    }

    [Fact]
    public async Task Journaled_entry_detail_captures_branch_and_before_after_head_and_tracking_state()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        var project = CreateProject(clonePath);
        var service = CreateService();

        var newSha = CommitFileAndReturnSha(originPath, "new.txt", "new\n", "origin-only commit");
        var headBefore = _git.GetHeadSha(clonePath);
        await service.FetchAsync(project);

        var result = await service.PullFastForwardAsync(project);

        var timeline = new TimelineRepository(_fixture.Database);
        var entry = timeline.GetForProject(project.Id)
            .First(e => e.Category == TimelineCategory.GitSync && e.Message.Contains("pull"));

        Assert.NotNull(entry.Detail);
        Assert.Contains(result.Branch!, entry.Detail);
        Assert.Contains(headBefore![..7], entry.Detail);
        Assert.Contains(newSha[..7], entry.Detail);
        Assert.Contains("0/1 -> 0/0", entry.Detail); // ahead/behind before -> after
    }

    /// <summary>
    /// Locks in the safety invariant as an executable check, not just a code
    /// reading: whatever exact command string GitSyncService hands to the
    /// process runner for a real fetch/pull/push cycle, it never carries a
    /// force or branch-delete flag. Wraps the real <see cref="ProcessRunner"/>
    /// (rather than faking it) so the fetch/pull/push still genuinely execute
    /// against real repositories — this only adds an observation point.
    /// </summary>
    [Fact]
    public async Task Fetch_pull_and_push_commands_never_carry_force_or_branch_delete_flags()
    {
        var (originPath, clonePath) = CreateOriginAndClone();
        CommitFile(originPath, "new.txt", "new\n", "origin-only commit");

        var project = CreateProject(clonePath);
        var recorder = new RecordingProcessRunner(_runner);
        var service = new GitSyncService(_git, new TimelineRepository(_fixture.Database), recorder);

        await service.FetchAsync(project);
        await service.PullFastForwardAsync(project);
        CommitFile(clonePath, "local.txt", "local\n", "local commit to push");
        await service.PushAsync(project);

        Assert.Equal(["git fetch --all --prune", "git pull --ff-only", "git push"], recorder.Commands);
        Assert.All(recorder.Commands, cmd =>
        {
            Assert.DoesNotContain("force", cmd, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("delete", cmd, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"(?<!\S)-[Ddf](?!\S)", cmd);
        });
    }

    private static string CommitFileAndReturnSha(string repoPath, string fileName, string content, string message)
    {
        File.WriteAllText(Path.Combine(repoPath, fileName), content);
        using var repo = new Repository(repoPath);
        Commands.Stage(repo, "*");
        return repo.Commit(message, Author, Author).Sha;
    }

    /// <summary>Records every command string passed through, then delegates to a real runner.</summary>
    private sealed class RecordingProcessRunner(IProcessRunner inner) : IProcessRunner
    {
        public List<string> Commands { get; } = [];

        public Task<ProcessResult> RunAsync(
            string command, string? workingDirectory, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return inner.RunAsync(command, workingDirectory, timeout, cancellationToken);
        }
    }
}
