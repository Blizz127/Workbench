using System.Net;
using System.Text.Json;
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
/// Host-level tests: the dashboard runs in a TestServer with all external
/// services stubbed. No Docker, git, tmux, or database is touched.
/// </summary>
public sealed class WebDashboardHostTests
{
    private const string Token = "test-token";

    private sealed class DashboardFactory(bool withToken) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Web:AuthToken"] = withToken ? Token : string.Empty,
                    ["Web:DatabasePath"] = Path.Combine(
                        Path.GetTempPath(), "workbench-tests", "web-" + Guid.NewGuid().ToString("N") + ".db"),
                }));

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IProjectRepository>(new WebStubs.Projects(new Project
                {
                    Id = 7,
                    Name = "Banshee Realm 11",
                    Status = ProjectStatus.Active,
                    RepositoryPath = "/repo/banshee",
                }));
                services.AddSingleton<ITimelineRepository>(new WebStubs.Timeline());
                services.AddSingleton<ISystemHealthService>(new WebStubs.SystemHealth());
                services.AddSingleton<IGitService>(new WebStubs.Git(new RepositorySnapshot(
                    IsValid: true, BranchName: "main",
                    LastCommit: new CommitInfo("abcdef123456", "abcdef1", "Fix login packet", "blizz",
                        DateTimeOffset.Now),
                    RecentCommits: [], ModifiedFiles: [], IsDirty: false,
                    Error: null, AheadBy: 0, BehindBy: 0)));
                services.AddSingleton<IDockerComposeService>(new WebStubs.Docker(composeFile: null, status: null));
                services.AddSingleton<ITmuxService>(new WebStubs.Tmux(new TmuxServerStatus(
                    true, true, [new TmuxSession("banshee", null, false, 1, "bash")], null)));
            });
        }
    }

    [Fact]
    public async Task Dashboard_requires_authentication()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/?token=wrong-token")).StatusCode);
    }

    [Fact]
    public async Task A_valid_token_opens_the_dashboard_via_query_or_header()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        var page = await client.GetAsync($"/?token={Token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("text/html", page.Content.Headers.ContentType?.ToString());
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Banshee Realm 11", html);
        Assert.Contains("width=device-width", html);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/snapshot");
        request.Headers.Add("X-Workbench-Token", Token);
        var api = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);

        using var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        Assert.Equal("Banshee Realm 11",
            json.RootElement.GetProperty("projects")[0].GetProperty("name").GetString());
        Assert.Equal("banshee",
            json.RootElement.GetProperty("tmux").GetProperty("sessions")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_unconfigured_token_refuses_everything_with_503()
    {
        using var factory = new DashboardFactory(withToken: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/?token=anything");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not configured", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Mutation_verbs_have_nowhere_to_go(string method)
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/", "/api/snapshot" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"{path}?token={Token}");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }
}
