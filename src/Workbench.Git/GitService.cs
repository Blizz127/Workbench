using LibGit2Sharp;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Git;

/// <summary>
/// LibGit2Sharp-backed implementation of <see cref="IGitService"/>. All git
/// types stay inside this assembly; callers only see Core models, so the git
/// backend can be swapped (e.g. for a CLI abstraction) without touching the UI.
/// </summary>
public sealed class GitService : IGitService
{
    public RepositorySnapshot GetSnapshot(string? repositoryPath, int recentCommitCount = 10)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return RepositorySnapshot.Invalid("No repository path configured.");

        if (!Directory.Exists(repositoryPath))
            return RepositorySnapshot.Invalid($"Path does not exist: {repositoryPath}");

        try
        {
            var discovered = Repository.Discover(repositoryPath);
            if (discovered is null)
                return RepositorySnapshot.Invalid("Not a git repository.");

            using var repo = new Repository(discovered);

            var branchName = repo.Head.FriendlyName;
            var lastCommit = repo.Head.Tip is { } tip ? ToCommitInfo(tip) : null;

            var recentCommits = repo.Head.Tip is null
                ? new List<CommitInfo>()
                : repo.Commits
                    .QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Time })
                    .Take(recentCommitCount)
                    .Select(ToCommitInfo)
                    .ToList();

            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            });

            var changes = status
                .Where(e => e.State != FileStatus.Ignored)
                .Select(e => new FileChange(e.FilePath, Describe(e.State)))
                .OrderBy(c => c.Path, StringComparer.Ordinal)
                .ToList();

            var tracking = repo.Head.TrackingDetails;

            return new RepositorySnapshot(
                IsValid: true,
                BranchName: branchName,
                LastCommit: lastCommit,
                RecentCommits: recentCommits,
                ModifiedFiles: changes,
                IsDirty: changes.Count > 0,
                Error: null,
                AheadBy: tracking.AheadBy,
                BehindBy: tracking.BehindBy);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return RepositorySnapshot.Invalid($"Failed to read repository: {ex.Message}");
        }
    }

    public string? GetHeadSha(string? repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            return null;

        try
        {
            var discovered = Repository.Discover(repositoryPath);
            if (discovered is null)
                return null;

            using var repo = new Repository(discovered);
            return repo.Head.Tip?.Sha;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? GetWorkingTreeDiff(string? repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            return null;

        try
        {
            var discovered = Repository.Discover(repositoryPath);
            if (discovered is null)
                return null;

            using var repo = new Repository(discovered);
            if (repo.Head.Tip is not { } tip)
                return null; // unborn HEAD — nothing to diff against

            var patch = repo.Diff.Compare<Patch>(
                tip.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory);
            return patch.Content;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<CommitFileChange> GetCommitFiles(string? repositoryPath, string sha)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || string.IsNullOrWhiteSpace(sha)
            || !Directory.Exists(repositoryPath))
            return [];

        try
        {
            var discovered = Repository.Discover(repositoryPath);
            if (discovered is null)
                return [];

            using var repo = new Repository(discovered);
            var commit = repo.Lookup<Commit>(sha);
            if (commit is null)
                return [];

            var parentTree = commit.Parents.FirstOrDefault()?.Tree;
            var changes = parentTree is null
                ? repo.Diff.Compare<TreeChanges>(null, commit.Tree)
                : repo.Diff.Compare<TreeChanges>(parentTree, commit.Tree);

            return changes
                .Select(c => new CommitFileChange(
                    c.Path,
                    c.Status switch
                    {
                        ChangeKind.Added => "added",
                        ChangeKind.Deleted => "deleted",
                        ChangeKind.Renamed => "renamed",
                        _ => "modified",
                    }))
                .OrderBy(c => c.Path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string? GetFileContentAtCommit(string? repositoryPath, string sha, string filePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || string.IsNullOrWhiteSpace(sha)
            || string.IsNullOrWhiteSpace(filePath) || !Directory.Exists(repositoryPath))
            return null;

        try
        {
            var discovered = Repository.Discover(repositoryPath);
            if (discovered is null)
                return null;

            using var repo = new Repository(discovered);
            var commit = repo.Lookup<Commit>(sha);
            if (commit?[filePath]?.Target is not Blob blob)
                return null;

            if (blob.IsBinary || blob.Size > MaxBlobBytes)
                return null;

            return blob.GetContentText();
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Largest blob served by <see cref="GetFileContentAtCommit"/> (512 KB).</summary>
    private const int MaxBlobBytes = 512 * 1024;

    private static CommitInfo ToCommitInfo(Commit commit) => new(
        Sha: commit.Sha,
        ShortSha: commit.Sha[..7],
        MessageSummary: commit.MessageShort,
        Author: commit.Author.Name,
        When: commit.Author.When);

    private static string Describe(FileStatus state)
    {
        if (state.HasFlag(FileStatus.Conflicted)) return "Conflicted";
        if (state.HasFlag(FileStatus.NewInWorkdir) || state.HasFlag(FileStatus.NewInIndex)) return "Added";
        if (state.HasFlag(FileStatus.DeletedFromWorkdir) || state.HasFlag(FileStatus.DeletedFromIndex)) return "Deleted";
        if (state.HasFlag(FileStatus.RenamedInWorkdir) || state.HasFlag(FileStatus.RenamedInIndex)) return "Renamed";
        if (state.HasFlag(FileStatus.ModifiedInWorkdir) || state.HasFlag(FileStatus.ModifiedInIndex)) return "Modified";
        return state.ToString();
    }
}
