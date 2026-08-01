using System.Text.RegularExpressions;

namespace Workbench.Core.Services;

/// <summary>
/// Redacts password-like values from command output before it reaches the UI
/// or the journal. Matches <c>KEY=value</c> / <c>KEY: value</c> / JSON-style
/// <c>"KEY": "value"</c> pairs whose key contains a secret-ish word; the key
/// and any quoting are preserved, only the value becomes <c>***</c>.
/// </summary>
public static partial class SecretRedactor
{
    public const string Replacement = "***";

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return SecretPattern().Replace(text, match =>
        {
            var group = match.Groups["dq"].Success ? match.Groups["dq"]
                : match.Groups["sq"].Success ? match.Groups["sq"]
                : match.Groups["bare"];
            // Empty values hide nothing; leave them untouched.
            if (group.Value.Length == 0)
                return match.Value;

            // Replace exactly the captured value span, keeping key, separator,
            // and any quoting intact.
            var start = group.Index - match.Index;
            return string.Concat(match.Value.AsSpan(0, start), Replacement, match.Value.AsSpan(start + group.Length));
        });
    }

    [GeneratedRegex(
        @"(?:\b|[""'])[\w-]*(?:password|passwd|pwd|secret|token|(?:api|access|private)[-_]?key)\b[""']?\s*[:=]\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s"",;&]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SecretPattern();
}
