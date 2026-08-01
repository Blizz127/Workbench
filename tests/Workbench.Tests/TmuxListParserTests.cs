using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class TmuxListParserTests
{
    [Fact]
    public void Parses_a_real_detached_session_line()
    {
        // Captured from tmux 3.6: list-sessions -F of the live 'banshee' session.
        var sessions = TmuxListParser.Parse("banshee|1785610417|0|1|bash");

        var session = Assert.Single(sessions);
        Assert.Equal("banshee", session.Name);
        Assert.False(session.IsAttached);
        Assert.Equal(1, session.WindowCount);
        Assert.Equal("bash", session.ActiveWindowName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785610417), session.Created);
    }

    [Fact]
    public void Attached_count_above_zero_means_attached()
    {
        var session = Assert.Single(TmuxListParser.Parse("dev|1785610417|2|3|work"));

        Assert.True(session.IsAttached);
        Assert.Equal(3, session.WindowCount);
    }

    [Fact]
    public void Parses_multiple_sessions_and_ignores_blank_lines()
    {
        var sessions = TmuxListParser.Parse(
            "banshee|1785610417|0|1|bash\n\ndev|1785700000|1|2|vim\n");

        Assert.Equal(2, sessions.Count);
        Assert.Equal("dev", sessions[1].Name);
        Assert.True(sessions[1].IsAttached);
    }

    [Fact]
    public void Empty_output_means_no_sessions()
    {
        Assert.Empty(TmuxListParser.Parse(string.Empty));
        Assert.Empty(TmuxListParser.Parse("\n  \n"));
    }

    [Fact]
    public void Malformed_lines_are_skipped()
    {
        var sessions = TmuxListParser.Parse(string.Join('\n',
            "no-pipes-here",
            "only|two|parts",
            "badepoch|notanumber|0|1|bash",
            "badattached|1785610417|x|1|bash",
            "|1785610417|0|1|noname",
            "good|1785610417|0|1|bash"));

        var session = Assert.Single(sessions);
        Assert.Equal("good", session.Name);
    }

    [Fact]
    public void Missing_or_empty_window_name_is_null()
    {
        Assert.Null(Assert.Single(TmuxListParser.Parse("s|1785610417|0|1")).ActiveWindowName);
        Assert.Null(Assert.Single(TmuxListParser.Parse("s|1785610417|0|1|")).ActiveWindowName);
    }

    [Fact]
    public void Window_name_containing_the_delimiter_is_rejoined()
    {
        var session = Assert.Single(TmuxListParser.Parse("s|1785610417|0|1|a|b"));

        Assert.Equal("a|b", session.ActiveWindowName);
    }

    [Fact]
    public void Zero_epoch_means_unknown_creation_time()
    {
        Assert.Null(Assert.Single(TmuxListParser.Parse("s|0|0|1|bash")).Created);
    }
}
