namespace Workbench.Core.Models;

/// <summary>Summary of one commit, decoupled from any git library.</summary>
public sealed record CommitInfo(
    string Sha,
    string ShortSha,
    string MessageSummary,
    string Author,
    DateTimeOffset When);

/// <summary>A working-tree change relative to HEAD.</summary>
public sealed record FileChange(string Path, string State);

/// <summary>
/// Snapshot of a repository's state, produced by <see cref="Services.IGitService"/>.
/// <see cref="IsValid"/> is false when the path is missing or not a repository,
/// in which case <see cref="Error"/> explains why.
/// </summary>
public sealed record RepositorySnapshot(
    bool IsValid,
    string? BranchName,
    CommitInfo? LastCommit,
    IReadOnlyList<CommitInfo> RecentCommits,
    IReadOnlyList<FileChange> ModifiedFiles,
    bool IsDirty,
    string? Error,
    /// <summary>Commits ahead of the upstream tracking branch, or null if there is none.</summary>
    int? AheadBy = null,
    /// <summary>Commits behind the upstream tracking branch, or null if there is none.</summary>
    int? BehindBy = null)
{
    public static RepositorySnapshot Invalid(string error) =>
        new(false, null, null, [], [], false, error);
}

/// <summary>A TODO/FIXME/HACK comment found in the repository's source files.</summary>
public sealed record TodoItem(string FilePath, int LineNumber, string Text);
