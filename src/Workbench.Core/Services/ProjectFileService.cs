using System.Text;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Read-only, project-scoped file surface for the mobile code browser:
/// directory listings, text-file reads, and in-process filename/text search.
/// Every path is resolved against the project's repository root and refused
/// if it escapes it (including through symlinks); binaries and oversized
/// files are blocked; nothing here ever starts a process.
/// </summary>
public interface IProjectFileService
{
    DirectoryListing ListDirectory(string repositoryRoot, string? relativePath);

    FileTextResult ReadTextFile(string repositoryRoot, string? relativePath);

    SearchResults<FileSearchHit> SearchNames(string repositoryRoot, string query);

    SearchResults<TextSearchHit> SearchText(string repositoryRoot, string query);
}

/// <summary>One row of a directory listing.</summary>
public sealed record DirectoryEntry(string Name, string RelativePath, bool IsDirectory, long? SizeBytes);

/// <summary>Directory contents, or a short machine-readable <see cref="Error"/> code.</summary>
public sealed record DirectoryListing(
    string RelativePath,
    IReadOnlyList<DirectoryEntry> Entries,
    string? Error);

/// <summary>A text file's full contents, or an error code ("not-found", "forbidden", "too-large", "binary", "not-a-file").</summary>
public sealed record FileTextResult(
    string RelativePath,
    string? Text,
    long SizeBytes,
    string? Error);

public sealed record FileSearchHit(string RelativePath, bool IsDirectory);

public sealed record TextSearchHit(string RelativePath, int LineNumber, string LineText);

/// <summary>Search hits; <see cref="Truncated"/> when a cap stopped the walk early.</summary>
public sealed record SearchResults<T>(IReadOnlyList<T> Hits, bool Truncated);

public sealed class ProjectFileService : IProjectFileService
{
    /// <summary>Largest file the browser will render (512 KB).</summary>
    public const int MaxViewBytes = 512 * 1024;

    /// <summary>Largest file text search will open (1 MB).</summary>
    public const int MaxSearchFileBytes = 1024 * 1024;

    private const int MaxHits = 200;
    private const int MaxHitsPerFile = 3;
    private const int MaxWalkEntries = 100_000;

    /// <summary>Directories that are noise or risk for browsing and searching.</summary>
    private static readonly HashSet<string> SkippedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "node_modules" };

    public DirectoryListing ListDirectory(string repositoryRoot, string? relativePath)
    {
        var relative = Normalize(relativePath);
        var full = ResolveWithinRoot(repositoryRoot, relative);
        if (full is null)
            return new DirectoryListing(relative, [], "forbidden");
        if (!Directory.Exists(full))
            return new DirectoryListing(relative, [], "not-a-directory");

        try
        {
            var entries = Directory
                .EnumerateFileSystemEntries(full)
                .Where(e => Path.GetFileName(e) != ".git")
                .Select(e =>
                {
                    var isDirectory = Directory.Exists(e);
                    return new DirectoryEntry(
                        Path.GetFileName(e),
                        Combine(relative, Path.GetFileName(e)),
                        isDirectory,
                        isDirectory ? null : new FileInfo(e).Length);
                })
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new DirectoryListing(relative, entries, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DirectoryListing(relative, [], "forbidden");
        }
    }

    public FileTextResult ReadTextFile(string repositoryRoot, string? relativePath)
    {
        var relative = Normalize(relativePath);
        var full = ResolveWithinRoot(repositoryRoot, relative);
        if (full is null)
            return new FileTextResult(relative, null, 0, "forbidden");
        if (Directory.Exists(full))
            return new FileTextResult(relative, null, 0, "not-a-file");
        if (!File.Exists(full))
            return new FileTextResult(relative, null, 0, "not-found");

        var size = new FileInfo(full).Length;
        if (size > MaxViewBytes)
            return new FileTextResult(relative, null, size, "too-large");

        try
        {
            var bytes = File.ReadAllBytes(full);
            if (LooksBinary(bytes))
                return new FileTextResult(relative, null, size, "binary");

            return new FileTextResult(relative, Decode(bytes), size, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileTextResult(relative, null, size, "forbidden");
        }
    }

    public SearchResults<FileSearchHit> SearchNames(string repositoryRoot, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResults<FileSearchHit>([], false);

        var hits = new List<FileSearchHit>();
        var truncated = Walk(repositoryRoot, (relative, isDirectory) =>
        {
            if (Path.GetFileName(relative).Contains(query, StringComparison.OrdinalIgnoreCase))
                hits.Add(new FileSearchHit(relative, isDirectory));

            return hits.Count >= MaxHits;
        });

        return new SearchResults<FileSearchHit>(hits, truncated || hits.Count >= MaxHits);
    }

    public SearchResults<TextSearchHit> SearchText(string repositoryRoot, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResults<TextSearchHit>([], false);

        var rootFull = Path.GetFullPath(repositoryRoot);
        var hits = new List<TextSearchHit>();
        var truncated = Walk(repositoryRoot, (relative, isDirectory) =>
        {
            if (isDirectory)
                return false;

            var full = Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (new FileInfo(full).Length > MaxSearchFileBytes)
                    return false;

                var bytes = File.ReadAllBytes(full);
                if (LooksBinary(bytes))
                    return false;

                var perFile = 0;
                var lineNumber = 0;
                foreach (var line in Decode(bytes).Split('\n'))
                {
                    lineNumber++;
                    if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new TextSearchHit(relative, lineNumber, TrimLine(line)));
                        if (++perFile >= MaxHitsPerFile)
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable files simply don't match.
            }

            return hits.Count >= MaxHits;
        });

        return new SearchResults<TextSearchHit>(hits, truncated || hits.Count >= MaxHits);
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against the root and refuses
    /// anything that escapes it — via <c>..</c>, absolute paths, or a symlink
    /// in any segment pointing outside the root.
    /// </summary>
    internal static string? ResolveWithinRoot(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            return null;

        var rootFull = Path.GetFullPath(repositoryRoot);
        if (Path.IsPathRooted(relativePath))
            return null;

        var current = rootFull;
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." )
                continue;
            if (segment is "..")
                return null; // explicit; the prefix check below would catch it too

            var candidate = Path.GetFullPath(Path.Combine(current, segment));
            if (!candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return null;

            // A symlinked segment must resolve to somewhere still inside the root.
            var linkTarget = ResolveLink(candidate);
            if (linkTarget is not null)
            {
                var targetFull = Path.GetFullPath(linkTarget);
                if (!targetFull.Equals(rootFull, StringComparison.Ordinal) &&
                    !targetFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return null;

                candidate = targetFull;
            }

            current = candidate;
        }

        return current;
    }

    private static string? ResolveLink(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            if (Directory.Exists(path))
                return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unresolvable links are treated as ordinary (prefix-checked) paths.
        }

        return null;
    }

    /// <summary>Walks the repo (skipping noise directories) until the visitor asks to stop or a cap hits. True = stopped early.</summary>
    private static bool Walk(string repositoryRoot, Func<string, bool, bool> stopAfter)
    {
        var rootFull = Path.GetFullPath(repositoryRoot);
        var pending = new Stack<string>();
        pending.Push(rootFull);
        var visited = 0;

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (++visited > MaxWalkEntries)
                    return true;

                var isDirectory = Directory.Exists(entry);
                if (isDirectory && SkippedDirectories.Contains(Path.GetFileName(entry)))
                    continue;

                var relative = Path.GetRelativePath(rootFull, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (stopAfter(relative, isDirectory))
                    return true;

                if (isDirectory)
                    pending.Push(entry);
            }
        }

        return false;
    }

    /// <summary>Normalizes separators and trailing slashes; a leading slash is kept so the rooted-path check in <see cref="ResolveWithinRoot"/> still fires.</summary>
    private static string Normalize(string? relativePath) =>
        (relativePath ?? string.Empty).Replace('\\', '/').TrimEnd('/');

    private static string Combine(string directory, string name) =>
        directory.Length == 0 ? name : $"{directory}/{name}";

    private static bool LooksBinary(byte[] bytes)
    {
        var probe = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < probe; i++)
            if (bytes[i] == 0)
                return true;

        return false;
    }

    private static string Decode(byte[] bytes) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(bytes);

    private static string TrimLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }
}
