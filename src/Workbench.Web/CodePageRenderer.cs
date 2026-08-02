using System.Text;

namespace Workbench.Web;

/// <summary>
/// Renders the five read-only code pages (directory browser, file view,
/// search, working-tree changes, commit detail) from
/// <see cref="ProjectCodeService"/> snapshots. The pages are static — no
/// polling endpoint — and every dynamic value goes through
/// <see cref="WebPageShell.Encode"/>; code and diff text is tokenized by
/// <see cref="SyntaxHighlighter"/>, which encodes as it highlights.
/// </summary>
public static class CodePageRenderer
{
    public static string RenderDirectory(CodeBrowserSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 16 * 1024);
        WebPageShell.WriteHead(html, $"{snapshot.Name} — Code", "code", "");
        WritePageHeader(html, snapshot.Id, snapshot.Name);
        html.Append("<h2>Code</h2>");
        WriteBreadcrumb(html, snapshot.Id, snapshot.Path);

        if (snapshot.Error is { } error)
        {
            WriteErrorCard(html, error);
        }
        else if (snapshot.Entries.Count == 0)
        {
            html.Append("<div class=\"card muted\">Empty directory.</div>");
        }
        else
        {
            html.Append("<div class=\"card\"><ul class=\"lined\">");
            foreach (var entry in snapshot.Entries)
            {
                html.Append("<li><a class=\"mono\" href=\"")
                    .Append(entry.IsDirectory ? FilesUrl(snapshot.Id, entry.RelativePath) : FileUrl(snapshot.Id, entry.RelativePath))
                    .Append("\">").Append(WebPageShell.Encode(entry.Name))
                    .Append(entry.IsDirectory ? "/" : "").Append("</a>");
                if (entry.SizeBytes is { } size)
                    html.Append(" <span class=\"muted\">").Append(SizeText(size)).Append("</span>");
                html.Append("</li>");
            }

            html.Append("</ul></div>");
        }

        return Finish(html);
    }

    public static string RenderFile(FileViewSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 48 * 1024);
        WebPageShell.WriteHead(html, $"{snapshot.Name} — {snapshot.Path}", "code", "");
        WritePageHeader(html, snapshot.Id, snapshot.Name);
        WriteBreadcrumb(html, snapshot.Id, snapshot.Path);

        if (snapshot.Error is { } error)
        {
            WriteErrorCard(html, error);
            return Finish(html);
        }

        if (snapshot.Sha is { } sha)
            html.Append("<p class=\"muted\">Historical version at <span class=\"mono\">")
                .Append(WebPageShell.Encode(sha)).Append("</span> · <a href=\"")
                .Append(FileUrl(snapshot.Id, snapshot.Path)).Append("\">view current file</a></p>");
        else if (snapshot.SizeBytes > 0)
            html.Append("<p class=\"muted\">").Append(SizeText(snapshot.SizeBytes)).Append("</p>");

        var extension = Path.GetExtension(snapshot.Path);
        var highlighted = SyntaxHighlighter.HighlightToHtml(snapshot.Text ?? string.Empty, extension);
        var lines = highlighted.Split('\n');
        var count = lines.Length;
        if (count > 1 && lines[count - 1].Length == 0)
            count--; // trailing newline ends the last real line

        html.Append("<pre class=\"code\">");
        for (var i = 0; i < count; i++)
            html.Append("<span class=\"line\" id=\"L").Append(i + 1).Append("\"><span class=\"ln\">")
                .Append(i + 1).Append("</span>").Append(lines[i]).Append('\n').Append("</span>");
        html.Append("</pre>");

        return Finish(html);
    }

    public static string RenderSearch(SearchPageSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 16 * 1024);
        WebPageShell.WriteHead(html, $"{snapshot.Name} — Search", "code", "");
        WritePageHeader(html, snapshot.Id, snapshot.Name);
        html.Append("<h2>Search</h2>");

        html.Append("<form class=\"card search-form\" method=\"get\" action=\"/projects/").Append(snapshot.Id)
            .Append("/search\"><input type=\"search\" name=\"q\" value=\"")
            .Append(WebPageShell.Encode(snapshot.Query))
            .Append("\" placeholder=\"Search this repository\" aria-label=\"query\">")
            .Append("<div class=\"row\"><label><input type=\"radio\" name=\"kind\" value=\"name\"")
            .Append(snapshot.Kind == "name" ? " checked" : "").Append("> filenames</label>")
            .Append("<label><input type=\"radio\" name=\"kind\" value=\"text\"")
            .Append(snapshot.Kind == "text" ? " checked" : "").Append("> text</label>")
            .Append("<button type=\"submit\">Search</button></div></form>");

        if (snapshot.Error is { } error)
        {
            WriteErrorCard(html, error);
            return Finish(html);
        }

        if (snapshot.Query.Length == 0)
        {
            html.Append("<div class=\"card muted\">Type a query above — filenames or file text.</div>");
            return Finish(html);
        }

        if (snapshot.Truncated)
            html.Append("<p class=\"muted\"><span class=\"badge warn\">truncated</span> Too many hits — showing the first batch.</p>");

        if (snapshot.Kind == "text")
            WriteTextHits(html, snapshot);
        else
            WriteNameHits(html, snapshot);

        return Finish(html);
    }

    public static string RenderChanges(ChangesSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 32 * 1024);
        WebPageShell.WriteHead(html, $"{snapshot.Name} — Changes", "code", "");
        WritePageHeader(html, snapshot.Id, snapshot.Name);
        html.Append("<h2>Changes</h2>");

        if (snapshot.Error is { } error)
        {
            WriteErrorCard(html, error);
        }
        else if (snapshot.Patch is not { Length: > 0 })
        {
            html.Append("<div class=\"card muted\">Working tree is clean — nothing to commit.</div>");
        }
        else
        {
            html.Append("<pre class=\"code diff\">");
            foreach (var line in snapshot.Patch.Replace("\r\n", "\n").Split('\n'))
            {
                var cls = line.StartsWith("@@", StringComparison.Ordinal) ? "diff-hunk"
                    : line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
                        || line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal)
                        ? "diff-meta"
                    : line.StartsWith('+') ? "diff-add"
                    : line.StartsWith('-') ? "diff-del"
                    : "diff-ctx";
                html.Append("<span class=\"").Append(cls).Append("\">")
                    .Append(WebPageShell.Encode(line)).Append('\n').Append("</span>");
            }

            html.Append("</pre>");
        }

        return Finish(html);
    }

    public static string RenderCommit(CommitSnapshot snapshot)
    {
        var html = new StringBuilder(capacity: 16 * 1024);
        WebPageShell.WriteHead(html, $"{snapshot.Name} — Commit {snapshot.Sha}", "code", "");
        WritePageHeader(html, snapshot.Id, snapshot.Name);
        html.Append("<h2>Commit</h2>");

        if (snapshot.Error is "invalid-sha" or "unavailable")
        {
            WriteErrorCard(html, snapshot.Error);
            return Finish(html);
        }

        html.Append("<div class=\"card\"><div class=\"row\"><span class=\"mono grow\">")
            .Append(WebPageShell.Encode(snapshot.Sha)).Append("</span></div>");
        if (snapshot.Commit is { } commit)
            html.Append("<div>").Append(WebPageShell.Encode(commit.MessageSummary)).Append("</div>")
                .Append("<div class=\"muted\">").Append(WebPageShell.Encode(commit.Author))
                .Append(" · ").Append(commit.When.ToString("yyyy-MM-dd HH:mm")).Append("</div>");
        else
            html.Append("<div class=\"muted\">commit details unavailable — outside recent history</div>");
        html.Append("</div>");

        if (snapshot.Error is { } error)
        {
            WriteErrorCard(html, error);
        }
        else if (snapshot.Files.Count == 0)
        {
            html.Append("<div class=\"card muted\">No file changes.</div>");
        }
        else
        {
            html.Append("<div class=\"card\"><ul class=\"lined\">");
            foreach (var file in snapshot.Files)
            {
                html.Append("<li>");
                if (file.Kind == "deleted")
                    html.Append("<span class=\"mono\">").Append(WebPageShell.Encode(file.Path)).Append("</span>");
                else
                    html.Append("<a class=\"mono\" href=\"")
                        .Append(FileUrl(snapshot.Id, file.Path, snapshot.Sha)).Append("\">")
                        .Append(WebPageShell.Encode(file.Path)).Append("</a>");
                html.Append(" <span class=\"badge ").Append(BadgeClass(file.Kind)).Append("\">")
                    .Append(WebPageShell.Encode(file.Kind)).Append("</span></li>");
            }

            html.Append("</ul></div>");
        }

        return Finish(html);
    }

    // ---------- helpers ----------

    private static void WritePageHeader(StringBuilder html, long projectId, string name)
    {
        html.Append("<a class=\"back-link\" href=\"/projects/").Append(projectId).Append("\">← ")
            .Append(WebPageShell.Encode(name)).Append("</a>");
    }

    /// <summary>Path segments as links; the last one (current directory or file) is plain text.</summary>
    private static void WriteBreadcrumb(StringBuilder html, long projectId, string path)
    {
        html.Append("<nav class=\"breadcrumb\"><a href=\"").Append(FilesUrl(projectId, null)).Append("\">root</a>");
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        for (var i = 0; i < segments.Length; i++)
        {
            current = current.Length == 0 ? segments[i] : $"{current}/{segments[i]}";
            var last = i == segments.Length - 1;
            html.Append(" <span class=\"muted\">/</span> ");
            if (last)
                html.Append(WebPageShell.Encode(segments[i]));
            else
                html.Append("<a href=\"").Append(FilesUrl(projectId, current)).Append("\">")
                    .Append(WebPageShell.Encode(segments[i])).Append("</a>");
        }

        html.Append("</nav>");
    }

    private static void WriteNameHits(StringBuilder html, SearchPageSnapshot snapshot)
    {
        if (snapshot.NameHits.Count == 0)
        {
            html.Append("<div class=\"card muted\">No filenames match.</div>");
            return;
        }

        html.Append("<div class=\"card\"><ul class=\"lined\">");
        foreach (var hit in snapshot.NameHits)
            html.Append("<li><a class=\"mono\" href=\"")
                .Append(hit.IsDirectory ? FilesUrl(snapshot.Id, hit.RelativePath) : FileUrl(snapshot.Id, hit.RelativePath))
                .Append("\">").Append(WebPageShell.Encode(hit.RelativePath))
                .Append(hit.IsDirectory ? "/" : "").Append("</a></li>");
        html.Append("</ul></div>");
    }

    private static void WriteTextHits(StringBuilder html, SearchPageSnapshot snapshot)
    {
        if (snapshot.TextHits.Count == 0)
        {
            html.Append("<div class=\"card muted\">No text matches.</div>");
            return;
        }

        html.Append("<div class=\"card\"><ul class=\"lined\">");
        foreach (var group in snapshot.TextHits.GroupBy(h => h.RelativePath))
        {
            html.Append("<li><a class=\"mono\" href=\"").Append(FileUrl(snapshot.Id, group.Key)).Append("\">")
                .Append(WebPageShell.Encode(group.Key)).Append("</a><ul class=\"lined\">");
            foreach (var hit in group)
                html.Append("<li><a class=\"mono\" href=\"")
                    .Append(FileUrl(snapshot.Id, hit.RelativePath)).Append("&amp;line=").Append(hit.LineNumber)
                    .Append("#L").Append(hit.LineNumber).Append("\"><span class=\"muted\">")
                    .Append(hit.LineNumber).Append("</span> ")
                    .Append(WebPageShell.Encode(hit.LineText)).Append("</a></li>");
            html.Append("</ul></li>");
        }

        html.Append("</ul></div>");
    }

    private static void WriteErrorCard(StringBuilder html, string error) =>
        html.Append("<div class=\"card\"><span class=\"badge bad\">unavailable</span> <span class=\"muted\">")
            .Append(WebPageShell.Encode(ErrorText(error))).Append("</span></div>");

    private static string ErrorText(string error) => error switch
    {
        "unavailable" => "The project's repository is not available.",
        "forbidden" => "That path is outside the repository or cannot be read.",
        "not-found" => "Nothing found there — it may be missing, binary, or too large.",
        "not-a-file" => "That path is a directory, not a file.",
        "not-a-directory" => "That path is a file, not a directory.",
        "too-large" => "That file is too large to display (over 512 KB).",
        "binary" => "Binary file — nothing to display.",
        "invalid-sha" => "That commit id is invalid.",
        _ => error,
    };

    private static string BadgeClass(string kind) => kind switch
    {
        "added" => "added",
        "deleted" => "deleted",
        "renamed" => "renamed",
        _ => "modified",
    };

    private static string SizeText(long bytes) =>
        bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";

    private static string FilesUrl(long projectId, string? path) =>
        $"/projects/{projectId}/files" + (path is { Length: > 0 } ? $"?path={Uri.EscapeDataString(path)}" : "");

    private static string FileUrl(long projectId, string path, string? sha = null) =>
        $"/projects/{projectId}/file?path={Uri.EscapeDataString(path)}"
        + (sha is { Length: > 0 } ? $"&amp;sha={Uri.EscapeDataString(sha)}" : "");

    private static string Finish(StringBuilder html)
    {
        WebPageShell.WriteScript(html);
        return html.ToString();
    }
}
