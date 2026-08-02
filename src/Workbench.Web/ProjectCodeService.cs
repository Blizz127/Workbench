using System.Text.RegularExpressions;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Web;

/// <summary>
/// Typed snapshots behind the read-only code browser pages. Composes the same
/// Core services the rest of the dashboard uses — project lookup by database
/// id, repo-root-confined file access, LibGit2Sharp-backed history — and never
/// throws for content problems: every failure rides in the snapshot's
/// <c>Error</c> as a short machine-readable code ("unavailable", "not-found",
/// "forbidden", "too-large", "binary", "not-a-file", "not-a-directory",
/// "invalid-sha"). Null means the project id itself is unknown (→ 404).
/// Commit ids are validated against <c>^[0-9a-fA-F]{7,40}$</c> before git is
/// touched.
/// </summary>
public sealed partial class ProjectCodeService(
    IProjectRepository projects,
    IProjectFileService files,
    IGitService git)
{
    /// <summary>Recent commits scanned to resolve a commit page's header details.</summary>
    private const int CommitLookupDepth = 50;

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex ShaPattern();

    /// <summary>Directory listing for the browser page; <paramref name="path"/> is repo-relative.</summary>
    public CodeBrowserSnapshot? BuildDirectoryListing(long projectId, string? path)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        if (RepoRoot(project) is not { } root)
            return new CodeBrowserSnapshot(project.Id, project.Name, path ?? string.Empty, [], "unavailable");

        var listing = files.ListDirectory(root, path);
        return new CodeBrowserSnapshot(project.Id, project.Name, listing.RelativePath, listing.Entries, listing.Error);
    }

    /// <summary>One file's text, either from the working tree or — with <paramref name="sha"/> — from a commit.</summary>
    public FileViewSnapshot? BuildFileView(long projectId, string? path, string? sha)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        var relative = path ?? string.Empty;
        if (RepoRoot(project) is not { } root)
            return new FileViewSnapshot(project.Id, project.Name, relative, null, 0, null, "unavailable");

        if (sha is { Length: > 0 })
        {
            if (!ShaPattern().IsMatch(sha))
                return new FileViewSnapshot(project.Id, project.Name, relative, null, 0, null, "invalid-sha");

            // Null conflates missing commit, missing file, binary, and oversized
            // blobs — all render as "not available at that commit".
            var content = git.GetFileContentAtCommit(root, sha, relative);
            return content is null
                ? new FileViewSnapshot(project.Id, project.Name, relative, null, 0, sha, "not-found")
                : new FileViewSnapshot(project.Id, project.Name, relative, content, 0, sha, null);
        }

        var result = files.ReadTextFile(root, path);
        return new FileViewSnapshot(project.Id, project.Name, result.RelativePath,
            result.Text, result.SizeBytes, null, result.Error);
    }

    /// <summary>Filename or text search; <paramref name="kind"/> is "name" (default) or "text".</summary>
    public SearchPageSnapshot? BuildSearch(long projectId, string? query, string? kind)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        var q = query?.Trim() ?? string.Empty;
        var k = kind == "text" ? "text" : "name";
        if (RepoRoot(project) is not { } root)
            return new SearchPageSnapshot(project.Id, project.Name, q, k, [], [], false, "unavailable");

        if (q.Length == 0)
            return new SearchPageSnapshot(project.Id, project.Name, q, k, [], [], false, null);

        if (k == "text")
        {
            var text = files.SearchText(root, q);
            return new SearchPageSnapshot(project.Id, project.Name, q, k, [], text.Hits, text.Truncated, null);
        }

        var names = files.SearchNames(root, q);
        return new SearchPageSnapshot(project.Id, project.Name, q, k, names.Hits, [], names.Truncated, null);
    }

    /// <summary>Working-tree patch (HEAD vs index + working directory); empty string means clean.</summary>
    public ChangesSnapshot? BuildChanges(long projectId)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        if (RepoRoot(project) is not { } root)
            return new ChangesSnapshot(project.Id, project.Name, null, "unavailable");

        var patch = git.GetWorkingTreeDiff(root);
        return patch is null
            ? new ChangesSnapshot(project.Id, project.Name, null, "unavailable")
            : new ChangesSnapshot(project.Id, project.Name, patch, null);
    }

    /// <summary>One commit's header (best-effort from recent history) and its changed files.</summary>
    public CommitSnapshot? BuildCommit(long projectId, string sha)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        if (!ShaPattern().IsMatch(sha))
            return new CommitSnapshot(project.Id, project.Name, sha, null, [], "invalid-sha");

        if (RepoRoot(project) is not { } root)
            return new CommitSnapshot(project.Id, project.Name, sha, null, [], "unavailable");

        // Header details come from the recent-commits snapshot — the same list
        // the detail page links from. Older commits reached by URL still get
        // their file list, just without author/date/subject.
        var snapshot = git.GetSnapshot(root, CommitLookupDepth);
        var commit = snapshot.RecentCommits.FirstOrDefault(
            c => c.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase));
        var changedFiles = git.GetCommitFiles(root, sha);

        return new CommitSnapshot(project.Id, project.Name, sha, commit, changedFiles,
            commit is null && changedFiles.Count == 0 ? "not-found" : null);
    }

    /// <summary>The project's repository path when it really exists on disk, else null.</summary>
    private static string? RepoRoot(Project project) =>
        project.RepositoryPath is { Length: > 0 } path && Directory.Exists(path) ? path : null;
}

/// <summary>Directory-browser page data; <see cref="Error"/> null on success.</summary>
public sealed record CodeBrowserSnapshot(
    long Id,
    string Name,
    string Path,
    IReadOnlyList<DirectoryEntry> Entries,
    string? Error);

/// <summary>File-view page data. <see cref="Sha"/> set means a historical version; <see cref="SizeBytes"/> is 0 for historical views.</summary>
public sealed record FileViewSnapshot(
    long Id,
    string Name,
    string Path,
    string? Text,
    long SizeBytes,
    string? Sha,
    string? Error);

/// <summary>Search page data; only the hit list matching <see cref="Kind"/> is populated.</summary>
public sealed record SearchPageSnapshot(
    long Id,
    string Name,
    string Query,
    string Kind,
    IReadOnlyList<FileSearchHit> NameHits,
    IReadOnlyList<TextSearchHit> TextHits,
    bool Truncated,
    string? Error);

/// <summary>Working-tree diff page data; <see cref="Patch"/> empty means a clean tree, null means unavailable.</summary>
public sealed record ChangesSnapshot(
    long Id,
    string Name,
    string? Patch,
    string? Error);

/// <summary>Commit detail page data; <see cref="Commit"/> may be null for commits outside recent history.</summary>
public sealed record CommitSnapshot(
    long Id,
    string Name,
    string Sha,
    CommitInfo? Commit,
    IReadOnlyList<CommitFileChange> Files,
    string? Error);
