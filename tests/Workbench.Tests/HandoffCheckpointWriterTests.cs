using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class HandoffCheckpointWriterTests : IDisposable
{
    private readonly string _repoPath =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    private readonly HandoffCheckpointWriter _writer = new();

    public HandoffCheckpointWriterTests() => Directory.CreateDirectory(_repoPath);

    public void Dispose() => Directory.Delete(_repoPath, recursive: true);

    private string FullPath => Path.Combine(_repoPath, _writer.RelativePath);

    [Fact]
    public void RelativePath_points_at_the_fixed_ai_context_location()
    {
        Assert.Equal(Path.Combine("docs", "ai_context", "ACTIVE_HANDOFF.md"), _writer.RelativePath);
    }

    [Fact]
    public void First_append_creates_parent_directories_and_a_file_header()
    {
        _writer.Append(_repoPath, "first entry body");

        Assert.True(File.Exists(FullPath));
        var content = File.ReadAllText(FullPath);
        Assert.StartsWith("# Active Handoff", content);
        Assert.Contains("first entry body", content);
    }

    [Fact]
    public void Second_append_never_truncates_the_first_entry()
    {
        _writer.Append(_repoPath, "entry one");
        _writer.Append(_repoPath, "entry two");

        var content = File.ReadAllText(FullPath);
        Assert.Contains("entry one", content);
        Assert.Contains("entry two", content);
        Assert.True(content.IndexOf("entry one", StringComparison.Ordinal) <
                    content.IndexOf("entry two", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_repository_path_throws_without_writing_anything()
    {
        var missingPath = Path.Combine(_repoPath, "does-not-exist");
        Assert.Throws<InvalidOperationException>(() => _writer.Append(missingPath, "text"));
    }

    [Fact]
    public void Blank_repository_path_throws()
    {
        Assert.Throws<InvalidOperationException>(() => _writer.Append("", "text"));
    }

    [Fact]
    public void FormatCheckpoint_wraps_body_with_a_timestamped_heading()
    {
        var formatted = _writer.FormatCheckpoint("some body text", new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains("## Checkpoint —", formatted);
        Assert.Contains("some body text", formatted);
    }
}
