using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Host-level tests for the Phase 2C code browser: the five new routes run in
/// a TestServer over a seeded temp repository with git stubbed. No live git,
/// Docker, tmux, or database.
/// </summary>
public sealed class CodeBrowsingHostTests
{
    private const string Token = "test-token";

    private const string WorkingTreeDiff =
        "diff --git a/login.cs b/login.cs\nindex 1111111..2222222 100644\n--- a/login.cs\n+++ b/login.cs\n" +
        "@@ -1,1 +1,1 @@\n-old line\n+new line\n";

    private sealed class CodeFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Web:AuthToken"] = Token,
                    ["Web:DatabasePath"] = Path.Combine(
                        Path.GetTempPath(), "workbench-tests", "web-" + Guid.NewGuid().ToString("N") + ".db"),
                }));

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IProjectRepository>(
                    new WebStubs.Projects(WebFixtures.CodeProject()));
                services.AddSingleton<ITimelineRepository>(new WebStubs.Timeline());
                services.AddSingleton<IBlockerRepository>(new WebStubs.Blockers());
                services.AddSingleton<ISystemHealthService>(new WebStubs.SystemHealth());
                services.AddSingleton<IGitService>(new WebStubs.Git(
                    _ => WebFixtures.BansheeGit() with { RecentCommits = [WebFixtures.CodeCommit()] },
                    workingTreeDiffFor: _ => WorkingTreeDiff,
                    commitFilesFor: (_, _) =>
                    [
                        new CommitFileChange("src/Program.cs", "modified"),
                        new CommitFileChange("old.cs", "deleted"),
                    ],
                    fileContentAtFor: (_, _, _) => "historical content\n"));
                services.AddSingleton<IDockerComposeService>(new WebStubs.Docker((string?)null, null));
                services.AddSingleton<ITmuxService>(new WebStubs.Tmux(
                    new TmuxServerStatus(false, false, [], "tmux not installed")));
            });
        }
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Workbench-Token", Token);
        return await client.SendAsync(request);
    }

    // ---------- authentication & read-only enforcement ----------

    [Fact]
    public async Task Every_code_route_requires_authentication()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var path in new[]
        {
            "/projects/11/files",
            "/projects/11/file?path=notes.txt",
            "/projects/11/search?q=needle",
            "/projects/11/changes",
            "/projects/11/commits/ea407af",
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Mutation_verbs_have_nowhere_to_go_on_code_routes(string method)
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/projects/11/files",
            "/projects/11/file?path=notes.txt",
            "/projects/11/search?q=needle",
            "/projects/11/changes",
            "/projects/11/commits/ea407af",
        })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add("X-Workbench-Token", Token);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(request)).StatusCode);
        }
    }

    // ---------- pages ----------

    [Fact]
    public async Task Directory_page_lists_directories_first_then_files()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/files");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Crimson Sands", html);
        Assert.Contains(">src/</a>", html);
        Assert.Contains(">notes.txt</a>", html);
        Assert.Contains("href=\"/projects/11/files?path=src\"", html);
        Assert.Contains("href=\"/projects/11/file?path=notes.txt\"", html);
        Assert.True(html.IndexOf(">src/</a>", StringComparison.Ordinal)
            < html.IndexOf(">notes.txt</a>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_page_renders_highlighted_numbered_lines()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/file?path=src/Program.cs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<span class=\"line\" id=\"L1\">", html);
        Assert.Contains("id=\"L6\"", html); // six lines of Program.cs
        Assert.Contains("tok-k", html);    // "class"/"static" keywords
    }

    [Fact]
    public async Task Historical_file_view_is_marked_and_links_back_to_the_current_file()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/file?path=notes.txt&sha=ea407af");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Historical version", html);
        Assert.Contains("historical content", html);
        Assert.Contains("href=\"/projects/11/file?path=notes.txt\"", html);
    }

    [Fact]
    public async Task Traversal_and_absolute_paths_never_serve_content()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/projects/11/file?path=../../etc/passwd",
            "/projects/11/file?path=/etc/passwd",
            "/projects/11/files?path=..%2F..%2F..%2Fetc",
        })
        {
            var response = await Get(client, path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("outside the repository", html);
            Assert.DoesNotContain("root:", html);
        }
    }

    [Fact]
    public async Task Binary_files_show_a_blocked_message()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/file?path=blob.bin");

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Binary file", html);
    }

    [Fact]
    public async Task Search_page_renders_name_and_text_hits()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var names = await (await Get(client, "/projects/11/search?q=program&kind=name"))
            .Content.ReadAsStringAsync();
        Assert.Contains("src/Program.cs", names);

        var text = await (await Get(client, "/projects/11/search?q=needle&kind=text"))
            .Content.ReadAsStringAsync();
        Assert.Contains("notes.txt", text);
        Assert.Contains("beta needle gamma", text);
        Assert.Contains("&amp;line=2#L2", text);
    }

    [Fact]
    public async Task Changes_page_renders_the_working_tree_diff()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("diff-add", html);
        Assert.Contains("diff-del", html);
        Assert.Contains("diff-hunk", html);
        Assert.Contains("+new line", html);
    }

    [Fact]
    public async Task Commit_page_renders_header_and_changed_files()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/commits/ea407af");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Fix login packet ordering", html);
        Assert.Contains("blizz", html);
        Assert.Contains("src/Program.cs", html);
        Assert.Contains(">modified</span>", html);
        Assert.Contains(">deleted</span>", html);
        // Live files deep-link into the historical file view; deleted ones don't.
        Assert.Contains("sha=ea407af", html);
        Assert.DoesNotContain("file?path=old.cs", html);
    }

    [Fact]
    public async Task Unknown_project_ids_are_404_on_every_code_route()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/projects/999/files",
            "/projects/999/file?path=notes.txt",
            "/projects/999/search?q=needle",
            "/projects/999/changes",
            "/projects/999/commits/ea407af",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Get(client, path)).StatusCode);
        }
    }

    [Fact]
    public async Task Hostile_file_content_is_html_escaped()
    {
        using var factory = new CodeFactory();
        using var client = factory.CreateClient();

        var response = await Get(client, "/projects/11/file?path=evil.txt");

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }
}
