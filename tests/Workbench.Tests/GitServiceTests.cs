using LibGit2Sharp;
using Workbench.Git;
using Xunit;
using Signature = LibGit2Sharp.Signature;

namespace Workbench.Tests;

/// <summary>
/// Exercises GitService against real temporary git repositories (created via
/// LibGit2Sharp directly, no dependency on a `git` CLI being installed).
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly string _repoPath =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    private readonly GitService _service = new();
    private static readonly Signature Author = new("Test Author", "test@example.com", DateTimeOffset.Now);

    public GitServiceTests() => Repository.Init(_repoPath);

    public void Dispose()
    {
        // LibGit2Sharp keeps native handles open briefly; retry the delete once.
        try { Directory.Delete(_repoPath, recursive: true); }
        catch (IOException) { Directory.Delete(_repoPath, recursive: true); }
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_repoPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private Commit CommitAll(string message)
    {
        using var repo = new Repository(_repoPath);
        Commands.Stage(repo, "*");
        return repo.Commit(message, Author, Author);
    }

    [Fact]
    public void Missing_path_reports_invalid_with_a_reason()
    {
        var snapshot = _service.GetSnapshot(Path.Combine(_repoPath, "does-not-exist"));
        Assert.False(snapshot.IsValid);
        Assert.NotNull(snapshot.Error);
    }

    [Fact]
    public void Empty_repository_with_no_commits_reports_valid_but_no_head_commit()
    {
        var snapshot = _service.GetSnapshot(_repoPath);
        Assert.True(snapshot.IsValid);
        Assert.Null(snapshot.LastCommit);
        Assert.False(snapshot.IsDirty);
    }

    [Fact]
    public void Clean_repository_after_commit_reports_not_dirty_with_correct_head()
    {
        WriteFile("a.txt", "hello\n");
        var commit = CommitAll("initial commit");

        var snapshot = _service.GetSnapshot(_repoPath);

        Assert.True(snapshot.IsValid);
        Assert.False(snapshot.IsDirty);
        Assert.Equal(commit.Sha, snapshot.LastCommit!.Sha);
        Assert.Empty(snapshot.ModifiedFiles);
    }

    [Fact]
    public void Untracked_and_modified_files_are_reported_as_dirty()
    {
        WriteFile("a.txt", "hello\n");
        CommitAll("initial commit");

        WriteFile("a.txt", "hello changed\n");
        WriteFile("b.txt", "new file\n");

        var snapshot = _service.GetSnapshot(_repoPath);

        Assert.True(snapshot.IsDirty);
        Assert.Contains(snapshot.ModifiedFiles, f => f.Path == "a.txt" && f.State == "Modified");
        Assert.Contains(snapshot.ModifiedFiles, f => f.Path == "b.txt" && f.State == "Added");
    }

    [Fact]
    public void Diff_stat_reflects_added_and_removed_lines_against_head()
    {
        WriteFile("a.txt", "line1\nline2\nline3\n");
        CommitAll("initial commit");

        WriteFile("a.txt", "line1\nline2 changed\nline3\nline4\n");

        var snapshot = _service.GetSnapshot(_repoPath);

        Assert.NotNull(snapshot.DiffStat);
        Assert.Contains("1 file(s) changed", snapshot.DiffStat);
    }

    [Fact]
    public void Diff_check_flags_trailing_whitespace_on_added_lines()
    {
        WriteFile("a.txt", "line1\n");
        CommitAll("initial commit");

        WriteFile("a.txt", "line1\nline2 with trailing space   \n");

        var snapshot = _service.GetSnapshot(_repoPath);

        Assert.NotNull(snapshot.DiffCheckWarnings);
        Assert.Contains(snapshot.DiffCheckWarnings!, w => w.Issue.Contains("trailing whitespace"));
    }

    [Fact]
    public void Diff_check_flags_unresolved_conflict_markers()
    {
        WriteFile("a.txt", "line1\n");
        CommitAll("initial commit");

        WriteFile("a.txt", "line1\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch\n");

        var snapshot = _service.GetSnapshot(_repoPath);

        Assert.Contains(snapshot.DiffCheckWarnings!, w => w.Issue.Contains("conflict marker"));
    }

    [Fact]
    public void No_upstream_branch_reports_null_ahead_behind()
    {
        WriteFile("a.txt", "hello\n");
        CommitAll("initial commit");

        var snapshot = _service.GetSnapshot(_repoPath);

        Assert.Null(snapshot.AheadBy);
        Assert.Null(snapshot.BehindBy);
    }

    [Fact]
    public void GetHeadSha_returns_the_current_commit_sha()
    {
        WriteFile("a.txt", "hello\n");
        var commit = CommitAll("initial commit");

        Assert.Equal(commit.Sha, _service.GetHeadSha(_repoPath));
    }

    [Fact]
    public void GetHeadSha_returns_null_for_a_repository_with_no_commits()
    {
        Assert.Null(_service.GetHeadSha(_repoPath));
    }
}
