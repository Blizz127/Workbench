using System.Text;
using System.Text.RegularExpressions;

namespace Workbench.Core.Services;

/// <summary>
/// Prepares captured process output for display. Raw logs can carry ANSI
/// escape/control sequences (colors, cursor movement, hyperlinks) and C0/C1
/// control characters, which render as glyph noise or malformed replacement
/// characters in a plain text panel. Sanitizing strips them, keeping only
/// printable text, newlines, and tabs. Display-only: stored logs are untouched.
/// </summary>
public static partial class TerminalOutputSanitizer
{
    // CSI: ESC [ parameters intermediates final-byte (colors, cursor, erase…)
    // OSC: ESC ] payload terminated by BEL or ST (titles, hyperlinks…)
    // Remaining: ESC + optional intermediate bytes (charset selection, DECSC…)
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\)|[()#%][0-9A-Za-z]?|[@-Z\\-_])", RegexOptions.Singleline)]
    private static partial Regex AnsiSequence();

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var stripped = AnsiSequence().Replace(text, string.Empty);
        var builder = new StringBuilder(stripped.Length);
        foreach (var c in stripped)
        {
            // Keep printable text plus newline/tab; drop CR, other control
            // characters (char.IsControl covers C0, DEL and C1), and U+FFFD
            // replacement characters left by encoding mismatches.
            if (c is '\n' or '\t')
                builder.Append(c);
            else if (!char.IsControl(c) && c != '\uFFFD')
                builder.Append(c);
        }

        return builder.ToString();
    }
}
