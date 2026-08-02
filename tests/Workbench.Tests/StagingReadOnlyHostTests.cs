using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Host-level tests for the staging read-only mode: with
/// <c>Web:MobileActionsEnabled = false</c> the host is fully read-only —
/// both POST action endpoints answer 403 with the staging page before the
/// action service is ever touched, while every GET page keeps working.
/// </summary>
public sealed class StagingReadOnlyHostTests
{
    private const string Token = "test-token";

    private sealed class StagingFactory : WebApplicationFactory<Program>
    {
        public StagingFactory()
        {
            Docker = new WebStubs.Docker(
                p => p.ComposeFile is null ? null : "/repo/banshee/docker-compose.docker.yml",
                _ => WebFixtures.BansheeCompose(),
                (_, operation, _) => Task.FromResult(WebFixtures.ComposeOk(operation)));
        }

        /// <summary>The registered Docker stub — must stay untouched in staging mode.</summary>
        public WebStubs.Docker Docker { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Web:AuthToken"] = Token,
                    ["Web:MobileActionsEnabled"] = "false",
                    ["Web:DatabasePath"] = Path.Combine(
                        Path.GetTempPath(), "workbench-tests", "web-" + Guid.NewGuid().ToString("N") + ".db"),
                }));

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IProjectRepository>(
                    new WebStubs.Projects(WebFixtures.Banshee(), WebFixtures.Xenogears()));
                services.AddSingleton<ITimelineRepository>(new WebStubs.Timeline());
                services.AddSingleton<IBlockerRepository>(new WebStubs.Blockers());
                services.AddSingleton<ISystemHealthService>(new WebStubs.SystemHealth());
                services.AddSingleton<IMilestoneRepository>(new WebStubs.Milestones());
                services.AddSingleton<IFrontierRepository>(new WebStubs.Frontiers());
                services.AddSingleton<IRuntimeHealthRepository>(new WebStubs.RuntimeHealthRecords());
                services.AddSingleton<IBuildHistoryRepository>(new WebStubs.BuildHistory());
                services.AddSingleton<ITestHistoryRepository>(new WebStubs.TestHistory());
                services.AddSingleton<IGitService>(new WebStubs.Git(WebFixtures.BansheeGit()));
                services.AddSingleton<IDockerComposeService>(Docker);
                services.AddSingleton<ITmuxService>(new WebStubs.Tmux(WebFixtures.TmuxRunning(),
                    execute: (operation, _) => Task.FromResult(
                        new TmuxOperationResult(operation, true, false, 0, ""))));
                services.AddSingleton<IProjectConfigService>(new WebStubs.ProjectConfigs(path =>
                    path == WebFixtures.BansheeRepoDir ? WebFixtures.BansheeConfig() : WebFixtures.XenogearsConfig()));
                services.AddSingleton<IPredefinedCommandRunner>(WebStubs.PredefinedCommands.NeverCalled());
            });
        }
    }

    private static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Workbench-Token", Token);
        return request;
    }

    private static HttpRequestMessage Post(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("csrf", "forged"),
                new KeyValuePair<string, string>("nonce", "forged"),
            ]),
        };
        request.Headers.Add("X-Workbench-Token", Token);
        return request;
    }

    [Fact]
    public async Task Post_execute_is_forbidden_with_the_staging_page_and_runs_nothing()
    {
        using var factory = new StagingFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("staging read-only", html);
        Assert.Contains("Actions disabled on this instance", html);
        Assert.Contains("Nothing was executed or changed", html);
        // The staging gate fires before CSRF/nonce validation — the action
        // service (and through it Docker) is never invoked.
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Post_confirm2_is_forbidden_with_the_staging_page_and_runs_nothing()
    {
        using var factory = new StagingFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/projects/7/actions/restart-map/confirm2"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("staging read-only", html);
        Assert.Contains("Actions disabled on this instance", html);
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Get_confirm_renders_the_staging_explanation_without_a_form()
    {
        using var factory = new StagingFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/projects/7/actions/logs-all/confirm"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("staging read-only", html);
        Assert.Contains("Actions disabled on this instance", html);
        Assert.DoesNotContain("name=\"nonce\"", html);
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Dashboard_and_detail_pages_keep_working_in_staging_mode()
    {
        using var factory = new StagingFactory();
        using var client = factory.CreateClient();

        var dashboard = await client.SendAsync(Get("/"));
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("Banshee Realm 11", await dashboard.Content.ReadAsStringAsync());

        var detail = await client.SendAsync(Get("/projects/7"));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Banshee Realm 11", html);
        // The actions section explains the staging mode and links nowhere.
        Assert.Contains("Mobile actions are disabled on this instance (staging read-only).", html);
        Assert.Contains("badge warn\">disabled</span>", html);
        Assert.DoesNotContain("/actions/logs-all/confirm", html);
        Assert.DoesNotContain("/actions/restart-map/confirm", html);
    }
}
