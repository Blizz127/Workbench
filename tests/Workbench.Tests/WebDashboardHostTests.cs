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
                services.AddSingleton<IProjectRepository>(
                    new WebStubs.Projects(WebFixtures.Banshee(), WebFixtures.Xenogears()));
                services.AddSingleton<ITimelineRepository>(
                    new WebStubs.Timeline(WebFixtures.SecretJournalEntry()));
                services.AddSingleton<ISystemHealthService>(new WebStubs.SystemHealth());
                services.AddSingleton<IGitService>(new WebStubs.Git(path =>
                    path == WebFixtures.BansheeRepoDir
                        ? WebFixtures.BansheeGit()
                        : RepositorySnapshot.Invalid("Path does not exist: /repo/xenogears-does-not-exist")));
                services.AddSingleton<IDockerComposeService>(new WebStubs.Docker(
                    p => p.ComposeFile is null ? null : "/repo/banshee/docker-compose.docker.yml",
                    _ => WebFixtures.BansheeCompose()));
                services.AddSingleton<ITmuxService>(new WebStubs.Tmux(WebFixtures.TmuxRunning()));
            });
        }
    }

    // ---------- authentication ----------

    [Fact]
    public async Task Every_route_requires_authentication()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/projects/7")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects/7")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/?token=wrong-token")).StatusCode);
    }

    [Fact]
    public async Task Query_token_is_exchanged_for_a_session_cookie_and_a_clean_url()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var exchange = await client.GetAsync($"/?token={Token}");

        Assert.Equal(HttpStatusCode.Found, exchange.StatusCode);
        Assert.Equal("/", exchange.Headers.Location?.ToString());

        var cookie = exchange.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("dl_session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        // The token itself never travels to the client inside the cookie.
        Assert.DoesNotContain(Token, cookie);

        // The session cookie alone opens the dashboard — no token in the URL.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie.Split(';')[0]);
        var page = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Banshee Realm 11", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_bogus_cookie_does_not_authenticate()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "dl_session=forged-session-id");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Header_token_keeps_working_for_api_access()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

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

    // ---------- read-only enforcement ----------

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Mutation_verbs_have_nowhere_to_go(string method)
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/", "/projects/7", "/api/snapshot", "/api/projects/7" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add("X-Workbench-Token", Token);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    // ---------- project detail routes ----------

    [Fact]
    public async Task Detail_page_renders_the_banshee_project()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/projects/7");
        request.Headers.Add("X-Workbench-Token", Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Banshee Realm 11", html);
        Assert.Contains(">completed<", html); // db-update one-shot
        Assert.Contains(">healthy<", html);   // database healthcheck
        Assert.Contains("detached", html);     // tmux session state
        Assert.DoesNotContain("sup3rsecret", html);
    }

    [Fact]
    public async Task Detail_json_endpoint_feeds_the_polling_script()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects/7");
        request.Headers.Add("X-Workbench-Token", Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("Banshee Realm 11", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("git").GetProperty("dirty").GetBoolean());
        Assert.Equal(2, root.GetProperty("git").GetProperty("dirtyCount").GetInt32());
        var services = root.GetProperty("docker").GetProperty("services");
        Assert.Equal(6, services.GetArrayLength());
        Assert.Equal("completed",
            services.EnumerateArray().Single(s => s.GetProperty("name").GetString() == "db-update")
                .GetProperty("stateText").GetString());
        Assert.Equal("banshee", root.GetProperty("session").GetProperty("sessionName").GetString());
        Assert.True(root.GetProperty("session").GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task Unknown_or_malformed_project_ids_are_safe_404s()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/projects/999",     // unknown id → styled 404 page
            "/projects/-5",      // never a real id
            "/projects/abc",     // fails the long constraint → routing 404
            "/projects/7/extra", // no route match
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Workbench-Token", Token);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var jsonRequest = new HttpRequestMessage(HttpMethod.Get, "/api/projects/999");
        jsonRequest.Headers.Add("X-Workbench-Token", Token);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(jsonRequest)).StatusCode);
    }

    [Fact]
    public async Task Path_traversal_can_only_ever_hit_the_404_or_the_dashboard()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        // Route values are longs and never touch the filesystem. Traversal
        // shapes either fail to match a route (404) or normalize onto a
        // legitimate page — the response is never filesystem content.
        foreach (var path in new[] { "/projects/..%2F..%2Fetc%2Fpasswd", "/projects/%2e%2e", "/projects/7%2F..%2F.." })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Workbench-Token", Token);
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.OK,
                $"unexpected status {response.StatusCode} for {path}");
            if (response.StatusCode is HttpStatusCode.OK)
                Assert.Contains("Development Lab", body); // normalized onto a real page
            Assert.DoesNotContain("root:", body);
        }
    }

    [Fact]
    public async Task Dashboard_page_marks_the_stale_project_unavailable()
    {
        using var factory = new DashboardFactory(withToken: true);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Workbench-Token", Token);
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("Xenogears Decompilation", html);
        Assert.Contains("repo missing", html);
    }
}
