using System.Net;
using System.Text;

namespace Workbench.Web;

/// <summary>
/// Shared page furniture for both web pages: the HTML head, the phone-first
/// stylesheet (dark + light via custom properties), and the client-side
/// live-refresh script. The script polls the page's JSON endpoint every 10s
/// (server-side TTL caches cap the actual subsystem refresh rates at
/// 10/15/30s), pauses while the tab is hidden, never overlaps requests,
/// updates the DOM in place (scroll position is preserved), and marks the
/// page stale on errors without blanking data. The dashboard's filter
/// control is static markup the script only reads, so the chosen filter
/// survives every re-render.
/// </summary>
internal static class WebPageShell
{
    public static void WriteHead(StringBuilder html, string title, string pageKind, string endpoint)
    {
        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n")
            .Append("<meta name=\"color-scheme\" content=\"dark light\">\n<title>")
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
        :root {
          color-scheme: dark light;
          --bg: #0f1115; --surface: #1a1d24; --surface-2: #12141a; --border: #2a2e38;
          --text: #e6e8ee; --muted: #9aa3b2; --accent: #4f8ff7;
          --ok-fg: #5fd68a; --ok-bg: #14361f; --ok-border: #275c39;
          --warn-fg: #f0b45c; --warn-bg: #3a2c12; --warn-border: #6b5222;
          --bad-fg: #f47c7c; --bad-bg: #3d1a1a; --bad-border: #6b2f2f;
          --info-fg: #7aa2f7; --info-bg: #1d2b45; --info-border: #32496f;
        }
        @media (prefers-color-scheme: light) {
          :root {
            --bg: #f4f5f7; --surface: #ffffff; --surface-2: #eceef2; --border: #d3d8e0;
            --text: #1d222b; --muted: #5a6372; --accent: #1c5cd6;
            --ok-fg: #166534; --ok-bg: #ddf3e4; --ok-border: #a4d8b6;
            --warn-fg: #8a5a00; --warn-bg: #fbeed2; --warn-border: #e4c88d;
            --bad-fg: #a12727; --bad-bg: #fbdede; --bad-border: #e8a4a4;
            --info-fg: #1c5cd6; --info-bg: #dfe9fb; --info-border: #adc6f0;
          }
        }
        * { box-sizing: border-box; margin: 0; }
        [hidden] { display: none !important; }
        body { background: var(--bg); color: var(--text); font-family: system-ui, -apple-system, sans-serif;
               font-size: 16px; line-height: 1.45; padding: 12px; max-width: 640px; margin: 0 auto; }
        h1 { font-size: 1.4rem; font-weight: 700; margin: 8px 0 2px; }
        h2 { font-size: 1.0rem; color: var(--muted); margin: 20px 0 8px; text-transform: uppercase;
             letter-spacing: .05em; font-weight: 600; }
        /* Anchor targets (tab nav) must clear the sticky project header. */
        h2[id] { scroll-margin-top: 110px; }
        a { color: var(--accent); }
        a:focus-visible, button:focus-visible, summary:focus-visible,
        input:focus-visible, select:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
        .muted { color: var(--muted); font-size: .85rem; }
        .card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px;
                padding: 14px 16px; margin: 12px 0; }
        .project-card { min-height: 44px; }
        .project-card:hover { border-color: var(--accent); }
        .card-head strong { font-size: 1.05rem; font-weight: 650; }
        .project-header { position: sticky; top: 0; z-index: 10; background: var(--bg);
                          border-bottom: 1px solid var(--border); padding: 2px 0 8px; }
        .project-header h1 { font-size: 1.1rem; font-weight: 700; margin: 0; }
        .project-header .back-link { min-height: 36px; font-size: .9rem; }
        .tabs { display: flex; flex-wrap: wrap; gap: 6px; margin: 10px 0 2px; }
        .tabs a { display: inline-flex; align-items: center; min-height: 44px; padding: 0 14px;
                  background: var(--surface); border: 1px solid var(--border); border-radius: 999px;
                  color: var(--text); text-decoration: none; font-weight: 600; font-size: .9rem; }
        .tabs a:hover { border-color: var(--accent); color: var(--accent); }
        details.filters { background: var(--surface); border: 1px solid var(--border);
                          border-radius: 10px; margin: 4px 0 12px; }
        details.filters > summary { display: flex; align-items: center; min-height: 44px;
                                    padding: 0 14px; font-weight: 600; cursor: pointer; list-style: none; }
        details.filters > summary::-webkit-details-marker { display: none; }
        details.filters > summary::after { content: "▾"; margin-left: auto; color: var(--muted); }
        details.filters[open] > summary::after { content: "▴"; }
        .filter-buttons { display: flex; flex-wrap: wrap; gap: 6px; padding: 0 14px 12px; }
        button.filter { min-height: 44px; padding: 0 14px; border-radius: 999px; font: inherit;
                        font-size: .9rem; font-weight: 600; color: var(--text);
                        background: var(--surface-2); border: 1px solid var(--border); cursor: pointer; }
        button.filter.active { border-color: var(--accent); color: var(--accent); }
        details.resolved { margin-top: 6px; border-top: 1px solid var(--border); }
        details.resolved > summary { display: flex; align-items: center; min-height: 44px;
                                     cursor: pointer; color: var(--muted); font-size: .85rem; list-style: none; }
        details.resolved > summary::-webkit-details-marker { display: none; }
        details.resolved > summary::before { content: "▸"; margin-right: 6px; }
        details.resolved[open] > summary::before { content: "▾"; }
        .summary { display: flex; flex-wrap: wrap; gap: 4px 14px; margin: 2px 0 6px;
                   font-size: .85rem; color: var(--muted); }
        .summary-item strong { color: var(--text); font-variant-numeric: tabular-nums; }
        .view-link { display: inline-flex; align-items: center; min-height: 44px; color: var(--accent);
                     font-weight: 600; font-size: .95rem; }
        .back-link { display: inline-flex; align-items: center; min-height: 44px; color: var(--accent);
                     text-decoration: none; font-weight: 600; }
        .metrics { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
        .metric { background: var(--surface-2); border-radius: 8px; padding: 10px 12px; }
        .metric .value { font-size: 1.1rem; font-weight: 600; font-variant-numeric: tabular-nums; }
        .bar { height: 6px; background: var(--border); border-radius: 3px; margin-top: 6px; overflow: hidden; }
        .bar > div { height: 100%; background: var(--accent); border-radius: 3px; }
        .row { display: flex; align-items: center; gap: 8px; min-height: 44px; }
        .grow { flex: 1; min-width: 0; overflow-wrap: anywhere; }
        .badge { display: inline-block; padding: 2px 10px; border: 1px solid var(--border);
                 border-radius: 999px; font-size: .75rem; line-height: 1.5;
                 background: var(--surface-2); color: var(--muted); white-space: nowrap; }
        .badge.ok { background: var(--ok-bg); color: var(--ok-fg); border-color: var(--ok-border); }
        .badge.warn { background: var(--warn-bg); color: var(--warn-fg); border-color: var(--warn-border); }
        .badge.bad { background: var(--bad-bg); color: var(--bad-fg); border-color: var(--bad-border); }
        .badge.added { background: var(--ok-bg); color: var(--ok-fg); border-color: var(--ok-border); }
        .badge.deleted { background: var(--bad-bg); color: var(--bad-fg); border-color: var(--bad-border); }
        .badge.modified { background: var(--warn-bg); color: var(--warn-fg); border-color: var(--warn-border); }
        .badge.renamed { background: var(--info-bg); color: var(--info-fg); border-color: var(--info-border); }
        .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem; }
        .path { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .8rem;
                color: var(--muted); overflow-wrap: anywhere; }
        ul.lined { padding: 0; margin: 4px 0 0; }
        ul.lined li { list-style: none; padding: 6px 0; border-bottom: 1px solid var(--border); }
        ul.lined li:last-child { border-bottom: 0; }
        .code-links { display: flex; gap: 18px; }
        .code-links a { display: inline-flex; align-items: center; min-height: 44px; color: var(--accent);
                        text-decoration: none; font-weight: 600; }
        a.commit-link { display: block; color: inherit; text-decoration: none; }
        .breadcrumb { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem;
                      margin: 4px 0 8px; overflow-wrap: anywhere; }
        .breadcrumb a { color: var(--accent); text-decoration: none; }
        pre.code { background: var(--surface-2); border: 1px solid var(--border); border-radius: 8px;
                   padding: 8px 0; margin: 8px 0; overflow-x: auto;
                   font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                   font-size: .8rem; line-height: 1.5; }
        pre.code .line { display: block; white-space: pre; padding-right: 12px; }
        pre.code .line:target { background: var(--info-bg); }
        pre.code .ln { display: inline-block; min-width: 3.4em; padding: 0 10px 0 12px;
                       color: var(--muted); text-align: right; user-select: none; }
        .tok-k { color: #7aa2f7; }
        .tok-c { color: #6a9955; }
        .tok-s { color: #ce9178; }
        .tok-n { color: #b5cea8; }
        .tok-t { color: #4ec9b0; }
        .diff-add, .diff-del, .diff-hunk, .diff-meta, .diff-ctx { display: block; white-space: pre; padding: 0 12px; }
        .diff-add { background: var(--ok-bg); color: var(--ok-fg); }
        .diff-del { background: var(--bad-bg); color: var(--bad-fg); }
        .diff-hunk { color: var(--info-fg); }
        .diff-meta { color: var(--muted); }
        .search-form input[type="search"] { width: 100%; background: var(--surface-2); color: var(--text);
                                            border: 1px solid var(--border); border-radius: 8px;
                                            padding: 10px 12px; font-size: 1rem; margin-bottom: 8px; }
        .search-form label { display: inline-flex; align-items: center; gap: 6px; min-height: 44px;
                             margin-right: 14px; color: var(--muted); }
        .search-form button { background: var(--accent); color: var(--bg); border: 0; border-radius: 8px;
                              padding: 8px 18px; font-size: .95rem; font-weight: 600; min-height: 40px; }
        a.action-btn, div.action-btn { display: flex; align-items: center; gap: 8px; min-height: 52px;
                              background: var(--surface-2); border: 1px solid var(--border); border-radius: 10px;
                              padding: 12px 14px; margin: 8px 0; color: var(--text);
                              text-decoration: none; font-weight: 600; }
        a.action-btn:active, a.action-btn:hover { border-color: var(--accent); }
        .action-btn.mutating { border-color: var(--warn-border); background: var(--surface); }
        .action-btn.disabled { opacity: .55; }
        .action-reason { margin: -4px 0 8px 4px; }
        pre.output { background: var(--surface-2); border: 1px solid var(--border); border-radius: 8px;
                     padding: 10px 12px; margin: 8px 0; overflow-x: auto; max-height: 50vh; overflow-y: auto;
                     font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                     font-size: .8rem; line-height: 1.5; white-space: pre-wrap; overflow-wrap: anywhere; }
        button.action-submit { width: 100%; min-height: 52px; background: var(--accent); color: var(--bg);
                               border: 0; border-radius: 10px; font-size: 1rem; font-weight: 700; margin-top: 8px; }
        button.action-submit.danger { background: var(--bad-fg); color: var(--bg); }
        .audit-filters label { display: inline-flex; align-items: center; gap: 6px; min-height: 44px;
                               margin-right: 14px; color: var(--muted); font-size: .85rem; }
        .audit-filters input, .audit-filters select { background: var(--surface-2); color: var(--text);
                               border: 1px solid var(--border); border-radius: 8px; padding: 8px 10px; font-size: .9rem; }
        .audit-filters input[type="number"] { width: 6em; }
        @media (max-width: 480px) {
          .search-form .row { flex-direction: column; align-items: stretch; }
          .search-form label { margin-right: 0; }
          .search-form button { width: 100%; }
        }
        @media (min-width: 768px) {
          body { max-width: 920px; padding: 16px; }
        }
        @media (min-width: 1200px) {
          body { max-width: 1100px; padding: 24px; }
          .metrics { grid-template-columns: repeat(4, 1fr); }
        }
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

          // Project filters: the <details class="filters"> block is static
          // markup outside #projects-body, so polling never wipes it; the
          // current filter is re-applied after every re-render.
          var currentFilter = "all";
          function filterMatches(card, f) {
            if (f === "all") return true;
            if (f === "dirty") return card.getAttribute("data-dirty") === "true";
            var h = card.getAttribute("data-health") || "";
            if (f === "blocked") return h === "Blocked";
            if (f === "attention") return h !== "Healthy" && h !== "Unknown";
            return true;
          }
          function applyFilter() {
            var cards = document.querySelectorAll(".project-card");
            for (var i = 0; i < cards.length; i++) cards[i].hidden = !filterMatches(cards[i], currentFilter);
            var buttons = document.querySelectorAll("button.filter");
            for (var j = 0; j < buttons.length; j++) {
              var active = buttons[j].getAttribute("data-filter") === currentFilter;
              buttons[j].className = "filter" + (active ? " active" : "");
              buttons[j].setAttribute("aria-pressed", active ? "true" : "false");
            }
          }
          var filterButtons = document.querySelectorAll("button.filter");
          for (var k = 0; k < filterButtons.length; k++) {
            filterButtons[k].addEventListener("click", function () {
              currentFilter = this.getAttribute("data-filter") || "all";
              applyFilter();
            });
          }

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
            if (!projects.length) { body.appendChild(el("div", "card muted", "No projects registered.")); applyFilter(); return; }
            projects.forEach(function (p) {
              // The card is a div so the footer can hold both tap targets.
              var card = el("div", "card project-card");
              card.setAttribute("data-health", p.healthText || "Unknown");
              card.setAttribute("data-dirty", p.gitDirty ? "true" : "false");
              var head = row();
              head.className = "row card-head";
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
            applyFilter(); // the chosen filter survives the re-render
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
              var failing = d.docker.services.filter(function (s) {
                return s.stateText === "failed" || s.stateText === "restarting" || s.healthText === "unhealthy";
              });
              show("docker-failing", failing.length > 0);
              if (failing.length) {
                setText("docker-failing-n", failing.length + " failing");
                setText("docker-failing-v", failing.map(function (s) { return s.name; }).join(", "));
              }
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
