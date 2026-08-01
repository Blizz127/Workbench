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
}
