using System.Text;

namespace Workbench.Web;

/// <summary>
/// Dependency-free syntax highlighting for the code browser. Produces one
/// HTML fragment per source line (joined by "\n", never containing a raw
/// newline) so the page renderer can wrap each in a numbered
/// <c>&lt;span class="line"&gt;</c>. Tokens become
/// <c>&lt;span class="tok-*"&gt;</c> runs (tok-k keyword, tok-c comment,
/// tok-s string, tok-n number); everything is HTML-encoded, and unknown
/// extensions degrade to encoding only. Block-comment state carries across
/// lines (<c>/* */</c>, lua <c>--[[ ]]</c>, xml <c>&lt;!-- --&gt;</c>).
/// </summary>
public static class SyntaxHighlighter
{
    /// <summary>Highlights <paramref name="text"/> for the given file extension (no dot, case-insensitive).</summary>
    public static string HighlightToHtml(string text, string extension)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (For(extension) is not { } spec)
            return string.Join("\n", lines.Select(WebPageShell.Encode));

        var html = new StringBuilder(capacity: text.Length + text.Length / 4);
        var inBlockComment = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                html.Append('\n');
            HighlightLine(html, lines[i], spec, ref inBlockComment);
        }

        return html.ToString();
    }

    private static void HighlightLine(
        StringBuilder html, string line, LanguageSpec spec, ref bool inBlockComment)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (inBlockComment)
            {
                var end = line.IndexOf(spec.BlockComment!.Value.End, i, StringComparison.Ordinal);
                if (end < 0)
                {
                    Span(html, "tok-c", line[i..]);
                    i = line.Length;
                }
                else
                {
                    Span(html, "tok-c", line[i..(end + spec.BlockComment.Value.End.Length)]);
                    i = end + spec.BlockComment.Value.End.Length;
                    inBlockComment = false;
                }

                continue;
            }

            var rest = line[i..];
            // Block openers first: lua's "--[[" also starts with the "--" line
            // comment, and the block must win.
            if (spec.BlockComment is { } block && rest.StartsWith(block.Start, StringComparison.Ordinal))
            {
                var end = line.IndexOf(block.End, i + block.Start.Length, StringComparison.Ordinal);
                if (end < 0)
                {
                    Span(html, "tok-c", rest);
                    inBlockComment = true;
                    i = line.Length;
                }
                else
                {
                    Span(html, "tok-c", line[i..(end + block.End.Length)]);
                    i = end + block.End.Length;
                }

                continue;
            }

            if (spec.LineComments.Any(rest.StartsWith))
            {
                Span(html, "tok-c", rest);
                break;
            }

            var c = line[i];
            if (spec.StringDelimiters.Contains(c))
            {
                var end = i + 1;
                while (end < line.Length)
                {
                    if (line[end] == '\\')
                        end += 2; // escaped character never terminates the string
                    else if (line[end] == c)
                        break;
                    else
                        end++;
                }

                var stop = Math.Min(end + 1, line.Length);
                Span(html, "tok-s", line[i..stop]);
                i = stop;
                continue;
            }

            if (char.IsDigit(c))
            {
                var end = i + 1;
                while (end < line.Length &&
                       (char.IsLetterOrDigit(line[end]) || line[end] is '.' or '_' or '\''))
                    end++;
                Span(html, "tok-n", line[i..end]);
                i = end;
                continue;
            }

            if (char.IsLetter(c) || c is '_' or '#' or '@')
            {
                var end = i + 1;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                    end++;
                var word = line[i..end];
                if (spec.Keywords.Contains(word))
                    Span(html, "tok-k", word);
                else
                    html.Append(WebPageShell.Encode(word));
                i = end;
                continue;
            }

            html.Append(WebPageShell.Encode(c.ToString()));
            i++;
        }
    }

    private static void Span(StringBuilder html, string tokenClass, string text) =>
        html.Append("<span class=\"").Append(tokenClass).Append("\">")
            .Append(WebPageShell.Encode(text)).Append("</span>");

    private sealed record LanguageSpec(
        string[] LineComments,
        (string Start, string End)? BlockComment,
        char[] StringDelimiters,
        HashSet<string> Keywords);

    private static LanguageSpec? For(string extension) =>
        extension.TrimStart('.').ToLowerInvariant() switch
        {
            "cs" => CSharp,
            "lua" => Lua,
            "c" or "h" or "cc" or "cpp" or "hpp" or "cxx" => Cpp,
            "json" => Json,
            "xml" or "yml" or "yaml" => Xml,
            "sh" => Shell,
            _ => null,
        };

    private static HashSet<string> Words(string words) =>
        new(words.Split(' '), StringComparer.Ordinal);

    private static readonly LanguageSpec CSharp = new(
        ["//"], ("/*", "*/"), ['"', '\''],
        Words("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while record get set init partial async await nameof when yield"));

    private static readonly LanguageSpec Lua = new(
        ["--"], ("--[[", "]]"), ['"', '\''],
        Words("and break do else elseif end false for function goto if in local nil not or repeat return then true until while"));

    private static readonly LanguageSpec Cpp = new(
        ["//"], ("/*", "*/"), ['"', '\''],
        Words("alignas auto bool break case catch char class const constexpr continue decltype default delete do double else enum explicit export extern false float for friend goto if include inline int long mutable namespace new noexcept nullptr operator private protected public register return short signed sizeof static struct switch template this throw true try typedef typename union unsigned using virtual void volatile while"));

    private static readonly LanguageSpec Json = new(
        [], null, ['"'],
        Words("true false null"));

    private static readonly LanguageSpec Xml = new(
        ["#"], ("<!--", "-->"), ['"', '\''],
        Words("true false null"));

    private static readonly LanguageSpec Shell = new(
        ["#"], null, ['"', '\''],
        Words("if then else elif fi for while until do done case esac in function return local export source echo cd eval exec exit set shift trap readonly unset"));
}
