using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Unit tests for the mobile action service with every external dependency
/// stubbed — no Docker, tmux, filesystem config, or database is touched.
/// </summary>
public sealed class MobileActionServiceTests
{
    private const string BansheeComposePath = "/repo/banshee/docker-compose.docker.yml";

    private sealed class Harness
    {
        public WebStubs.Projects Projects = new(WebFixtures.Banshee(), WebFixtures.Xenogears());
        public WebStubs.Docker Docker = new(
            p => p.ComposeFile is null ? null : BansheeComposePath,
            _ => null,
            (_, operation, _) => Task.FromResult(WebFixtures.ComposeOk(operation, "log line one\nlog line two")));
        public WebStubs.Tmux Tmux = new(WebFixtures.TmuxRunning(),
            execute: (operation, _) => Task.FromResult(
                new TmuxOperationResult(operation, true, false, 0, "")));
        public WebStubs.ProjectConfigs Configs = new(path =>
            path == WebFixtures.BansheeRepoDir ? WebFixtures.BansheeConfig() : WebFixtures.XenogearsConfig());
        public WebStubs.PredefinedCommands Predefined = WebStubs.PredefinedCommands.NeverCalled();
        public ActionJournal Journal = new();
        public MobileActionService Service = null!;

        public static Harness Create(Action<Harness>? customize = null)
        {
            var harness = new Harness();
            customize?.Invoke(harness);
            var git = new WebStubs.Git(WebFixtures.BansheeGit());
            var timeline = new WebStubs.Timeline();
            var blockers = new WebStubs.Blockers();
            var projection = new DashboardProjectionBuilder(
                harness.Projects, new WebStubs.Milestones(), blockers, new WebStubs.Frontiers(),
                new WebStubs.RuntimeHealthRecords(), new WebStubs.BuildHistory(), new WebStubs.TestHistory(),
                timeline, new WebStubs.MobileActionAudits(), git);
            var snapshots = new DashboardSnapshotService(
                new WebStubs.SystemHealth(), harness.Projects,
                git, harness.Docker, harness.Tmux,
                timeline, blockers, projection, harness.Configs);
            harness.Service = new MobileActionService(
                harness.Projects, harness.Configs, harness.Docker, harness.Tmux,
                harness.Predefined, snapshots, harness.Journal);
            return harness;
        }
    }

    // ---------- allowlist ----------

    [Fact]
    public void Actions_include_the_internal_read_only_entries_and_the_config_allowlist()
    {
        var actions = Harness.Create().Service.GetActions(7);

        Assert.Contains(actions, a => a.Id == MobileActionService.RefreshStatusId && a.IsReadOnly);
        Assert.Contains(actions, a => a.Id == MobileActionService.TmuxAttachInfoId && a.IsReadOnly);
        Assert.Contains(actions, a => a.Id == "logs-all");
        Assert.Contains(actions, a => a.Id == "restart-map" && a.IsMutating);
    }

    [Fact]
    public void Unknown_project_has_no_actions_and_no_preview()
    {
        var service = Harness.Create().Service;

        Assert.Empty(service.GetActions(999));
        Assert.Null(service.BuildPreview(999, "logs-all"));
    }

    // ---------- execution ----------

    [Fact]
    public async Task Allowlisted_logs_action_succeeds()
    {
        var harness = Harness.Create();

        var result = (await harness.Service.ExecuteAsync(7, "logs-all", confirmed: false))!;

        Assert.True(result.Succeeded);
        Assert.False(result.Blocked);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("log line one", result.Output);
        var (operation, _) = Assert.Single(harness.Docker.Executed);
        Assert.Equal(ComposeOperationKind.LogsAll, operation.Kind);
    }

    [Fact]
    public async Task Unlisted_action_is_rejected_and_nothing_runs()
    {
        var harness = Harness.Create();

        var result = (await harness.Service.ExecuteAsync(7, "restart-redis", confirmed: true))!;

        Assert.False(result.Succeeded);
        Assert.True(result.Blocked);
        Assert.Contains("allowlist", result.Output);
        Assert.Empty(harness.Docker.Executed);
        Assert.Contains(harness.Journal.Recent(7, 10), e => e.Outcome == ActionOutcome.Rejected);
    }

    [Fact]
    public async Task Database_restart_is_rejected_without_allow_database()
    {
        var harness = Harness.Create();

        var result = (await harness.Service.ExecuteAsync(7, "restart-database", confirmed: true))!;

        Assert.True(result.Blocked);
        Assert.Contains("allowDatabase", result.Output);
        Assert.Empty(harness.Docker.Executed);
        Assert.Contains(harness.Journal.Recent(7, 10), e => e.Outcome == ActionOutcome.Blocked);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("db")]
    [InlineData("postgres-16")]  // prefix match
    [InlineData("REDIS")]        // case-insensitive
    public async Task Database_like_service_names_are_all_refused(string service)
    {
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions =
            [
                new() { Id = "restart-target", Label = "restart", Type = "dockerRestartService", Service = service },
            ],
        }));

        var result = (await harness.Service.ExecuteAsync(7, "restart-target", confirmed: true))!;

        Assert.True(result.Blocked);
        Assert.Empty(harness.Docker.Executed);
    }

    // ---------- bulk restart (dockerRestartAllowed) ----------

    [Fact]
    public async Task Bulk_restart_restarts_only_services_with_their_own_restart_entries()
    {
        // database and db-update run in the stack but have no restart entries,
        // so they can never enter the bulk set.
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions =
            [
                new() { Id = "restart-map", Label = "m", Type = "dockerRestartService", Service = "map" },
                new() { Id = "restart-search", Label = "s", Type = "dockerRestartService", Service = "search" },
                new() { Id = "restart-world", Label = "w", Type = "dockerRestartService", Service = "world" },
                new() { Id = "restart-connect", Label = "c", Type = "dockerRestartService", Service = "connect" },
                new() { Id = "logs-database", Label = "l", Type = "dockerLogsService", Service = "database" },
                new() { Id = "restart-game", Label = "all", Type = "dockerRestartAllowed" },
            ],
        }));

        var result = (await harness.Service.ExecuteAsync(7, "restart-game", confirmed: true))!;

        Assert.True(result.Succeeded);
        var restarted = harness.Docker.Executed.Select(e => e.Operation.ServiceName).ToList();
        Assert.Equal(["map", "search", "world", "connect"], restarted);
        Assert.All(harness.Docker.Executed, e =>
        {
            Assert.Equal(ComposeOperationKind.RestartService, e.Operation.Kind);
            Assert.True(e.Confirmed);
        });
    }

    [Fact]
    public async Task Bulk_restart_skips_database_like_entries_even_when_explicitly_configured()
    {
        // Defense in depth: even if someone adds database/db-update restart
        // entries without allowDatabase, the bulk path must skip them.
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions =
            [
                new() { Id = "restart-map", Label = "m", Type = "dockerRestartService", Service = "map" },
                new() { Id = "restart-database", Label = "d", Type = "dockerRestartService", Service = "database" },
                new() { Id = "restart-db-update", Label = "u", Type = "dockerRestartService", Service = "db-update" },
                new() { Id = "restart-game", Label = "all", Type = "dockerRestartAllowed" },
            ],
        }));

        var result = (await harness.Service.ExecuteAsync(7, "restart-game", confirmed: true))!;

        var operation = Assert.Single(harness.Docker.Executed).Operation;
        Assert.Equal("map", operation.ServiceName);
        Assert.Contains("== database == skipped", result.Output);
        Assert.Contains("== db-update == skipped", result.Output);
    }

    [Fact]
    public async Task Allow_database_true_permits_the_database_restart()
    {
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions =
            [
                new()
                {
                    Id = "restart-database", Label = "Restart database", Type = "dockerRestartService",
                    Service = "database", AllowDatabase = true,
                },
            ],
        }));

        var result = (await harness.Service.ExecuteAsync(7, "restart-database", confirmed: true))!;

        Assert.True(result.Succeeded);
        var (operation, confirmed) = Assert.Single(harness.Docker.Executed);
        Assert.Equal(ComposeOperationKind.RestartService, operation.Kind);
        Assert.True(confirmed);
    }

    [Fact]
    public async Task Service_name_not_in_the_config_is_never_restarted()
    {
        var harness = Harness.Create();

        // The allowlist has restart-map only — "world" is a real service but not allowlisted.
        var result = (await harness.Service.ExecuteAsync(7, "restart-world", confirmed: true))!;

        Assert.True(result.Blocked);
        Assert.Empty(harness.Docker.Executed);
    }

    [Fact]
    public async Task Restart_allowed_restarts_each_allowlisted_service_in_order()
    {
        var harness = Harness.Create();

        var result = (await harness.Service.ExecuteAsync(7, "restart-game", confirmed: true))!;

        Assert.True(result.Succeeded);
        // BansheeConfig has restart-map and restart-database; the database one
        // is skipped by the hard guard, map is restarted.
        var (operation, _) = Assert.Single(harness.Docker.Executed);
        Assert.Equal("map", operation.ServiceName);
        Assert.Contains("skipped", result.Output);
        Assert.Contains("database", result.Output);
    }

    [Fact]
    public async Task Unknown_action_type_fails_closed()
    {
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions = [new() { Id = "hack", Label = "Run shell", Type = "shellExec" }],
        }));

        var descriptor = Assert.Single(harness.Service.GetActions(7), a => a.Id == "hack");
        Assert.NotNull(descriptor.DisabledReason);
        Assert.Contains("Unknown action type", descriptor.DisabledReason);

        var preview = harness.Service.BuildPreview(7, "hack");
        Assert.NotNull(preview?.BlockReason);

        var result = (await harness.Service.ExecuteAsync(7, "hack", confirmed: true))!;
        Assert.True(result.Blocked);
        Assert.Empty(harness.Docker.Executed);
    }

    // ---------- safety ----------

    [Fact]
    public void Safety_defaults_follow_the_action_type()
    {
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions =
            [
                new() { Id = "a", Label = "a", Type = "dockerLogsAll" },
                new() { Id = "b", Label = "b", Type = "dockerLogsService", Service = "map" },
                new() { Id = "c", Label = "c", Type = "dockerRestartService", Service = "map" },
                new() { Id = "d", Label = "d", Type = "dockerRestartAllowed" },
                new() { Id = "e", Label = "e", Type = "tmuxCreateSession" },
                new() { Id = "f", Label = "f", Type = "predefinedCommand", CommandId = "build" },
                new() { Id = "g", Label = "g", Type = "predefinedCommand", CommandId = "test" },
                new() { Id = "h", Label = "h", Type = "predefinedCommand", CommandId = "launch" },
            ],
        }));

        var actions = harness.Service.GetActions(7);
        MobileActionSafety SafetyOf(string id) => actions.Single(a => a.Id == id).Safety;

        Assert.Equal(MobileActionSafety.ReadOnly, SafetyOf("a"));
        Assert.Equal(MobileActionSafety.ReadOnly, SafetyOf("b"));
        Assert.Equal(MobileActionSafety.RequiresDoubleConfirmation, SafetyOf("c"));
        Assert.Equal(MobileActionSafety.RequiresDoubleConfirmation, SafetyOf("d"));
        Assert.Equal(MobileActionSafety.RequiresConfirmation, SafetyOf("e"));
        Assert.Equal(MobileActionSafety.RequiresConfirmation, SafetyOf("f"));
        Assert.Equal(MobileActionSafety.RequiresConfirmation, SafetyOf("g"));
        Assert.Equal(MobileActionSafety.RequiresDoubleConfirmation, SafetyOf("h"));
    }

    [Fact]
    public void Safety_config_can_raise_but_never_lower_the_default()
    {
        var harness = Harness.Create(h => h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
        {
            MobileActions =
            [
                new() { Id = "lower", Label = "l", Type = "dockerRestartService", Service = "map", Safety = "readOnly" },
                new() { Id = "raise", Label = "r", Type = "dockerLogsAll", Safety = "requiresDoubleConfirmation" },
            ],
        }));

        var actions = harness.Service.GetActions(7);
        Assert.Equal(MobileActionSafety.RequiresDoubleConfirmation, actions.Single(a => a.Id == "lower").Safety);
        Assert.Equal(MobileActionSafety.RequiresDoubleConfirmation, actions.Single(a => a.Id == "raise").Safety);
    }

    [Fact]
    public async Task Mutating_action_without_confirmation_is_rejected()
    {
        var harness = Harness.Create();

        var result = (await harness.Service.ExecuteAsync(7, "restart-map", confirmed: false))!;

        Assert.True(result.Blocked);
        Assert.Contains("confirmation", result.Output);
        Assert.Empty(harness.Docker.Executed);
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task A_second_concurrent_execution_is_blocked_not_queued()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource<ComposeOperationResult>();
        var harness = Harness.Create(h => h.Docker = new WebStubs.Docker(
            _ => BansheeComposePath, _ => null,
            async (_, operation, _) =>
            {
                entered.TrySetResult();
                return await release.Task;
            }));

        var first = harness.Service.ExecuteAsync(7, "logs-all", confirmed: false);
        await entered.Task; // first execution is now inside the stubbed compose call

        var second = (await harness.Service.ExecuteAsync(7, "logs-map", confirmed: false))!;

        Assert.True(second.Blocked);
        Assert.Contains("already running", second.Output);

        release.SetResult(WebFixtures.ComposeOk(harness.Docker.Executed[0].Operation));
        Assert.True((await first)!.Succeeded);
    }

    // ---------- tmux ----------

    [Fact]
    public async Task Tmux_create_session_builds_the_create_command_rooted_at_the_repo()
    {
        // Session does not exist yet — the fixture's tmux server has one, so override.
        var harness = Harness.Create(h => h.Tmux = new WebStubs.Tmux(
            WebFixtures.TmuxRunning(),
            execute: (operation, _) => Task.FromResult(
                new TmuxOperationResult(operation, true, false, 0, "")),
            sessionExists: _ => false));

        var result = (await harness.Service.ExecuteAsync(7, "new-session", confirmed: true))!;

        var operation = Assert.Single(harness.Tmux.Executed);
        Assert.Equal(TmuxOperationKind.CreateSession, operation.Kind);
        Assert.Equal($"tmux new-session -d -s banshee -c \"{WebFixtures.BansheeRepoDir}\"", operation.Command);
        Assert.Contains(operation.Command, harness.Service.BuildPreview(7, "new-session")!.CommandPreview);
        _ = result;
    }

    [Fact]
    public async Task Tmux_create_session_noops_cleanly_when_the_session_exists()
    {
        // The fixture tmux server already has a "banshee" session.
        var harness = Harness.Create();

        var result = (await harness.Service.ExecuteAsync(7, "new-session", confirmed: true))!;

        Assert.True(result.Succeeded);
        Assert.Contains("already exists", result.Output);
        Assert.Empty(harness.Tmux.Executed);
    }

    // ---------- predefined commands ----------

    [Fact]
    public async Task Predefined_command_with_unconfigured_id_is_blocked_cleanly()
    {
        var harness = Harness.Create();

        var preview = harness.Service.BuildPreview(9, "launch-test-env");
        Assert.NotNull(preview);
        Assert.False(preview.IsExecutable);
        Assert.Contains("not configured", preview.BlockReason);

        var result = (await harness.Service.ExecuteAsync(9, "launch-test-env", confirmed: true))!;
        Assert.True(result.Blocked);
        Assert.Contains("not configured", result.Output);
    }

    [Fact]
    public async Task Predefined_build_runs_the_workbench_json_command_through_the_runner()
    {
        var harness = Harness.Create(h =>
        {
            h.Configs = new WebStubs.ProjectConfigs(_ => new ProjectConfig
            {
                BuildCommand = "dotnet build Workbench.sln",
                MobileActions =
                [
                    new() { Id = "build", Label = "Build", Type = "predefinedCommand", CommandId = "build" },
                ],
            });
            h.Predefined = new WebStubs.PredefinedCommands((_, _, command) =>
                Task.FromResult<PredefinedCommandResult?>(new PredefinedCommandResult(
                    true, 0, TimeSpan.FromSeconds(3), "Build succeeded.", DateTimeOffset.Now)));
        });

        var result = (await harness.Service.ExecuteAsync(7, "build", confirmed: true))!;

        Assert.True(result.Succeeded);
        var (kind, command) = Assert.Single(harness.Predefined.Calls);
        Assert.Equal(PredefinedCommandKind.Build, kind);
        Assert.Equal("dotnet build Workbench.sln", command);
        Assert.Equal("dotnet build Workbench.sln",
            harness.Service.BuildPreview(7, "build")!.CommandPreview);
    }

    // ---------- preview text ----------

    [Fact]
    public void Command_preview_shows_the_exact_command_that_will_run()
    {
        var harness = Harness.Create();

        var preview = harness.Service.BuildPreview(7, "logs-map");

        Assert.NotNull(preview);
        Assert.Null(preview.BlockReason);
        Assert.Equal(
            $"docker compose -f \"{BansheeComposePath}\" logs --no-color --tail=200 map",
            preview.CommandPreview);
    }

    [Fact]
    public void Attach_info_previews_the_tmux_attach_command()
    {
        var harness = Harness.Create();

        var preview = harness.Service.BuildPreview(7, MobileActionService.TmuxAttachInfoId);

        Assert.NotNull(preview);
        Assert.True(preview.IsExecutable);
        Assert.Equal("tmux attach-session -t banshee", preview.CommandPreview);
    }

    // ---------- journaling ----------

    [Fact]
    public async Task Journal_records_attempted_blocked_succeeded_failed_and_rejected()
    {
        var harness = Harness.Create(h => h.Docker = new WebStubs.Docker(
            _ => BansheeComposePath, _ => null,
            (_, operation, _) => Task.FromResult(operation.Kind == ComposeOperationKind.LogsAll
                ? WebFixtures.ComposeOk(operation)
                : WebFixtures.ComposeFailed(operation))));

        await harness.Service.ExecuteAsync(7, "logs-all", confirmed: false);       // Attempted + Succeeded
        await harness.Service.ExecuteAsync(7, "logs-map", confirmed: false);       // Attempted + Failed
        await harness.Service.ExecuteAsync(7, "restart-database", confirmed: true); // Attempted + Blocked
        await harness.Service.ExecuteAsync(7, "not-allowed", confirmed: true);      // Attempted + Rejected

        var entries = harness.Journal.Recent(7, 50);
        Assert.Equal(8, entries.Count);
        Assert.Equal(4, entries.Count(e => e.Outcome == ActionOutcome.Attempted));
        Assert.Single(entries, e => e.Outcome == ActionOutcome.Succeeded && e.ActionId == "logs-all");
        Assert.Single(entries, e => e.Outcome == ActionOutcome.Failed && e.ActionId == "logs-map");
        Assert.Single(entries, e => e.Outcome == ActionOutcome.Blocked && e.ActionId == "restart-database");
        Assert.Single(entries, e => e.Outcome == ActionOutcome.Rejected && e.ActionId == "not-allowed");
        // Newest first.
        Assert.Equal("not-allowed", entries[0].ActionId);
    }
}
