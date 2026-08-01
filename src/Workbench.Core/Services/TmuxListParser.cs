using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Parses <see cref="TmuxCommandBuilder.ListSessionsCommand"/> output
/// (pipe-delimited: name|created-epoch|attached-count|window-count|active-window-name)
/// into typed <see cref="TmuxSession"/> rows. Malformed lines are skipped.
/// </summary>
public static class TmuxListParser
{
    public static IReadOnlyList<TmuxSession> Parse(string output)
    {
        var sessions = new List<TmuxSession>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var parts = trimmed.Split('|');
            if (parts.Length < 4 || parts[0].Length == 0)
                continue;

            if (!long.TryParse(parts[1], out var createdEpoch))
                continue;
            if (!int.TryParse(parts[2], out var attachedCount))
                continue;
            if (!int.TryParse(parts[3], out var windowCount))
                continue;

            // The window name may itself be empty; rejoin in case it ever
            // contains the delimiter.
            var activeWindow = parts.Length > 4
                ? string.Join('|', parts.Skip(4)) is { Length: > 0 } name ? name : null
                : null;

            sessions.Add(new TmuxSession(
                Name: parts[0],
                Created: createdEpoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(createdEpoch) : null,
                IsAttached: attachedCount > 0,
                WindowCount: windowCount,
                ActiveWindowName: activeWindow));
        }

        return sessions;
    }
}
