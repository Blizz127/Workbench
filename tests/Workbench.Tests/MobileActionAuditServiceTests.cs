using Microsoft.Extensions.Logging.Abstractions;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data;
using Workbench.Data.Repositories;
using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Unit tests for the persistent mobile action audit service: field mapping
/// for every state, redaction and truncation before storage, session hashing,
/// correlation ids, and failure isolation. Real temp SQLite where practical.
/// </summary>
public sealed class MobileActionAuditServiceTests : IDisposable
{
    private const string BansheeComposePath = "/repo/banshee/docker-compose.docker.yml";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private (MobileActionAuditService Service, MobileActionAuditRepository Repository) RealService()
    {
        var database = new WorkbenchDatabase(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".db"));
        var repo = new MobileActionAuditRepository(database);
        return (new MobileActionAuditService(repo, NullLogger<MobileActionAuditService>.Instance), repo);
    }

    private static MobileActionPreview Preview(
        string actionId = "logs-all", string label = "View all logs",
        string type = "dockerLogsAll", MobileActionSafety safety = MobileActionSafety.ReadOnly,
        string commandPreview = "docker compose logs --tail=200") =>
        new(7, "Banshee Realm 11",
            new MobileActionDescriptor(actionId, label, type, safety, null, null),
            commandPreview, safety, BlockReason: null);

    private static MobileActionExecution Execution(
        MobileActionPreview preview, bool succeeded, int? exitCode, string output) =>
        new("exec1", preview.Action, succeeded, Blocked: false, exitCode,
            TimeSpan.FromSeconds(2), output, DateTimeOffset.Now);

    // ---------- state transitions ----------

    [Fact]
    public void Attempted_blocked_and_rejected_states_are_stored_with_their_fields()
    {
        var (service, repo) = RealService();
        var context = new MobileActionAuditContext("session-1", "corr-x");

        service.RecordAttempted(context, Preview(), ["map"], "corr-x");
        service.RecordRefused(context, Preview("restart-map", "Restart map", "dockerRestartService",
                MobileActionSafety.RequiresDoubleConfirmation),
            MobileActionAuditState.Blocked, "Refusing to restart database-like service 'db'.", ["db"], "corr-y");
        service.RecordRefused(context, Preview("hack", "hack", "unknown"),
            MobileActionAuditState.Rejected, "This action is not on the project's mobile allowlist.", [], "corr-z");

        var records = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.Equal(3, records.Count);

        var attempted = records.Single(r => r.State == MobileActionAuditState.Attempted);
        Assert.Equal("corr-x", attempted.CorrelationId);
        Assert.Equal("Banshee Realm 11", attempted.ProjectName); // snapshot
        Assert.Equal("View all logs", attempted.ActionLabel);    // snapshot
        Assert.Equal("dockerLogsAll", attempted.ActionType);
        Assert.Equal("readOnly", attempted.Safety);
        Assert.Equal("docker compose logs --tail=200", attempted.CommandPreview);
        Assert.Equal(["map"], attempted.Services);
        Assert.Equal(MobileActionAuditService.HashSessionId("session-1"), attempted.SessionHash);
        Assert.Null(attempted.ExitCode);
        Assert.Null(attempted.DurationMs);

        var blocked = records.Single(r => r.State == MobileActionAuditState.Blocked);
        Assert.Equal("requiresDoubleConfirmation", blocked.Safety);
        Assert.Contains("database-like", blocked.FailureReason);
        Assert.Equal(["db"], blocked.Services);

        var rejected = records.Single(r => r.State == MobileActionAuditState.Rejected);
        Assert.Contains("allowlist", rejected.FailureReason);
        Assert.Empty(rejected.Services);
    }

    [Fact]
    public async Task The_full_execute_flow_persists_every_transition_under_one_correlation_id()
    {
        var (service, repo) = RealService();
        var harness = AuditHarness.Create(service);

        var context = new MobileActionAuditContext("session-2", "corr-flow");
        var result = await harness.Service.ExecuteAsync(7, "logs-all", confirmed: false, context);

        Assert.True(result!.Succeeded);
        var records = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal("corr-flow", r.CorrelationId));
        Assert.Single(records, r => r.State == MobileActionAuditState.Attempted);
        var succeeded = Assert.Single(records, r => r.State == MobileActionAuditState.Succeeded);
        Assert.Equal(0, succeeded.ExitCode);
        Assert.NotNull(succeeded.DurationMs);
        Assert.Contains("log line one", succeeded.OutputSummary);
    }

    // ---------- redaction / truncation ----------

    [Fact]
    public void Secret_shaped_output_and_command_previews_are_redacted_before_storage()
    {
        var (service, repo) = RealService();
        var preview = Preview(commandPreview: "docker compose up -e MARIADB_PASSWORD=sup3rsecret");
        var context = new MobileActionAuditContext(null, "corr-1");

        service.RecordAttempted(context, preview, [], "corr-1");
        service.RecordTerminal(context, preview,
            Execution(preview, succeeded: false, 1, "connect failed: MARIADB_PASSWORD=sup3rsecret refused"),
            [], "corr-1");

        var records = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.All(records, r =>
        {
            Assert.DoesNotContain("sup3rsecret", r.CommandPreview ?? "");
            Assert.DoesNotContain("sup3rsecret", r.OutputSummary ?? "");
            Assert.DoesNotContain("sup3rsecret", r.FailureReason ?? "");
        });
        var terminal = records.Single(r => r.State == MobileActionAuditState.Failed);
        Assert.Contains("MARIADB_PASSWORD=***", terminal.OutputSummary);
        Assert.Equal(1, terminal.ExitCode);
        Assert.NotNull(terminal.FailureReason);
    }

    [Fact]
    public void Output_over_the_limit_is_truncated_with_the_flag_set_and_the_state_still_correct()
    {
        var (service, repo) = RealService();
        var preview = Preview();
        var hugeOutput = new string('x', MobileActionAuditService.OutputSummaryLimit + 500);

        service.RecordTerminal(null, preview,
            Execution(preview, succeeded: true, 0, hugeOutput), [], "corr-1");

        var record = Assert.Single(repo.Query(new MobileActionAuditFilter { Limit = 50 }));
        Assert.Equal(MobileActionAuditState.Succeeded, record.State);
        Assert.True(record.OutputTruncated);
        Assert.Equal(MobileActionAuditService.OutputSummaryLimit, record.OutputSummary!.Length);
        Assert.Equal(0, record.ExitCode);
        Assert.Null(record.FailureReason); // success keeps no failure reason
    }

    [Fact]
    public void Terminal_state_mapping_covers_succeeded_failed_cancelled_and_timed_out()
    {
        var (service, repo) = RealService();
        var preview = Preview();
        var failed = Execution(preview, succeeded: false, 1, "boom");
        var ok = Execution(preview, succeeded: true, 0, "fine");

        service.RecordTerminal(null, preview, ok, [], "c-ok");
        service.RecordTerminal(null, preview, failed, [], "c-failed");
        service.RecordTerminal(null, preview, failed, [], "c-cancelled", MobileActionAuditState.Cancelled);
        service.RecordTerminal(null, preview, failed, [], "c-timeout", MobileActionAuditState.TimedOut);

        var records = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.Equal(MobileActionAuditState.Succeeded, records.Single(r => r.CorrelationId == "c-ok").State);
        Assert.Equal(MobileActionAuditState.Failed, records.Single(r => r.CorrelationId == "c-failed").State);
        Assert.Equal(MobileActionAuditState.Cancelled, records.Single(r => r.CorrelationId == "c-cancelled").State);
        Assert.Equal(MobileActionAuditState.TimedOut, records.Single(r => r.CorrelationId == "c-timeout").State);
    }

    // ---------- session hashing ----------

    [Fact]
    public void The_session_id_is_stored_only_as_a_sha256_hash()
    {
        var (service, repo) = RealService();
        const string sessionId = "raw-session-id-that-must-never-be-stored";
        var context = new MobileActionAuditContext(sessionId, "corr-1");
        var preview = Preview();

        service.RecordAttempted(context, preview, [], "corr-1");
        service.RecordTerminal(context, preview, Execution(preview, true, 0, "ok"), [], "corr-1");

        var records = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.All(records, r =>
        {
            Assert.Equal(MobileActionAuditService.HashSessionId(sessionId), r.SessionHash);
            Assert.Matches("^[0-9a-f]{64}$", r.SessionHash);
            Assert.DoesNotContain(sessionId, r.SessionHash);
            Assert.DoesNotContain(sessionId, r.CommandPreview ?? "");
            Assert.DoesNotContain(sessionId, r.OutputSummary ?? "");
        });
    }

    // ---------- failure isolation ----------

    [Fact]
    public void Persistence_failures_are_counted_and_never_thrown()
    {
        var service = new MobileActionAuditService(
            new WebStubs.ThrowingAudits(), NullLogger<MobileActionAuditService>.Instance);
        var preview = Preview();
        var context = new MobileActionAuditContext("s", "corr-1");

        service.RecordAttempted(context, preview, [], "corr-1");
        service.RecordRefused(context, preview, MobileActionAuditState.Blocked, "nope", [], "corr-1");
        service.RecordTerminal(context, preview, Execution(preview, true, 0, "ok"), [], "corr-1");

        Assert.Equal(3, service.WriteFailures);
        Assert.Empty(service.Query(new MobileActionAuditFilter()));
        Assert.Null(service.GetById(1));
        Assert.Equal(0, service.EnforceRetention(5000, 90)); // retention failure reports zero
    }

    [Fact]
    public async Task A_failing_audit_store_does_not_re_execute_block_or_fail_the_action()
    {
        var audit = new MobileActionAuditService(
            new WebStubs.ThrowingAudits(), NullLogger<MobileActionAuditService>.Instance);
        var harness = AuditHarness.Create(audit);

        var result = await harness.Service.ExecuteAsync(7, "logs-all", confirmed: false,
            new MobileActionAuditContext("s", "corr-1"));

        Assert.True(result!.Succeeded);
        Assert.Single(harness.Docker.Executed); // exactly one run — never retried
        Assert.Equal(2, audit.WriteFailures);   // attempted + terminal both failed to persist
    }

    // ---------- retention passthrough ----------

    [Fact]
    public void Retention_enforcement_delegates_to_the_repository()
    {
        var (service, repo) = RealService();
        var now = DateTimeOffset.UtcNow;
        int[] agesDays = [-100, -99, -98, -5, 0];
        foreach (var age in agesDays)
        {
            repo.Add(new MobileActionAuditRecord
            {
                TimestampUtc = now.AddDays(age), ProjectId = 7, ProjectName = "p",
                ActionId = "a", ActionLabel = "a", ActionType = "t", Safety = "readOnly",
                State = MobileActionAuditState.Succeeded, CorrelationId = $"c{age}",
            });
        }

        var deleted = service.EnforceRetention(maxRecords: 5000, maxAgeDays: 90);

        Assert.Equal(3, deleted); // the three 100/99/98-day-old rows
        Assert.Equal(2, repo.Query(new MobileActionAuditFilter { Limit = 50 }).Count);
    }

    /// <summary>Minimal MobileActionService harness with a real audit service attached.</summary>
    private sealed class AuditHarness
    {
        public WebStubs.Docker Docker = null!;
        public MobileActionService Service = null!;

        public static AuditHarness Create(MobileActionAuditService audit)
        {
            var projects = new WebStubs.Projects(WebFixtures.Banshee(), WebFixtures.Xenogears());
            var docker = new WebStubs.Docker(
                p => p.ComposeFile is null ? null : BansheeComposePath,
                _ => null,
                (_, operation, _) => Task.FromResult(WebFixtures.ComposeOk(operation, "log line one\nlog line two")));
            var tmux = new WebStubs.Tmux(WebFixtures.TmuxRunning());
            var configs = new WebStubs.ProjectConfigs(path =>
                path == WebFixtures.BansheeRepoDir ? WebFixtures.BansheeConfig() : WebFixtures.XenogearsConfig());
            var git = new WebStubs.Git(WebFixtures.BansheeGit());
            var timeline = new WebStubs.Timeline();
            var blockers = new WebStubs.Blockers();
            var projection = new DashboardProjectionBuilder(
                projects, new WebStubs.Milestones(), blockers, new WebStubs.Frontiers(),
                new WebStubs.RuntimeHealthRecords(), new WebStubs.BuildHistory(), new WebStubs.TestHistory(),
                timeline, new WebStubs.MobileActionAudits(), git);
            var snapshots = new DashboardSnapshotService(
                new WebStubs.SystemHealth(), projects,
                git, docker, tmux,
                timeline, blockers, projection, configs);

            return new AuditHarness
            {
                Docker = docker,
                Service = new MobileActionService(
                    projects, configs, docker, tmux,
                    WebStubs.PredefinedCommands.NeverCalled(), snapshots, new ActionJournal(), audit),
            };
        }
    }
}
