using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Read-only view over a project's git repository. Implementations must never
/// throw for missing/invalid paths — they return <see cref="RepositorySnapshot.Invalid"/>.
/// </summary>
public interface IGitService
{
    RepositorySnapshot GetSnapshot(string? repositoryPath, int recentCommitCount = 10);

    /// <summary>SHA of HEAD, or null when unavailable.</summary>
    string? GetHeadSha(string? repositoryPath);

    /// <summary>
    /// Unified patch of HEAD vs index + working directory (tracked changes).
    /// Null when the repository is missing/invalid or has an unborn HEAD;
    /// empty string when the tree is clean.
    /// </summary>
    string? GetWorkingTreeDiff(string? repositoryPath);

    /// <summary>
    /// Files changed by one commit (against its first parent). Short SHAs are
    /// accepted. Empty when the repository or commit is unavailable.
    /// </summary>
    IReadOnlyList<CommitFileChange> GetCommitFiles(string? repositoryPath, string sha);

    /// <summary>
    /// Text content of one file at one commit. Null when the repository,
    /// commit, or file is unavailable, or the blob is binary or oversized.
    /// </summary>
    string? GetFileContentAtCommit(string? repositoryPath, string sha, string filePath);
}

/// <summary>Scans a repository's text files for TODO/FIXME/HACK comments.</summary>
public interface ITodoScanner
{
    IReadOnlyList<TodoItem> Scan(string? repositoryPath, int maxItems = 100);
}
