using System.Net;
using System.Text.RegularExpressions;
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
/// Host-level tests for the Phase 2D mobile action layer: the full
/// confirm → (confirm2) → execute flow runs in a TestServer with Docker,
/// tmux, config, and predefined commands stubbed. Nothing external is touched.
/// </summary>
public sealed class MobileActionHostTests
{
    private const string Token = "test-token";
    private const string ComposePath = "/repo/banshee/docker-compose.docker.yml";

    private sealed class ActionFactory : WebApplicationFactory<Program>
    {
        public ActionFactory()
        {
            Docker = new WebStubs.Docker(
                p => p.ComposeFile is null ? null : ComposePath,
                _ => WebFixtures.BansheeCompose(),
                (_, operation, _) => Task.FromResult(ComposeOutcome(operation)));
        }

        /// <summary>What the stubbed compose execution returns; replaceable per test.</summary>
        public Func<ComposeOperation, ComposeOperationResult> ComposeOutcome { get; set; } =
            operation => WebFixtures.ComposeOk(operation, "log line one\nlog line two");

        /// <summary>The registered Docker stub — inspect Executed after a run.</summary>
        public WebStubs.Docker Docker { get; }

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
                    new WebStubs.Projects(WebFixtures.Banshee(), WebFixtures.Xenogears()));
                services.AddSingleton<ITimelineRepository>(new WebStubs.Timeline());
                services.AddSingleton<IBlockerRepository>(new WebStubs.Blockers());
                services.AddSingleton<ISystemHealthService>(new WebStubs.SystemHealth());
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

    // ---------- helpers ----------

    private static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Workbench-Token", Token);
        return request;
    }

    private static HttpRequestMessage Post(string url, string? csrf, string? nonce)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (csrf is not null)
            fields.Add(new KeyValuePair<string, string>("csrf", csrf));
        if (nonce is not null)
            fields.Add(new KeyValuePair<string, string>("nonce", nonce));
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("X-Workbench-Token", Token);
        return request;
    }

    private static string HiddenField(string html, string name) =>
        Regex.Match(html, $"<input type=\"hidden\" name=\"{name}\" value=\"([^\"]*)\"").Groups[1].Value;

    private static async Task<(string Csrf, string Nonce)> ConfirmPageTokens(HttpClient client, string url)
    {
        var html = await (await client.SendAsync(Get(url))).Content.ReadAsStringAsync();
        return (HiddenField(html, "csrf"), HiddenField(html, "nonce"));
    }

    // ---------- authentication ----------

    [Fact]
    public async Task Action_routes_require_authentication()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/projects/7/actions/logs-all/confirm")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/projects/7/actions/logs-all/execute", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/projects/7/actions/restart-map/confirm2", new FormUrlEncodedContent([]))).StatusCode);
    }

    // ---------- confirm page ----------

    [Fact]
    public async Task Confirm_page_renders_the_exact_command_preview_and_the_nonce_form()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/projects/7/actions/logs-map/confirm"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"docker compose -f &quot;{ComposePath}&quot; logs --no-color --tail=200 map", html);
        Assert.Contains("name=\"csrf\"", html);
        Assert.Contains("name=\"nonce\"", html);
        Assert.Contains("action=\"/projects/7/actions/logs-map/execute\"", html);
        Assert.Contains("read-only", html);
    }

    [Fact]
    public async Task Detail_page_lists_the_actions_and_journal_statically()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var html = await (await client.SendAsync(Get("/projects/7"))).Content.ReadAsStringAsync();

        Assert.Contains("<h2>Actions</h2>", html);
        Assert.Contains("View all logs", html);
        Assert.Contains("/projects/7/actions/logs-all/confirm", html);
        Assert.Contains("/projects/7/actions/restart-map/confirm", html);
        Assert.Contains("<h2>Recent Actions</h2>", html);
        Assert.Contains("No actions attempted yet.", html);
    }

    // ---------- CSRF / nonce gates ----------

    [Fact]
    public async Task Execute_without_csrf_is_forbidden()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf: null, nonce: "whatever"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Execute_with_a_wrong_csrf_is_forbidden()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf + "x", nonce));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Execute_without_a_nonce_is_rejected()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var (csrf, _) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Request rejected", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Execute_with_a_non_form_body_is_forbidden_not_an_error()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        // No form content-type at all — must be a clean CSRF rejection, never a 500.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/projects/7/actions/logs-all/execute");
        request.Headers.Add("X-Workbench-Token", Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Docker.Executed);
    }

    // ---------- end-to-end read-only flow ----------

    [Fact]
    public async Task Allowlisted_logs_action_executes_end_to_end_and_shows_output()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("success", html);
        Assert.Contains("log line one", html);
        var (operation, _) = Assert.Single(factory.Docker.Executed);
        Assert.Equal(ComposeOperationKind.LogsAll, operation.Kind);
    }

    [Fact]
    public async Task A_nonce_cannot_be_replayed()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        Assert.Equal(HttpStatusCode.OK,
            (await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce))).StatusCode);

        var replay = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));

        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Single(factory.Docker.Executed); // the replay ran nothing
    }

    [Fact]
    public async Task Secret_shaped_output_is_redacted_on_the_result_page()
    {
        using var factory = new ActionFactory();
        factory.ComposeOutcome = operation => WebFixtures.ComposeOk(
            operation, "connect failed: MARIADB_PASSWORD=sup3rsecret refused");
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var html = await (await client.SendAsync(
            Post("/projects/7/actions/logs-all/execute", csrf, nonce))).Content.ReadAsStringAsync();

        Assert.Contains("MARIADB_PASSWORD=***", html);
        Assert.DoesNotContain("sup3rsecret", html);
    }

    // ---------- allowlist enforcement ----------

    [Fact]
    public async Task Unlisted_action_is_rejected_and_nothing_executes()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        // The confirm page fails closed with a reason and no form.
        var confirm = await client.SendAsync(Get("/projects/7/actions/restart-world/confirm"));
        var confirmHtml = await confirm.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains("not on the project", confirmHtml);
        Assert.DoesNotContain("name=\"nonce\"", confirmHtml);

        // A forged POST never reaches the executor.
        var (csrf, _) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/restart-world/execute", csrf, "forged"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Database_restart_is_refused_end_to_end()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var confirm = await client.SendAsync(Get("/projects/7/actions/restart-database/confirm"));
        var confirmHtml = await confirm.Content.ReadAsStringAsync();
        Assert.Contains("allowDatabase", confirmHtml);
        Assert.DoesNotContain("name=\"nonce\"", confirmHtml);

        var (csrf, _) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/restart-database/execute", csrf, "forged"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Docker.Executed);
    }

    [Fact]
    public async Task Unconfigured_predefined_command_renders_a_clean_blocked_page()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/projects/9/actions/launch-test-env/confirm"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("not configured", html);
        Assert.Contains("not available", html);
        Assert.DoesNotContain("name=\"nonce\"", html);
    }

    // ---------- journal on the detail page ----------

    [Fact]
    public async Task Journal_entries_appear_on_the_detail_page()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));

        var html = await (await client.SendAsync(Get("/projects/7"))).Content.ReadAsStringAsync();

        Assert.Contains("<h2>Recent Actions</h2>", html);
        Assert.Contains("logs-all", html);
        Assert.Contains(">succeeded<", html);
    }

    // ---------- double confirmation ----------

    [Fact]
    public async Task A_double_confirmation_action_requires_both_steps()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        // Step 1: the confirm page for a restart points at confirm2, not execute.
        var confirmHtml = await (await client.SendAsync(
            Get("/projects/7/actions/restart-map/confirm"))).Content.ReadAsStringAsync();
        Assert.Contains("double confirmation", confirmHtml);
        Assert.Contains("action=\"/projects/7/actions/restart-map/confirm2\"", confirmHtml);
        Assert.Contains("restart map", confirmHtml.ToLowerInvariant());

        var csrf = HiddenField(confirmHtml, "csrf");
        var stepOneNonce = HiddenField(confirmHtml, "nonce");

        // Skipping step 2: the step-one nonce cannot execute.
        var skipped = await client.SendAsync(Post("/projects/7/actions/restart-map/execute", csrf, stepOneNonce));
        Assert.Equal(HttpStatusCode.Forbidden, skipped.StatusCode);
        Assert.Empty(factory.Docker.Executed);

        // Step 2: a fresh step-one nonce opens the second confirmation page.
        (_, stepOneNonce) = await ConfirmPageTokens(client, "/projects/7/actions/restart-map/confirm");
        var second = await client.SendAsync(Post("/projects/7/actions/restart-map/confirm2", csrf, stepOneNonce));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondHtml = await second.Content.ReadAsStringAsync();
        Assert.Contains("Are you sure?", secondHtml);
        Assert.Contains("action=\"/projects/7/actions/restart-map/execute\"", secondHtml);

        var stepTwoNonce = HiddenField(secondHtml, "nonce");
        Assert.NotEqual(stepOneNonce, stepTwoNonce);

        // Final execute with the step-two nonce runs the restart.
        var executed = await client.SendAsync(Post("/projects/7/actions/restart-map/execute", csrf, stepTwoNonce));
        Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
        Assert.Contains("success", await executed.Content.ReadAsStringAsync());
        var (operation, confirmed) = Assert.Single(factory.Docker.Executed);
        Assert.Equal(ComposeOperationKind.RestartService, operation.Kind);
        Assert.Equal("map", operation.ServiceName);
        Assert.True(confirmed);
    }

    // ---------- method enforcement ----------

    [Fact]
    public async Task Get_on_the_execute_endpoint_is_method_not_allowed()
    {
        using var factory = new ActionFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/projects/7/actions/logs-all/execute"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
