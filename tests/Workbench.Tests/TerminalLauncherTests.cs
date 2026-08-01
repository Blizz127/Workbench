using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class TerminalLauncherTests
{
    [Fact]
    public void Candidates_follow_the_documented_preference_order()
    {
        var ordered = TerminalLauncher.OrderCandidates(
            ["x-terminal-emulator", "konsole", "xfce4-terminal"], terminalOverride: null);

        Assert.Equal(["xfce4-terminal", "konsole", "x-terminal-emulator"],
            ordered.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void The_terminal_environment_override_is_promoted_to_the_front()
    {
        var ordered = TerminalLauncher.OrderCandidates(
            ["xfce4-terminal", "kitty"], terminalOverride: "kitty");

        Assert.Equal("kitty", ordered[0].Name);
        Assert.Equal("-e {command}", ordered[0].ArgumentsTemplate);
        Assert.Equal("xfce4-terminal", ordered[1].Name);
    }

    [Fact]
    public void An_override_that_is_not_installed_is_ignored()
    {
        var ordered = TerminalLauncher.OrderCandidates(["konsole"], terminalOverride: "ghostty");

        Assert.Equal("konsole", Assert.Single(ordered).Name);
    }

    [Fact]
    public void Nothing_available_means_nothing_to_try()
    {
        Assert.Empty(TerminalLauncher.OrderCandidates([], terminalOverride: null));
        Assert.Empty(TerminalLauncher.OrderCandidates(["not-a-real-terminal"], terminalOverride: null));
    }

    [Fact]
    public void Detect_available_only_reports_supported_terminals()
    {
        string[] supported =
            ["xfce4-terminal", "konsole", "x-terminal-emulator", "ptyxis", "gnome-terminal", "kitty", "alacritty", "foot"];

        var detected = new TerminalLauncher().DetectAvailable();

        Assert.All(detected, name => Assert.Contains(name, supported));
        Assert.Equal(detected.Count, detected.Distinct().Count());
    }

    [Fact]
    public void Open_refuses_a_missing_working_directory_without_launching_anything()
    {
        var missing = Path.Combine(Path.GetTempPath(), "workbench-tests", "no-such-dir-" + Guid.NewGuid().ToString("N"));

        Assert.False(new TerminalLauncher().Open(missing, "tmux attach-session -t x"));
    }
}
