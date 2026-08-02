using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

/// <summary>SyntaxHighlighter token spans, HTML escaping, and cross-line block-comment state.</summary>
public sealed class SyntaxHighlighterTests
{
    [Fact]
    public void Hostile_characters_are_html_escaped()
    {
        var html = SyntaxHighlighter.HighlightToHtml("if (a < b && c > \"d\")", "cs");

        Assert.Contains("&lt;", html);
        Assert.Contains("&gt;", html);
        Assert.Contains("&amp;&amp;", html);
        Assert.Contains("&quot;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Keywords_get_a_keyword_span()
    {
        var html = SyntaxHighlighter.HighlightToHtml("while (true) { }", "cs");

        Assert.Contains("<span class=\"tok-k\">while</span>", html);
        Assert.Contains("<span class=\"tok-k\">true</span>", html);
    }

    [Fact]
    public void Line_comments_get_a_comment_span()
    {
        var html = SyntaxHighlighter.HighlightToHtml("int a; // trailing <note>", "cs");

        Assert.Contains("<span class=\"tok-c\">// trailing &lt;note&gt;</span>", html);
    }

    [Fact]
    public void Block_comment_state_carries_across_lines()
    {
        var html = SyntaxHighlighter.HighlightToHtml("int a; /* start\nmiddle while\nend */ int b;", "cs");
        var lines = html.Split('\n');

        Assert.Contains("<span class=\"tok-c\">/* start</span>", lines[0]);
        Assert.Equal("<span class=\"tok-c\">middle while</span>", lines[1]); // "while" stays comment, not keyword
        Assert.Contains("<span class=\"tok-c\">end */</span>", lines[2]);
        Assert.Contains("<span class=\"tok-k\">int</span>", lines[2]); // code resumes after the block
    }

    [Fact]
    public void Strings_with_escapes_get_a_string_span()
    {
        var html = SyntaxHighlighter.HighlightToHtml("var s = \"a\\\"b\";", "cs");

        // The escaped quote must not terminate the string early.
        Assert.Contains("<span class=\"tok-s\">&quot;a\\&quot;b&quot;</span>", html);
    }

    [Fact]
    public void Numbers_get_a_number_span()
    {
        var html = SyntaxHighlighter.HighlightToHtml("x = 42;", "cs");

        Assert.Contains("<span class=\"tok-n\">42</span>", html);
    }

    [Fact]
    public void Lua_line_and_block_comments_span_lines()
    {
        var html = SyntaxHighlighter.HighlightToHtml("-- greeting\nlocal x --[[ open\nstill comment\nclose ]] = 1", "lua");
        var lines = html.Split('\n');

        Assert.Equal("<span class=\"tok-c\">-- greeting</span>", lines[0]);
        Assert.Contains("<span class=\"tok-c\">--[[ open</span>", lines[1]);
        Assert.Equal("<span class=\"tok-c\">still comment</span>", lines[2]);
        Assert.Contains("<span class=\"tok-c\">close ]]</span>", lines[3]);
        Assert.Contains("<span class=\"tok-n\">1</span>", lines[3]);
    }

    [Fact]
    public void Unknown_extensions_escape_without_any_spans()
    {
        var html = SyntaxHighlighter.HighlightToHtml("if (x < 1)", "xyz");

        Assert.Equal("if (x &lt; 1)", html);
        Assert.DoesNotContain("tok-", html);
    }
}
