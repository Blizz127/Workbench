using LibGit2Sharp;
using Workbench.Git;
using Xunit;
using Signature = LibGit2Sharp.Signature;

namespace Workbench.Tests;

/// <summary>
/// GitService's working-tree diff against real temp repositories (no network,
/// no git CLI — LibGit2Sharp only).
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "git-" + Guid.NewGuid().ToString("N"));
    private readonly GitService _git = new();
    private static readonly Signature Author = new("Test Author", "test@example.com", DateTimeOffset.Now);

    public GitServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { Directory.Delete(_root, recursive: true); }
    }

    private string InitRepoWithCommit([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        Repository.Init(path);
        File.WriteAllText(Path.Combine(path, "file.txt"), "line one\n");
        using var repo = new Repository(path);
        Commands.Stage(repo, "*");
        repo.Commit("initial", Author, Author);
        return path;
    }

    [Fact]
    public void Clean_tree_yields_an_empty_diff()
    {
        var path = InitRepoWithCommit();
        Assert.Equal(string.Empty, _git.GetWorkingTreeDiff(path));
    }

    [Fact]
    public void Modified_tracked_file_appears_in_the_patch()
    {
        var path = InitRepoWithCommit();
        File.WriteAllText(Path.Combine(path, "file.txt"), "line one\nline two\n");

        var diff = _git.GetWorkingTreeDiff(path);

        Assert.NotNull(diff);
        Assert.Contains("file.txt", diff);
        Assert.Contains("+line two", diff);
    }

    [Fact]
    public void Staged_change_appears_in_the_patch()
    {
        var path = InitRepoWithCommit();
        File.WriteAllText(Path.Combine(path, "file.txt"), "changed\n");
        using (var repo = new Repository(path))
            Commands.Stage(repo, "file.txt");

        var diff = _git.GetWorkingTreeDiff(path);

        Assert.NotNull(diff);
        Assert.Contains("file.txt", diff);
        Assert.Contains("+changed", diff);
    }

    [Fact]
    public void Missing_path_and_non_repo_yield_null()
    {
        Assert.Null(_git.GetWorkingTreeDiff(Path.Combine(_root, "does-not-exist")));
        Assert.Null(_git.GetWorkingTreeDiff(null));

        var plain = Path.Combine(_root, "plain-directory");
        Directory.CreateDirectory(plain);
        Assert.Null(_git.GetWorkingTreeDiff(plain));
    }

    [Fact]
    public void Unborn_head_yields_null()
    {
        var path = Path.Combine(_root, "unborn");
        Directory.CreateDirectory(path);
        Repository.Init(path);

        Assert.Null(_git.GetWorkingTreeDiff(path));
    }

    // ---------- GetCommitFiles ----------

    /// <summary>Two commits: initial adds keep.txt + del.txt; second modifies keep, deletes del, adds new + sub/deep.</summary>
    private string InitRepoWithTwoCommits([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        Repository.Init(path);
        File.WriteAllText(Path.Combine(path, "keep.txt"), "v1\n");
        File.WriteAllText(Path.Combine(path, "del.txt"), "gone soon\n");
        using (var repo = new Repository(path))
        {
            Commands.Stage(repo, "*");
            repo.Commit("initial", Author, Author);
        }

        File.WriteAllText(Path.Combine(path, "keep.txt"), "v2\n");
        File.Delete(Path.Combine(path, "del.txt"));
        File.WriteAllText(Path.Combine(path, "new.txt"), "fresh\n");
        Directory.CreateDirectory(Path.Combine(path, "sub"));
        File.WriteAllText(Path.Combine(path, "sub", "deep.txt"), "nested\n");
        using (var repo = new Repository(path))
        {
            Commands.Stage(repo, "*");
            repo.Commit("second", Author, Author);
        }

        return path;
    }

    [Fact]
    public void Commit_files_report_added_modified_and_deleted_kinds()
    {
        var path = InitRepoWithTwoCommits();
        using var repo = new Repository(path);
        var sha = repo.Head.Tip.Sha;

        var files = _git.GetCommitFiles(path, sha);

        Assert.Contains(files, f => f.Path == "new.txt" && f.Kind == "added");
        Assert.Contains(files, f => f.Path == "sub/deep.txt" && f.Kind == "added");
        Assert.Contains(files, f => f.Path == "keep.txt" && f.Kind == "modified");
        Assert.Contains(files, f => f.Path == "del.txt" && f.Kind == "deleted");
    }

    [Fact]
    public void Commit_files_accept_a_short_sha()
    {
        var path = InitRepoWithTwoCommits();
        using var repo = new Repository(path);
        var sha = repo.Head.Tip.Sha;

        var files = _git.GetCommitFiles(path, sha[..7]);

        Assert.Equal(4, files.Count);
    }

    [Fact]
    public void Commit_files_for_bad_sha_and_non_repo_are_empty()
    {
        var path = InitRepoWithTwoCommits();

        Assert.Empty(_git.GetCommitFiles(path, "deadbeefcafe"));
        Assert.Empty(_git.GetCommitFiles(null, "deadbeefcafe"));

        var plain = Path.Combine(_root, "plain-for-commit-files");
        Directory.CreateDirectory(plain);
        Assert.Empty(_git.GetCommitFiles(plain, "deadbeefcafe"));
    }

    // ---------- GetFileContentAtCommit ----------

    [Fact]
    public void File_content_comes_from_the_requested_commit_not_the_working_tree()
    {
        var path = InitRepoWithCommit();
        string firstSha;
        using (var repo = new Repository(path))
            firstSha = repo.Head.Tip.Sha;
        File.WriteAllText(Path.Combine(path, "file.txt"), "line one\nline two\n");
        using (var repo = new Repository(path))
        {
            Commands.Stage(repo, "*");
            repo.Commit("second", Author, Author);
        }

        var content = _git.GetFileContentAtCommit(path, firstSha, "file.txt");

        Assert.Equal("line one\n", content);
    }

    [Fact]
    public void File_content_supports_nested_paths()
    {
        var path = InitRepoWithTwoCommits();
        using var repo = new Repository(path);
        var sha = repo.Head.Tip.Sha;

        Assert.Equal("nested\n", _git.GetFileContentAtCommit(path, sha, "sub/deep.txt"));
    }

    [Fact]
    public void File_content_is_null_for_missing_files_bad_shas_and_binary_blobs()
    {
        var path = InitRepoWithCommit();
        File.WriteAllBytes(Path.Combine(path, "blob.bin"), [0x89, 0x00, 0x50, 0x4E]);
        using (var repo = new Repository(path))
        {
            Commands.Stage(repo, "*");
            repo.Commit("add binary", Author, Author);
        }

        using var open = new Repository(path);
        var sha = open.Head.Tip.Sha;

        Assert.Null(_git.GetFileContentAtCommit(path, sha, "missing.txt"));
        Assert.Null(_git.GetFileContentAtCommit(path, "deadbeefcafe", "file.txt"));
        Assert.Null(_git.GetFileContentAtCommit(path, sha, "blob.bin"));
        Assert.Null(_git.GetFileContentAtCommit(Path.Combine(_root, "does-not-exist"), sha, "file.txt"));
    }
}
