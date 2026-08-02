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
/// Host-level tests for the persistent mobile action audit: the full
/// confirm → (confirm2) → execute flow writes real SQLite rows (each factory
/// gets its own temp database), the audit pages read them back, and nothing
/// secret-shaped ever lands in a stored field.
/// </summary>
public sealed class MobileActionAuditHostTests : IDisposable
{
    private const string Token = "test-token";
    private const string ComposePath = "/repo/banshee/docker-compose.docker.yml";

    private readonly List<string> _databasePaths = [];

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in _databasePaths)
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    private sealed class AuditFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath;

        public AuditFactory(string databasePath)
        {
            _databasePath = databasePath;
            Docker = new WebStubs.Docker(
                p => p.ComposeFile is null ? null : ComposePath,
                _ => WebFixtures.BansheeCompose(),
                (_, operation, _) => Task.FromResult(ComposeOutcome(operation)));
        }

        /// <summary>What the stubbed compose execution returns; replaceable per test.</summary>
        public Func<ComposeOperation, ComposeOperationResult> ComposeOutcome { get; set; } =
            operation => WebFixtures.ComposeOk(operation, "log line one\nlog line two");

        public WebStubs.Docker Docker { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Web:AuthToken"] = Token,
                    ["Web:DatabasePath"] = _databasePath,
                }));

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IProjectRepository>(
                    new WebStubs.Projects(WebFixtures.Banshee(), WebFixtures.Xenogears()));
                services.AddSingleton<ITimelineRepository>(new WebStubs.Timeline());
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

    private AuditFactory CreateFactory()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), "workbench-tests", "audit-" + Guid.NewGuid().ToString("N") + ".db");
        _databasePaths.Add(databasePath);
        return new AuditFactory(databasePath);
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

    private static IReadOnlyList<MobileActionAuditRecord> AuditRows(IServiceProvider services, long projectId) =>
        services.GetRequiredService<IMobileActionAuditRepository>()
            .Query(new MobileActionAuditFilter { ProjectId = projectId, Limit = 500 });

    // ---------- lifecycle ----------

    [Fact]
    public async Task Attempted_and_succeeded_share_one_correlation_id_end_to_end()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = AuditRows(factory.Services, 7);
        Assert.Equal(2, rows.Count);
        var attempted = Assert.Single(rows, r => r.State == MobileActionAuditState.Attempted);
        var succeeded = Assert.Single(rows, r => r.State == MobileActionAuditState.Succeeded);

        Assert.NotEqual("", attempted.CorrelationId);
        Assert.Equal(attempted.CorrelationId, succeeded.CorrelationId);
        Assert.Equal("View all logs", attempted.ActionLabel);
        Assert.Contains("docker compose", attempted.CommandPreview);
        Assert.Equal(0, succeeded.ExitCode);
        Assert.NotNull(succeeded.DurationMs);
        Assert.Contains("log line one", succeeded.OutputSummary);
        Assert.False(succeeded.OutputTruncated);
    }

    [Fact]
    public async Task Double_confirmation_carries_the_step_one_correlation_id_into_execution()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var confirmHtml = await (await client.SendAsync(
            Get("/projects/7/actions/restart-map/confirm"))).Content.ReadAsStringAsync();
        var csrf = HiddenField(confirmHtml, "csrf");
        var stepOneNonce = HiddenField(confirmHtml, "nonce");

        var secondHtml = await (await client.SendAsync(
            Post("/projects/7/actions/restart-map/confirm2", csrf, stepOneNonce))).Content.ReadAsStringAsync();
        var stepTwoNonce = HiddenField(secondHtml, "nonce");

        var executed = await client.SendAsync(Post("/projects/7/actions/restart-map/execute", csrf, stepTwoNonce));
        Assert.Equal(HttpStatusCode.OK, executed.StatusCode);

        var rows = AuditRows(factory.Services, 7);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.State == MobileActionAuditState.Attempted);
        Assert.Single(rows, r => r.State == MobileActionAuditState.Succeeded);
        Assert.Single(rows.Select(r => r.CorrelationId).Distinct()); // one id through both nonces
        Assert.Equal(["map"], rows[0].Services);
    }

    [Fact]
    public async Task A_failed_action_stores_a_redacted_failure_summary_and_the_exit_code()
    {
        using var factory = CreateFactory();
        factory.ComposeOutcome = operation => WebFixtures.ComposeFailed(
            operation, "restart failed: MARIADB_PASSWORD=sup3rsecret refused");
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        var response = await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = AuditRows(factory.Services, 7);
        var failed = Assert.Single(rows, r => r.State == MobileActionAuditState.Failed);
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("MARIADB_PASSWORD=***", failed.OutputSummary);
        Assert.NotNull(failed.FailureReason);
        Assert.DoesNotContain("sup3rsecret", failed.OutputSummary!);
        Assert.DoesNotContain("sup3rsecret", failed.FailureReason!);
    }

    // ---------- secret hygiene ----------

    [Fact]
    public async Task Session_id_token_csrf_and_nonce_never_appear_in_any_stored_field()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));

        var sessionId = factory.Services.GetRequiredService<SessionStore>().IssueForHeaderToken(Token);
        var rows = AuditRows(factory.Services, 7);
        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            string[] fields =
            [
                row.ProjectName, row.ActionId, row.ActionLabel, row.ActionType, row.Safety,
                row.SessionHash ?? "", row.CommandPreview ?? "", row.OutputSummary ?? "",
                row.FailureReason ?? "", row.Source, row.CorrelationId,
            ];
            foreach (var field in fields)
            {
                Assert.DoesNotContain(sessionId, field);
                Assert.DoesNotContain(Token, field);
                Assert.DoesNotContain(csrf, field);
                Assert.DoesNotContain(nonce, field);
            }

            // The session fingerprint is exactly the SHA-256 hash of the session id.
            Assert.Equal(MobileActionAuditService.HashSessionId(sessionId), row.SessionHash);
        }
    }

    // ---------- audit pages ----------

    [Fact]
    public async Task Audit_pages_read_back_the_stored_history()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));
        var row = AuditRows(factory.Services, 7).First(r => r.State == MobileActionAuditState.Succeeded);

        var list = await (await client.SendAsync(Get("/audit"))).Content.ReadAsStringAsync();
        Assert.Contains("Action audit", list);
        Assert.Contains("View all logs", list);
        Assert.Contains(">succeeded<", list);
        Assert.Contains($"/audit/{row.Id}", list);

        var filtered = await (await client.SendAsync(
            Get("/audit?project=7&state=failed"))).Content.ReadAsStringAsync();
        Assert.Contains("No audit records match.", filtered);

        var detail = await (await client.SendAsync(Get($"/audit/{row.Id}"))).Content.ReadAsStringAsync();
        Assert.Contains("log line one", detail);
        Assert.Contains(row.CorrelationId, detail);
        Assert.Contains("exit code", detail);
    }

    [Fact]
    public async Task Page_refreshes_create_zero_audit_rows()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (csrf, nonce) = await ConfirmPageTokens(client, "/projects/7/actions/logs-all/confirm");
        await client.SendAsync(Post("/projects/7/actions/logs-all/execute", csrf, nonce));
        var before = AuditRows(factory.Services, 7).Count;
        var detailId = AuditRows(factory.Services, 7).First().Id;

        await client.SendAsync(Get("/audit"));
        await client.SendAsync(Get("/audit?project=7&state=succeeded"));
        await client.SendAsync(Get($"/audit/{detailId}"));
        await client.SendAsync(Get("/projects/7/actions/logs-all/confirm")); // re-GET confirm
        await client.SendAsync(Get("/projects/7"));                          // detail page with journal

        Assert.Equal(before, AuditRows(factory.Services, 7).Count);
    }

    [Fact]
    public async Task Audit_pages_require_authentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/audit/1")).StatusCode);
    }

    [Fact]
    public async Task Audit_pages_reject_post()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var listPost = new HttpRequestMessage(HttpMethod.Post, "/audit");
        listPost.Headers.Add("X-Workbench-Token", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(listPost)).StatusCode);

        using var detailPost = new HttpRequestMessage(HttpMethod.Post, "/audit/1");
        detailPost.Headers.Add("X-Workbench-Token", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(detailPost)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_audit_id_is_a_styled_404()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/audit/4242"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Audit record not found", await response.Content.ReadAsStringAsync());
    }
}
