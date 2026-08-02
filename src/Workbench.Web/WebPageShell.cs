using System.Net;
using System.Text;

namespace Workbench.Web;

/// <summary>
/// Shared page furniture for both web pages: the HTML head, the phone-first
/// dark stylesheet, and the client-side live-refresh script. The script polls
/// the page's JSON endpoint every 10s (server-side TTL caches cap the actual
/// subsystem refresh rates at 10/15/30s), pauses while the tab is hidden,
/// never overlaps requests, updates the DOM in place (scroll position is
/// preserved), and marks the page stale on errors without blanking data.
/// </summary>
internal static class WebPageShell
{
    public static void WriteHead(StringBuilder html, string title, string pageKind, string endpoint)
    {
        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n")
            .Append("<meta name=\"color-scheme\" content=\"dark\">\n<title>")
            .Append(Encode(title))
            .Append("</title>\n<style>")
            .Append(Css)
            .Append("</style>\n</head>\n<body data-page=\"")
            .Append(pageKind)
            .Append("\" data-endpoint=\"")
            .Append(endpoint)
            .Append("\">\n");
    }

    public static void WriteScript(StringBuilder html) =>
        html.Append("<script>").Append(ClientScript).Append("</script>\n</body>\n</html>");

    public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private const string Css = """
        :root { color-scheme: dark; }
        * { box-sizing: border-box; margin: 0; }
        [hidden] { display: none !important; }
        body { background: #0f1115; color: #e6e8ee; font-family: system-ui, -apple-system, sans-serif;
               font-size: 16px; line-height: 1.45; padding: 12px; max-width: 640px; margin: 0 auto; }
        h1 { font-size: 1.25rem; margin: 8px 0 2px; }
        h2 { font-size: 1.0rem; color: #9aa3b2; margin-bottom: 8px; text-transform: uppercase;
             letter-spacing: .05em; font-weight: 600; }
        .muted { color: #9aa3b2; font-size: .85rem; }
        .card { background: #1a1d24; border: 1px solid #2a2e38; border-radius: 12px;
                padding: 14px; margin: 12px 0; }
        .project-card { min-height: 44px; }
        .project-card:hover { border-color: #4f8ff7; }
        .summary { display: flex; flex-wrap: wrap; gap: 4px 14px; margin: 2px 0 6px;
                   font-size: .85rem; color: #9aa3b2; }
        .summary-item strong { color: #e6e8ee; font-variant-numeric: tabular-nums; }
        .view-link { display: inline-flex; align-items: center; min-height: 44px; color: #4f8ff7;
                     font-weight: 600; font-size: .95rem; }
        .back-link { display: inline-flex; align-items: center; min-height: 44px; color: #4f8ff7;
                     text-decoration: none; font-weight: 600; }
        .metrics { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
        .metric { background: #12141a; border-radius: 8px; padding: 10px 12px; }
        .metric .value { font-size: 1.1rem; font-weight: 600; font-variant-numeric: tabular-nums; }
        .bar { height: 4px; background: #2a2e38; border-radius: 2px; margin-top: 6px; overflow: hidden; }
        .bar > div { height: 100%; background: #4f8ff7; border-radius: 2px; }
        .row { display: flex; align-items: center; gap: 8px; min-height: 44px; }
        .grow { flex: 1; min-width: 0; overflow-wrap: anywhere; }
        .badge { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: .75rem;
                 background: #2a2e38; color: #9aa3b2; white-space: nowrap; }
        .badge.ok { background: #14361f; color: #5fd68a; }
        .badge.warn { background: #3a2c12; color: #f0b45c; }
        .badge.bad { background: #3d1a1a; color: #f47c7c; }
        .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem; }
        .path { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .8rem;
                color: #9aa3b2; overflow-wrap: anywhere; }
        ul.lined { padding: 0; margin: 4px 0 0; }
        ul.lined li { list-style: none; padding: 6px 0; border-bottom: 1px solid #22262f; }
        ul.lined li:last-child { border-bottom: 0; }
        .code-links { display: flex; gap: 18px; }
        .code-links a { display: inline-flex; align-items: center; min-height: 44px; color: #4f8ff7;
                        text-decoration: none; font-weight: 600; }
        a.commit-link { display: block; color: inherit; text-decoration: none; }
        .breadcrumb { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem;
                      margin: 4px 0 8px; overflow-wrap: anywhere; }
        .breadcrumb a { color: #4f8ff7; text-decoration: none; }
        pre.code { background: #12141a; border: 1px solid #2a2e38; border-radius: 8px;
                   padding: 8px 0; margin: 8px 0; overflow-x: auto;
                   font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                   font-size: .8rem; line-height: 1.5; }
        pre.code .line { display: block; white-space: pre; padding-right: 12px; }
        pre.code .line:target { background: #25375c; }
        pre.code .ln { display: inline-block; min-width: 3.4em; padding: 0 10px 0 12px;
                       color: #5b6472; text-align: right; user-select: none; }
        .tok-k { color: #7aa2f7; }
        .tok-c { color: #6a9955; }
        .tok-s { color: #ce9178; }
        .tok-n { color: #b5cea8; }
        .tok-t { color: #4ec9b0; }
        .diff-add, .diff-del, .diff-hunk, .diff-meta, .diff-ctx { display: block; white-space: pre; padding: 0 12px; }
        .diff-add { background: #14361f; color: #5fd68a; }
        .diff-del { background: #3d1a1a; color: #f47c7c; }
        .diff-hunk { color: #7aa2f7; }
        .diff-meta { color: #9aa3b2; }
        .badge.added { background: #14361f; color: #5fd68a; }
        .badge.deleted { background: #3d1a1a; color: #f47c7c; }
        .badge.modified { background: #3a2c12; color: #f0b45c; }
        .badge.renamed { background: #1d2b45; color: #7aa2f7; }
        .search-form input[type="search"] { width: 100%; background: #12141a; color: #e6e8ee;
                                            border: 1px solid #2a2e38; border-radius: 8px;
                                            padding: 10px 12px; font-size: 1rem; margin-bottom: 8px; }
        .search-form label { display: inline-flex; align-items: center; gap: 6px; min-height: 44px;
                             margin-right: 14px; color: #9aa3b2; }
        .search-form button { background: #4f8ff7; color: #0f1115; border: 0; border-radius: 8px;
                              padding: 8px 18px; font-size: .95rem; font-weight: 600; min-height: 40px; }
        a.action-btn, div.action-btn { display: flex; align-items: center; gap: 8px; min-height: 52px;
                              background: #12141a; border: 1px solid #2a2e38; border-radius: 10px;
                              padding: 12px 14px; margin: 8px 0; color: #e6e8ee;
                              text-decoration: none; font-weight: 600; }
        a.action-btn:active, a.action-btn:hover { border-color: #4f8ff7; }
        .action-btn.mutating { border-color: #5c3a2a; background: #17141a; }
        .action-btn.disabled { opacity: .55; }
        .action-reason { margin: -4px 0 8px 4px; }
        pre.output { background: #12141a; border: 1px solid #2a2e38; border-radius: 8px;
                     padding: 10px 12px; margin: 8px 0; overflow-x: auto; max-height: 50vh; overflow-y: auto;
                     font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                     font-size: .8rem; line-height: 1.5; white-space: pre-wrap; overflow-wrap: anywhere; }
        button.action-submit { width: 100%; min-height: 52px; background: #4f8ff7; color: #0f1115;
                               border: 0; border-radius: 10px; font-size: 1rem; font-weight: 700; margin-top: 8px; }
        button.action-submit.danger { background: #f47c7c; }
        .audit-filters label { display: inline-flex; align-items: center; gap: 6px; min-height: 44px;
                               margin-right: 14px; color: #9aa3b2; font-size: .85rem; }
        .audit-filters input, .audit-filters select { background: #12141a; color: #e6e8ee;
                               border: 1px solid #2a2e38; border-radius: 8px; padding: 8px 10px; font-size: .9rem; }
        .audit-filters input[type="number"] { width: 6em; }
        """;

    private const string ClientScript = """
        (function () {
          "use strict";
          var endpoint = document.body.getAttribute("data-endpoint");
          var page = document.body.getAttribute("data-page");
          if (!endpoint) return; // static pages (code browser, 404) never poll
          var timer = null, inflight = false, lastOk = new Date();

          function $(id) { return document.getElementById(id); }
          function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
          function badge(text, cls) { return el("span", "badge" + (cls ? " " + cls : ""), text); }
          function row() { return el("div", "row"); }
          function muted(text) { return el("span", "muted", text); }
          function monoGrow(text) { return el("span", "mono grow", text); }
          function setText(id, text) { var e = $(id); if (e) e.textContent = text; }
          function show(id, visible) { var e = $(id); if (e) e.hidden = !visible; }
          function clear(e) { while (e && e.firstChild) e.removeChild(e.firstChild); }
          function setBar(id, pct) { var e = $(id); if (e) e.style.width = Math.max(0, Math.min(100, pct)) + "%"; }

          function serviceRow(s) {
            var cls = s.stateText === "running" || s.stateText === "paused" ? "ok"
              : s.stateText === "completed" ? ""
              : s.stateText === "failed" || s.stateText === "restarting" ? "bad" : "warn";
            var r = row();
            r.appendChild(monoGrow(s.name));
            r.appendChild(badge(s.stateText || "unknown", cls));
            if (s.healthText) r.appendChild(badge(s.healthText,
              s.healthText === "healthy" ? "ok" : s.healthText === "unhealthy" ? "bad" : "warn"));
            return r;
          }

          function renderSystem(s) {
            setText("sys-cpu-v", s.cpuText); setBar("sys-cpu-b", s.cpuPercent);
            setText("sys-ram-v", s.memoryText); setBar("sys-ram-b", s.memoryPercent);
            setText("sys-disk-v", s.diskText); setBar("sys-disk-b", s.diskPercent);
            setText("sys-uptime-v", s.uptimeText);
          }

          function renderTmux(t) {
            var body = $("tmux-body"); if (!body) return;
            clear(body);
            if (!t.tmuxAvailable) { body.appendChild(muted(t.error || "tmux is not available.")); return; }
            if (!t.serverRunning) { body.appendChild(muted(t.error || "No tmux server running — no sessions.")); return; }
            if (!t.sessions.length) { body.appendChild(muted("tmux server running, no sessions.")); return; }
            t.sessions.forEach(function (s) {
              var r = row();
              r.appendChild(monoGrow(s.name));
              r.appendChild(badge(s.isAttached ? "attached" : "detached", s.isAttached ? "ok" : "warn"));
              r.appendChild(muted(s.windowCount + (s.windowCount === 1 ? " window" : " windows")
                + (s.activeWindowName ? " · " + s.activeWindowName : "")));
              body.appendChild(r);
            });
          }

          function renderSummary(s) {
            var body = $("summary-body"); if (!body) return;
            clear(body);
            function item(label, count, badgeClass) {
              var wrap = el("span", "summary-item");
              wrap.appendChild(el("strong", null, String(count)));
              wrap.appendChild(document.createTextNode(" "));
              if (badgeClass) wrap.appendChild(badge(label, badgeClass));
              else wrap.appendChild(document.createTextNode(label));
              body.appendChild(wrap);
            }
            item("projects", s.totalProjects);
            item("healthy", s.healthyCount, "ok");
            item("attention", s.attentionCount, s.attentionCount > 0 ? "warn" : null);
            item("blocked", s.blockedCount, s.blockedCount > 0 ? "bad" : null);
            item("failing", s.failingCount, s.failingCount > 0 ? "bad" : null);
            item("unavailable", s.unavailableCount, s.unavailableCount > 0 ? "bad" : null);
            item("blockers", s.activeBlockerCount, s.activeBlockerCount > 0 ? "warn" : null);
            item("running", s.runningServices);
            item("failing services", s.failingServices, s.failingServices > 0 ? "bad" : null);
            item("dirty", s.dirtyProjects, s.dirtyProjects > 0 ? "warn" : null);
          }

          function renderProjects(projects) {
            var body = $("projects-body"); if (!body) return;
            clear(body);
            if (!projects.length) { body.appendChild(el("div", "card muted", "No projects registered.")); return; }
            projects.forEach(function (p) {
              // The card is a div so the footer can hold both tap targets.
              var card = el("div", "card project-card");
              var head = row();
              head.appendChild(el("strong", "grow", p.name));
              var health = badge(p.healthText || "Unknown", p.healthBadgeClass || null);
              if (p.healthReasonsText) health.title = p.healthReasonsText;
              head.appendChild(health);
              head.appendChild(badge(p.status));
              card.appendChild(head);

              // Progress: a null projection means "not configured", never 0%.
              var prog = row();
              prog.appendChild(muted("progress"));
              if (p.progressText) {
                var grow = el("div", "grow");
                var bar = el("div", "bar");
                var fill = el("div");
                fill.style.width = Math.max(0, Math.min(100, p.progressPercent)) + "%";
                bar.appendChild(fill);
                grow.appendChild(bar);
                prog.appendChild(grow);
                prog.appendChild(muted(p.progressText));
              } else {
                prog.appendChild(el("span", "muted grow", "Progress not configured"));
              }
              card.appendChild(prog);

              var fr = row();
              fr.appendChild(muted("frontier"));
              fr.appendChild(el("span", p.frontierText ? "mono grow" : "muted grow",
                p.frontierText || "no frontier set"));
              card.appendChild(fr);

              var bl = row();
              bl.appendChild(muted("blockers"));
              var blGrow = el("span", "grow");
              if (p.blockerText) blGrow.appendChild(badge(p.blockerText, "bad"));
              else blGrow.appendChild(muted("no active blockers"));
              bl.appendChild(blGrow);
              card.appendChild(bl);

              var git = row();
              git.appendChild(muted("git"));
              git.appendChild(monoGrow(p.gitText));
              if (p.gitDirty) git.appendChild(badge("dirty", "warn"));
              if (p.gitUnavailable) git.appendChild(badge("repo missing", "bad"));
              card.appendChild(git);

              var res = row();
              res.appendChild(muted("results"));
              if (p.resultsText) res.appendChild(monoGrow(p.resultsText));
              else res.appendChild(el("span", "muted grow", "no build/test history"));
              card.appendChild(res);

              if (p.dockerSummary) {
                var d = row(); d.appendChild(muted("docker")); d.appendChild(monoGrow(p.dockerSummary)); card.appendChild(d);
              }
              if (p.tmuxText) {
                var s = row(); s.appendChild(muted("tmux")); s.appendChild(monoGrow(p.tmuxText)); card.appendChild(s);
              }
              if (p.lastActivityText) {
                var a = row(); a.appendChild(muted("activity"));
                a.appendChild(el("span", "muted grow", "last " + p.lastActivityText)); card.appendChild(a);
              }
              var view = row();
              var viewLink = el("a", "view-link", "View project →");
              viewLink.href = "/projects/" + p.id;
              view.appendChild(viewLink);
              if (p.hasMobileActions) {
                var actionsLink = el("a", "view-link", "Actions →");
                actionsLink.href = "/projects/" + p.id + "#actions";
                view.appendChild(actionsLink);
              }
              card.appendChild(view);
              body.appendChild(card);
            });
          }

          function renderDetail(d) {
            setText("ov-status", d.status);
            setText("ov-path", d.repositoryPath || "no repository path");

            show("git-error", !d.git.available);
            if (!d.git.available) setText("git-error", d.git.error || "repository unavailable");
            show("git-body", d.git.available);
            if (d.git.available) {
              setText("git-branch", d.git.branch || "?");
              show("git-dirty", d.git.dirty);
              var counts = [d.git.dirtyCount + " dirty file" + (d.git.dirtyCount === 1 ? "" : "s")];
              if (d.git.aheadBy) counts.push("↑" + d.git.aheadBy);
              if (d.git.behindBy) counts.push("↓" + d.git.behindBy);
              setText("git-counts", counts.join(" · "));
              setText("git-last", d.git.lastCommitText || "no commits");
              var cl = $("git-commits"); clear(cl);
              d.git.recentCommits.forEach(function (c) {
                var li = el("li");
                var a = el("a", "commit-link");
                a.href = "/projects/" + d.id + "/commits/" + c.shortSha;
                a.appendChild(el("span", "mono", c.shortSha + " " + c.subject + " "));
                a.appendChild(muted(c.when));
                li.appendChild(a);
                cl.appendChild(li);
              });
            }

            show("docker-none", !d.docker.hasComposeFile);
            show("docker-body", d.docker.hasComposeFile);
            if (d.docker.hasComposeFile) {
              show("docker-error", !!d.docker.error);
              if (d.docker.error) setText("docker-error", d.docker.error);
              setText("docker-summary", d.docker.summary || "");
              show("docker-services", d.docker.services.length > 0);
              var ds = $("docker-services"); clear(ds);
              d.docker.services.forEach(function (s) { ds.appendChild(serviceRow(s)); });
            }

            var sess = d.session;
            var stateText = !sess.tmuxAvailable ? "tmux unavailable"
              : !sess.serverRunning ? "no tmux server"
              : sess.exists ? (sess.isAttached ? "attached" : "detached") : "no session";
            setText("session-name", sess.sessionName);
            setText("session-state", stateText +
              (sess.exists && sess.activeWindowName ? " · " + sess.activeWindowName : ""));
            show("session-windows", sess.exists);
            if (sess.exists) setText("session-windows-v", sess.windowCount + (sess.windowCount === 1 ? " window" : " windows")
              + (sess.activeWindowName ? " · " + sess.activeWindowName : ""));

            show("activity-none", d.recentActivity.length === 0);
            var al = $("activity-list"); clear(al);
            d.recentActivity.forEach(function (a) {
              var li = el("li");
              li.appendChild(muted(a.when + " · " + a.category));
              li.appendChild(document.createElement("br"));
              li.appendChild(el("span", null, a.message));
              if (a.detail) { li.appendChild(document.createElement("br")); li.appendChild(el("span", "muted mono", a.detail)); }
              al.appendChild(li);
            });
          }

          function render(data) {
            if (page === "dashboard") { renderSystem(data.system); renderSummary(data.summary); renderTmux(data.tmux); renderProjects(data.projects); }
            else if (page === "detail") { renderDetail(data); }
          }

          function schedule() { if (timer) clearTimeout(timer); timer = setTimeout(poll, 10000); }
          function poll() {
            if (document.hidden || inflight) { schedule(); return; }
            inflight = true;
            fetch(endpoint, { headers: { "Accept": "application/json" }, credentials: "same-origin" })
              .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
              .then(function (data) { render(data); lastOk = new Date(); show("stale", false); })
              .catch(function () { show("stale", true); })
              .then(function () { setText("last-refresh", lastOk.toLocaleTimeString()); inflight = false; schedule(); });
          }
          document.addEventListener("visibilitychange", function () { if (!document.hidden) poll(); });
          schedule();
        })();
        """;
}
