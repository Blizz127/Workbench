using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// ProjectFileService against real temp directories — confinement, binary and
/// size caps, listing order, and search. No git, no processes.
/// </summary>
public sealed class ProjectFileServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "files-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectFileService _files = new();

    public ProjectFileServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Beta"));
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "alpha.txt"), "hello needle world\nsecond line\n");
        File.WriteAllText(Path.Combine(_root, "charlie.txt"), "nothing here\n");
        File.WriteAllText(Path.Combine(_root, "Beta", "inner.cs"),
            "// comment\nint needleOne;\nneedle two\nneedle three\nneedle four\n");
        File.WriteAllText(Path.Combine(_root, ".git", "config"), "secret\n");
        File.WriteAllBytes(Path.Combine(_root, "blob.bin"), [0x89, 0x00, 0x50, 0x4E]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { Directory.Delete(_root, recursive: true); }
    }

    // ---------- confinement ----------

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\secret.txt")]
    [InlineData("Beta\\..\\..\\secret.txt")]
    [InlineData("/etc/passwd")]
    public void Traversal_and_rooted_paths_are_forbidden(string path)
    {
        Assert.Equal("forbidden", _files.ReadTextFile(_root, path).Error);
        Assert.Equal("forbidden", _files.ListDirectory(_root, path).Error);
    }

    [Fact]
    public void Symlink_pointing_outside_the_root_is_rejected()
    {
        var outside = Path.Combine(
            Path.GetTempPath(), "workbench-tests", "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "top secret\n");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "link"), outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // platform refuses symlink creation — nothing to test
            }

            var result = _files.ReadTextFile(_root, "link/secret.txt");

            Assert.Equal("forbidden", result.Error);
            Assert.Null(result.Text);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    // ---------- reads ----------

    [Fact]
    public void Binary_files_are_blocked()
    {
        Assert.Equal("binary", _files.ReadTextFile(_root, "blob.bin").Error);
    }

    [Fact]
    public void Oversized_files_are_blocked()
    {
        File.WriteAllText(Path.Combine(_root, "huge.txt"), new string('a', 600 * 1024));

        Assert.Equal("too-large", _files.ReadTextFile(_root, "huge.txt").Error);
    }

    // ---------- listings ----------

    [Fact]
    public void Root_listing_is_directories_first_then_case_insensitive_names_and_skips_dot_git()
    {
        var listing = _files.ListDirectory(_root, null);

        Assert.Null(listing.Error);
        Assert.Equal(string.Empty, listing.RelativePath);
        Assert.DoesNotContain(listing.Entries, e => e.Name == ".git");
        Assert.True(listing.Entries[0].IsDirectory);
        Assert.Equal("Beta", listing.Entries[0].Name);
        Assert.Equal(
            ["alpha.txt", "blob.bin", "charlie.txt"],
            listing.Entries.Skip(1).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Missing_paths_and_wrong_kinds_are_reported()
    {
        Assert.Equal("not-found", _files.ReadTextFile(_root, "nope.txt").Error);
        Assert.Equal("not-a-file", _files.ReadTextFile(_root, "Beta").Error);
        Assert.Equal("not-a-directory", _files.ListDirectory(_root, "alpha.txt").Error);
    }

    // ---------- search ----------

    [Fact]
    public void Name_search_matches_case_insensitively()
    {
        var hits = _files.SearchNames(_root, "ALPHA");

        Assert.False(hits.Truncated);
        Assert.Contains(hits.Hits, h => h.RelativePath == "alpha.txt" && !h.IsDirectory);
    }

    [Fact]
    public void Text_search_reports_line_numbers_and_caps_three_hits_per_file()
    {
        var hits = _files.SearchText(_root, "needle");

        var alpha = Assert.Single(hits.Hits, h => h.RelativePath == "alpha.txt");
        Assert.Equal(1, alpha.LineNumber);
        Assert.Equal(3, hits.Hits.Count(h => h.RelativePath == "Beta/inner.cs"));
    }

    [Fact]
    public void Empty_queries_return_empty_results()
    {
        Assert.Empty(_files.SearchNames(_root, "  ").Hits);
        Assert.Empty(_files.SearchText(_root, string.Empty).Hits);
    }
}
