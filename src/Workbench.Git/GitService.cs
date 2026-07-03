using System.Text.RegularExpressions;
using LibGit2Sharp;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Git;

/// <summary>
/// LibGit2Sharp-backed implementation of <see cref="IGitService"/>. All git
/// types stay inside this assembly; callers only see Core models, so the git
/// backend can be swapped (e.g. for a CLI abstraction) without touching the UI.
/// </summary>
public sealed partial class GitService : IGitService
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
            var (diffStat, diffCheckWarnings) = ComputeDiff(repo);

            return new RepositorySnapshot(
                IsValid: true,
                BranchName: branchName,
                LastCommit: lastCommit,
                RecentCommits: recentCommits,
                ModifiedFiles: changes,
                IsDirty: changes.Count > 0,
                Error: null,
                AheadBy: tracking.AheadBy,
                BehindBy: tracking.BehindBy,
                DiffStat: diffStat,
                DiffCheckWarnings: diffCheckWarnings);
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

    /// <summary>
    /// Computes a <c>git diff --stat</c>-style summary and a <c>git diff
    /// --check</c>-style warning list (trailing whitespace / conflict markers
    /// on added lines), comparing HEAD against the index and working
    /// directory combined — i.e. all uncommitted changes, staged or not.
    /// </summary>
    private static (string? DiffStat, IReadOnlyList<DiffCheckWarning> Warnings) ComputeDiff(Repository repo)
    {
        if (repo.Head.Tip is null)
            return (null, []);

        using var patch = repo.Diff.Compare<Patch>(
            repo.Head.Tip.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory);

        var fileCount = 0;
        var warnings = new List<DiffCheckWarning>();

        foreach (var entry in patch)
        {
            fileCount++;
            warnings.AddRange(FindDiffCheckWarnings(entry.Path, entry.Patch));
        }

        var diffStat = $"{fileCount} file(s) changed, +{patch.LinesAdded}/-{patch.LinesDeleted}";
        return (diffStat, warnings);
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeaderPattern();

    [GeneratedRegex(@"^(<{7}|={7}|>{7})(?!=)")]
    private static partial Regex ConflictMarkerPattern();

    private static IEnumerable<DiffCheckWarning> FindDiffCheckWarnings(string path, string patchText)
    {
        var warnings = new List<DiffCheckWarning>();
        var newLineNumber = 0;

        foreach (var line in patchText.Split('\n'))
        {
            var headerMatch = HunkHeaderPattern().Match(line);
            if (headerMatch.Success)
            {
                newLineNumber = int.Parse(headerMatch.Groups[1].Value) - 1;
                continue;
            }

            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
                continue;

            if (line.StartsWith('+'))
            {
                newLineNumber++;
                var content = line[1..].TrimEnd('\r');
                if (content.Length > 0 && (content[^1] == ' ' || content[^1] == '\t'))
                    warnings.Add(new DiffCheckWarning(path, newLineNumber, "trailing whitespace"));
                else if (ConflictMarkerPattern().IsMatch(content))
                    warnings.Add(new DiffCheckWarning(path, newLineNumber, "unresolved conflict marker"));
            }
            else if (line.StartsWith(' '))
            {
                newLineNumber++;
            }
        }

        return warnings;
    }
}
