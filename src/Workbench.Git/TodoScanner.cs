using System.Text.RegularExpressions;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Git;

/// <summary>
/// Scans source files for TODO/FIXME/HACK/XXX comments. Walks the working
/// tree directly (skipping common build/VCS directories) so it also sees
/// files git ignores in unusual setups; capped by size and item count to stay
/// fast on large repositories.
/// </summary>
public sealed partial class TodoScanner : ITodoScanner
{
    [GeneratedRegex(@"\b(TODO|FIXME|HACK|XXX)\b[:\s]*(.*)", RegexOptions.Compiled)]
    private static partial Regex TodoPattern();

    private const long MaxFileSizeBytes = 1024 * 1024; // skip files > 1 MB (likely data, not source)

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", "build", "dist",
        "target", ".vs", ".idea", "__pycache__", ".venv", "venv", "vendor",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cs", ".py", ".js", ".ts", ".tsx", ".jsx",
        ".rs", ".go", ".java", ".kt", ".rb", ".php", ".lua", ".sh", ".ps1", ".bat",
        ".asm", ".s", ".inc", ".md", ".txt", ".xml", ".json", ".yaml", ".yml", ".toml",
        ".axaml", ".xaml", ".sql", ".cmake", ".mk",
    };

    public IReadOnlyList<TodoItem> Scan(string? repositoryPath, int maxItems = 100)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            return [];

        var results = new List<TodoItem>();
        ScanDirectory(new DirectoryInfo(repositoryPath), repositoryPath, results, maxItems);
        return results;
    }

    private static void ScanDirectory(DirectoryInfo directory, string root, List<TodoItem> results, int maxItems)
    {
        if (results.Count >= maxItems)
            return;

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (results.Count >= maxItems)
                return;

            switch (entry)
            {
                case DirectoryInfo dir when !SkippedDirectories.Contains(dir.Name):
                    ScanDirectory(dir, root, results, maxItems);
                    break;

                case FileInfo file when TextExtensions.Contains(file.Extension) && file.Length <= MaxFileSizeBytes:
                    ScanFile(file, root, results, maxItems);
                    break;
            }
        }
    }

    private static void ScanFile(FileInfo file, string root, List<TodoItem> results, int maxItems)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(root, file.FullName);
        for (var i = 0; i < lines.Length && results.Count < maxItems; i++)
        {
            var match = TodoPattern().Match(lines[i]);
            if (!match.Success)
                continue;

            var text = match.Groups[2].Value.Trim();
            var marker = match.Groups[1].Value;
            results.Add(new TodoItem(
                relativePath,
                i + 1,
                string.IsNullOrEmpty(text) ? marker : $"{marker}: {text}"));
        }
    }
}
