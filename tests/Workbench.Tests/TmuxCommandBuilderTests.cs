using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class TmuxCommandBuilderTests
{
    [Fact]
    public void List_sessions_command_is_the_stable_pipe_delimited_probe()
    {
        Assert.Equal(
            "tmux list-sessions -F '#{session_name}|#{session_created}|#{session_attached}|#{session_windows}|#{window_name}'",
            TmuxCommandBuilder.ListSessionsCommand);
    }

    [Fact]
    public void Create_builds_a_detached_session_without_confirmation()
    {
        var operation = TmuxCommandBuilder.BuildCreate("workbench-phase1d-test");

        Assert.Equal(TmuxOperationKind.CreateSession, operation.Kind);
        Assert.Equal("tmux new-session -d -s workbench-phase1d-test", operation.Command);
        Assert.False(operation.RequiresConfirmation);
    }

    [Fact]
    public void Create_with_a_working_directory_roots_the_session_there()
    {
        var operation = TmuxCommandBuilder.BuildCreate("ten-revival", "/home/blizz/dev/ten");

        Assert.Equal("tmux new-session -d -s ten-revival -c \"/home/blizz/dev/ten\"", operation.Command);
    }

    [Fact]
    public void Rename_requires_confirmation()
    {
        var operation = TmuxCommandBuilder.BuildRename("old-name", "new-name");

        Assert.Equal(TmuxOperationKind.RenameSession, operation.Kind);
        Assert.Equal("tmux rename-session -t old-name new-name", operation.Command);
        Assert.True(operation.RequiresConfirmation);
    }

    [Fact]
    public void Kill_targets_exactly_one_session_and_requires_confirmation()
    {
        var operation = TmuxCommandBuilder.BuildKill("workbench-phase1d-test");

        Assert.Equal(TmuxOperationKind.KillSession, operation.Kind);
        Assert.Equal("tmux kill-session -t workbench-phase1d-test", operation.Command);
        Assert.True(operation.RequiresConfirmation);
    }

    [Fact]
    public void Probe_and_attach_commands_are_exact()
    {
        Assert.Equal("tmux has-session -t banshee", TmuxCommandBuilder.HasSessionCommand("banshee"));
        Assert.Equal("tmux attach-session -t banshee", TmuxCommandBuilder.AttachCommand("banshee"));
    }

    [Fact]
    public void No_builder_offers_fleet_wide_or_server_killing_primitives()
    {
        var commands = new[]
        {
            TmuxCommandBuilder.ListSessionsCommand,
            TmuxCommandBuilder.HasSessionCommand("s"),
            TmuxCommandBuilder.AttachCommand("s"),
            TmuxCommandBuilder.BuildCreate("s").Command,
            TmuxCommandBuilder.BuildRename("s", "t").Command,
            TmuxCommandBuilder.BuildKill("s").Command,
        };

        foreach (var command in commands)
        {
            Assert.DoesNotContain("kill-server", command);
            Assert.DoesNotContain(" -a", command);
        }
    }

    [Theory]
    [InlineData("banshee")]
    [InlineData("workbench-phase1d-test")]
    [InlineData("TEN_revival-2")]
    public void Valid_session_names_are_accepted(string name)
    {
        Assert.True(TmuxCommandBuilder.IsValidSessionName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("dot.name")]
    [InlineData("colon:name")]
    [InlineData("a;rm -rf /")]
    [InlineData("$(reboot)")]
    [InlineData("a|b")]
    public void Injection_shaped_names_are_rejected(string? name)
    {
        Assert.False(TmuxCommandBuilder.IsValidSessionName(name));
        Assert.Throws<ArgumentException>(() => TmuxCommandBuilder.BuildCreate(name!));
        Assert.Throws<ArgumentException>(() => TmuxCommandBuilder.BuildKill(name!));
    }

    [Fact]
    public void Rename_rejects_an_invalid_new_name()
    {
        Assert.Throws<ArgumentException>(() => TmuxCommandBuilder.BuildRename("valid", "not valid"));
    }
}
