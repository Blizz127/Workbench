namespace Workbench.Core.Services;

/// <summary>
/// Lightweight reader for compose files. Service names are scraped by YAML
/// indentation (top-level keys under the <c>services:</c> block) —
/// deliberately no YAML parser dependency for a two-question job.
/// </summary>
public static class ComposeFileInspector
{
    /// <summary>Conventional compose filenames, in preference order.</summary>
    private static readonly string[] ComposeFileNames =
    [
        "docker-compose.yml",
        "docker-compose.yaml",
        "docker-compose.docker.yml",
        "compose.yml",
        "compose.yaml",
    ];

    /// <summary>Whether the directory carries a compose file at its root.</summary>
    public static bool HasComposeFile(string directory) => FindComposeFile(directory) is not null;

    /// <summary>The compose file at the directory root, if any (conventional names in preference order).</summary>
    public static string? FindComposeFile(string directory)
    {
        foreach (var name in ComposeFileNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Names of the services declared under the top-level <c>services:</c>
    /// block: lines indented exactly one level deeper than <c>services:</c>
    /// with a trailing colon. Anything before or after that block at the same
    /// indent level is ignored.
    /// </summary>
    public static IReadOnlyList<string> GetServiceNames(string composeFilePath)
    {
        var services = new List<string>();
        if (!File.Exists(composeFilePath))
            return services;

        int? blockIndent = null;
        int? serviceIndent = null;

        foreach (var line in File.ReadLines(composeFilePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var indent = line.Length - line.TrimStart().Length;

            if (blockIndent is null)
            {
                if (trimmed == "services:")
                    blockIndent = indent;
                continue;
            }

            // Back at (or above) the services: indent — the block has ended.
            if (indent <= blockIndent.Value)
                break;

            // The first content line inside the block sets the service indent.
            serviceIndent ??= indent;
            if (indent == serviceIndent.Value && trimmed.EndsWith(':'))
                services.Add(trimmed.TrimEnd(':').Trim().Trim('"', '\''));
        }

        return services;
    }
}
