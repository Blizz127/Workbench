using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class TmuxServiceTests
{
    /// <summary>Returns queued results; never touches real processes or tmux.</summary>
    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new();

        public List<string> Commands { get; } = [];

        public void Enqueue(string output, int? exitCode = 0) =>
            _results.Enqueue(new ProcessResult("stub", DateTimeOffset.Now, exitCode, TimeSpan.FromMilliseconds(5), output, false));

        public Task<ProcessResult> RunAsync(
            string command, string? workingDirectory, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new ProcessResult(command, DateTimeOffset.Now, 0, TimeSpan.Zero, string.Empty, false));
        }
    }

    [Fact]
    public async Task Inspect_runs_the_read_only_list_command_and_parses_sessions()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("banshee|1785610417|0|1|bash\ndev|1785700000|2|3|vim");

        var status = await new TmuxService(runner).InspectAsync();

        Assert.True(status.TmuxAvailable);
        Assert.True(status.ServerRunning);
        Assert.Null(status.Error);
        Assert.Equal(2, status.Sessions.Count);
        Assert.Equal("banshee", status.Sessions[0].Name);
        Assert.True(status.Sessions[1].IsAttached);
        Assert.Equal(TmuxCommandBuilder.ListSessionsCommand, Assert.Single(runner.Commands));
    }

    [Theory]
    [InlineData("no server running on /tmp/tmux-1000/default")]
    [InlineData("error connecting to /tmp/tmux-1000/default (No such file or directory)")]
    [InlineData("can't find socket")]
    public async Task Inspect_treats_no_server_running_as_a_normal_empty_state(string tmuxMessage)
    {
        var runner = new StubProcessRunner();
        runner.Enqueue(tmuxMessage, exitCode: 1);

        var status = await new TmuxService(runner).InspectAsync();

        Assert.True(status.TmuxAvailable);
        Assert.False(status.ServerRunning);
        Assert.Empty(status.Sessions);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task Inspect_reports_tmux_missing_when_the_process_never_started()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("executable not found: tmux", exitCode: null);

        var status = await new TmuxService(runner).InspectAsync();

        Assert.False(status.TmuxAvailable);
        Assert.False(status.ServerRunning);
        Assert.NotNull(status.Error);
    }

    [Fact]
    public async Task Inspect_surfaces_an_unexpected_failure_as_an_error()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("tmux: permission denied on socket", exitCode: 1);

        var status = await new TmuxService(runner).InspectAsync();

        Assert.True(status.TmuxAvailable);
        Assert.False(status.ServerRunning);
        Assert.Equal("tmux: permission denied on socket", status.Error);
    }

    [Fact]
    public async Task Session_exists_probes_and_never_runs_for_an_invalid_name()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("", exitCode: 0);
        var service = new TmuxService(runner);

        Assert.True(await service.SessionExistsAsync("banshee"));
        Assert.Equal("tmux has-session -t banshee", Assert.Single(runner.Commands));

        runner.Enqueue("can't find session", exitCode: 1);
        Assert.False(await service.SessionExistsAsync("missing"));

        Assert.False(await service.SessionExistsAsync("bad;name"));
        Assert.Equal(2, runner.Commands.Count);
    }

    [Fact]
    public async Task Kill_and_rename_are_blocked_without_explicit_confirmation()
    {
        var runner = new StubProcessRunner();
        var service = new TmuxService(runner);

        var kill = await service.ExecuteAsync(TmuxCommandBuilder.BuildKill("dev"), confirmed: false);
        var rename = await service.ExecuteAsync(TmuxCommandBuilder.BuildRename("dev", "dev2"), confirmed: false);

        Assert.True(kill.WasBlocked);
        Assert.False(kill.Succeeded);
        Assert.True(rename.WasBlocked);

        // The confirmation gate held: not a single command reached the runner.
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Confirmed_kill_runs_exactly_the_previewed_command()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("");
        var service = new TmuxService(runner);

        var operation = TmuxCommandBuilder.BuildKill("workbench-phase1d-test");
        var result = await service.ExecuteAsync(operation, confirmed: true);

        Assert.True(result.Succeeded);
        Assert.Equal(operation.Command, Assert.Single(runner.Commands));
    }

    [Fact]
    public async Task Create_runs_without_confirmation_but_still_only_on_request()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("");

        var result = await new TmuxService(runner).ExecuteAsync(
            TmuxCommandBuilder.BuildCreate("dev", "/home/blizz/dev/workbench"));

        Assert.True(result.Succeeded);
        Assert.Equal("tmux new-session -d -s dev -c \"/home/blizz/dev/workbench\"",
            Assert.Single(runner.Commands));
    }

    [Fact]
    public async Task Failed_operation_reports_failure_with_exit_code()
    {
        var runner = new StubProcessRunner();
        runner.Enqueue("duplicate session: dev", exitCode: 1);

        var result = await new TmuxService(runner).ExecuteAsync(
            TmuxCommandBuilder.BuildCreate("dev"));

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("duplicate session", result.Output);
    }

    [Fact]
    public void Project_session_name_prefers_a_valid_explicit_tmux_session()
    {
        var service = new TmuxService(new StubProcessRunner());
        var project = new Project { Name = "Banshee Realm 11", TmuxSession = "banshee-dev" };

        Assert.Equal("banshee-dev", service.ProjectSessionName(project));
    }

    [Fact]
    public void An_invalid_explicit_name_falls_back_to_the_synthesized_slug()
    {
        var service = new TmuxService(new StubProcessRunner());
        var project = new Project { Name = "Banshee Realm 11", TmuxSession = "bad;name" };

        Assert.Equal("banshee-realm-11", service.ProjectSessionName(project));
    }

    [Theory]
    [InlineData("Banshee Realm 11", "banshee-realm-11")]
    [InlineData("TEN Revival", "ten-revival")]
    [InlineData("xenogears", "xenogears")]
    [InlineData("  Parasite  Eve  ", "parasite-eve")]
    [InlineData("!!!", "project")]
    public void Slugs_are_lowercase_safe_and_never_empty(string projectName, string expectedSlug)
    {
        var service = new TmuxService(new StubProcessRunner());

        Assert.Equal(expectedSlug, service.ProjectSessionName(new Project { Name = projectName }));
    }
}
