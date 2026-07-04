using System.Globalization;

namespace Workbench.Core.Services;

/// <summary>
/// Appends handoff checkpoints to a fixed file inside a project's own
/// repository: <c>docs/ai_context/ACTIVE_HANDOFF.md</c>. This is the one
/// place Workbench writes into a tracked project's working tree — the same
/// category of action as exporting <c>workbench.json</c> (a Workbench-owned
/// context artifact, not source code), and it is always append-only, always
/// explicit (the caller previews the text and confirms), and never touches
/// git — no add, no commit.
/// </summary>
public interface IHandoffCheckpointWriter
{
    /// <summary>Relative path of the checkpoint file within a repository.</summary>
    string RelativePath { get; }

    /// <summary>Wraps <paramref name="body"/> with a timestamped heading, ready to preview before appending.</summary>
    string FormatCheckpoint(string body, DateTimeOffset timestamp);

    /// <summary>
    /// Appends <paramref name="checkpointText"/> (typically the output of
    /// <see cref="FormatCheckpoint"/>) to the checkpoint file, creating the
    /// file and its parent directories if they don't exist. Never truncates
    /// or removes existing content.
    /// </summary>
    void Append(string repositoryPath, string checkpointText);
}

public sealed class HandoffCheckpointWriter : IHandoffCheckpointWriter
{
    public string RelativePath => Path.Combine("docs", "ai_context", "ACTIVE_HANDOFF.md");

    public string FormatCheckpoint(string body, DateTimeOffset timestamp)
    {
        var heading = $"## Checkpoint — {timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
        return $"{heading}\n\n{body.TrimEnd()}\n";
    }

    public void Append(string repositoryPath, string checkpointText)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new InvalidOperationException("The project has no repository path to write a checkpoint into.");
        if (!Directory.Exists(repositoryPath))
            throw new InvalidOperationException($"Repository path does not exist: {repositoryPath}");

        var fullPath = Path.Combine(repositoryPath, RelativePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var alreadyExists = File.Exists(fullPath);
        using var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);

        if (!alreadyExists)
            writer.Write("# Active Handoff\n\nAppend-only checkpoint log written by Workbench. Newest entries are at the bottom.\n\n");
        else
            writer.Write("\n---\n\n");

        writer.Write(checkpointText);
    }
}
