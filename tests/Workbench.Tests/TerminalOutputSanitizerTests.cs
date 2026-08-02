using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class TerminalOutputSanitizerTests
{
    [Fact]
    public void Plain_text_passes_through_unchanged()
    {
        const string text = "build OK\n12 tests passed\n";
        Assert.Equal(text, TerminalOutputSanitizer.Sanitize(text));
    }

    [Fact]
    public void Ansi_color_and_cursor_sequences_are_stripped()
    {
        Assert.Equal("error: failed", TerminalOutputSanitizer.Sanitize("\x1B[31merror\x1B[0m: \x1B[1Kfailed"));
        Assert.Equal("done", TerminalOutputSanitizer.Sanitize("\x1B[1;32mdone\x1B[0m"));
    }

    [Fact]
    public void Osc_sequences_are_stripped_with_both_terminators()
    {
        Assert.Equal("link", TerminalOutputSanitizer.Sanitize("\x1B]8;;https://example.com\alink\x1B]8;;\a"));
        Assert.Equal("title rest", TerminalOutputSanitizer.Sanitize("\x1B]0;window title\x1B\\title rest"));
    }

    [Fact]
    public void Newlines_and_tabs_survive_but_other_controls_are_dropped()
    {
        Assert.Equal("a\tb\nc", TerminalOutputSanitizer.Sanitize("a\tb\r\nc\a"));
        Assert.Equal("ab", TerminalOutputSanitizer.Sanitize("a\u007Fb"));
        Assert.Equal("ab", TerminalOutputSanitizer.Sanitize("a\u0085b"));
    }

    [Fact]
    public void Replacement_characters_are_dropped()
    {
        Assert.Equal("caf", TerminalOutputSanitizer.Sanitize("caf\uFFFD"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_and_empty_yield_empty(string? text)
    {
        Assert.Equal(string.Empty, TerminalOutputSanitizer.Sanitize(text));
    }
}
