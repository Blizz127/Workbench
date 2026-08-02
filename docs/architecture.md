# Workbench Architecture

## Goals

1. **Maintainability over speed.** Small, single-purpose layers with explicit
   contracts.
2. **Modularity.** Every subsystem the UI consumes is an interface defined in
   `Workbench.Core`. Implementations (SQLite, LibGit2Sharp) live in separate
   assemblies and can be swapped without touching the UI.
3. **AI-readiness without AI-dependence.** Phase 1 ships no AI code, but the
   shapes future providers need — handoff documents, AI context paths,
   preferred provider fields — already exist. Providers become modules plugged
   into Workbench, never the other way around.

## Layering

```
┌────────────────────────────────────────────────────────┐
│ Workbench.App        Avalonia UI, MVVM view models,    │
│                      composition root (AppServices)    │
├──────────────────┬─────────────────────────────────────┤
│ Workbench.Data   │ Workbench.Git                       │
│ SQLite repos,    │ LibGit2Sharp GitService,            │
│ schema, search   │ TodoScanner                         │
├──────────────────┴─────────────────────────────────────┤
│ Workbench.Core       entities, enums, service          │
│                      interfaces, HandoffService,       │
│                      ExecutionService, ProcessRunner,  │
│                      ProjectConfigService,             │
│                      SharedStateService,               │
│                      AppSettingsService,               │
│                      GitSyncService                    │
└────────────────────────────────────────────────────────┘
```

Dependencies point strictly downward. `Workbench.Core` has **zero** package
references; `Workbench.App` is the only assembly that knows concrete
implementations, in `AppServices` (a hand-rolled composition root — no DI
container needed at this size).

## Core concepts

| Concept | Model | Notes |
|---------|-------|-------|
| Project | `Project` | Status (Active/Waiting/Broken/Research/Complete), repo path, build/run/test commands, doc & AI context paths, tags, active task, objective. |
| Milestone | `Milestone` | Ordered checklist; completion timestamps journal to the timeline. |
| Frontier | `Frontier` | The current edge of progress. One `Current`, one `Next`; advancing completes the old current. Completed frontiers are history. |
| Blocker | `Blocker` | Open blockers surface on the overview and in handoffs; resolving keeps them as history with a resolution text. |
| Timeline | `TimelineEntry` | Append-only engineering journal. Categories: Build, Run, Test, Code, Milestone, Frontier, Blocker, Note, Regression, Status, GitSync, General. |
| Build history | `BuildRecord` | Append-only (`ExecutionKind.Build` or `.Run`): command, timestamp, exit code, duration, full output log, HEAD sha. |
| Test history | `TestRecord` | Append-only: command, result (Passed/Failed/Error/TimedOut), runtime, notes, associated commit. |
| Runtime health | `RuntimeHealthRecord` | Append-only; newest row per project is the current state (Unknown/Starting/Healthy/Degraded/Failed/Stopped). Separate from the launch command's exit code. |
| Note | `Note` | Markdown body, searchable. |

## Subsystems

### Persistence (`Workbench.Data`)

- `WorkbenchDatabase` owns the connection string and applies ordered schema
  migrations keyed off `PRAGMA user_version` (WAL mode, foreign keys on).
  History must survive both restarts and upgrades.
- One repository class per aggregate, implementing the `Workbench.Core`
  interfaces with plain parameterized SQL — no ORM, no magic.
- Build/test repositories deliberately have **no Update/Delete**: history is
  never overwritten.
- `SearchService` runs case-insensitive `LIKE` queries (wildcards escaped)
  across projects, notes, milestones, timeline, and frontiers.

### Git (`Workbench.Git`)

- `GitService.GetSnapshot` returns a `RepositorySnapshot` (branch, last commit,
  recent commits, modified files, dirty flag). It never throws — invalid paths
  produce `RepositorySnapshot.Invalid(reason)`. LibGit2Sharp types never leak
  out of the assembly, so the backend can be replaced by a git CLI abstraction.
- `TodoScanner` walks the working tree for `TODO`/`FIXME`/`HACK`/`XXX`
  comments, skipping build/VCS directories, capped by file size and item count.

### GitHub sync (`Workbench.Core.GitSyncService`)

The one place Workbench performs git *writes*, and the only place it talks to
a remote at all. Three explicit, button-driven actions — Fetch, Pull
(fast-forward only), Push — each guarded by a safety check that runs and can
block **before** any git command executes:

- `FetchAsync` runs `git fetch --all --prune` unconditionally (a fetch is
  never destructive) and refreshes the repository snapshot.
- `PullFastForwardAsync` runs `git pull --ff-only`, but only if: the repo has
  an upstream tracking branch, the working tree is clean, and the branch is
  behind without also being ahead (no divergence). A dirty tree, a diverged
  branch, a missing upstream, or nothing-to-pull all return
  `GitSyncStatus.Blocked` with a plain-English reason and never invoke git.
- `PushAsync` runs `git push`, but only if an upstream exists and the branch
  is ahead. A dirty working tree does **not** block a push — only committed
  work is ever pushed — but is surfaced as a non-blocking `Warning` so the
  user isn't surprised by what didn't come along.

Every attempt (blocked, succeeded, or failed) is journaled to the timeline
under the `GitSync` category, capturing the command, branch, HEAD before/after,
ahead/behind before/after, and — on failure — a bounded tail of the output.

Deliberately out of scope, permanently: auto-commit, auto-stash, auto-merge,
auto-rebase, force-push, and creating remotes or branches. Each method maps to
exactly one explicit UI button, and the UI (`GitSyncViewModel`) additionally
gates Pull and Push behind a preview panel — nothing runs until the user hits
*Confirm*.

Commands run through the same `IProcessRunner` used for build/test
commands, invoking the user's own `git` CLI rather than LibGit2Sharp's network
stack — so whatever credential helper, SSH agent, or cached token the user
already has configured is reused as-is, instead of Workbench reimplementing
git authentication.

The GitHub section also serves as the read-only **git console**:

- `GitSyncService.GetSyncTimes(project)` derives last-successful
  fetch/pull/push times from the append-only timeline journal (newest
  `GitSync` entry whose message reports the command succeeded, via
  `ITimelineRepository.GetLatestByCategory`); blocked/failed attempts never
  count. The section shows them under the sync buttons, and dashboard cards
  render a `sync` row (`pull … · push …`, comfortable density only).
- **View Diff** shows `IGitService.GetWorkingTreeDiff` (a LibGit2Sharp patch
  of HEAD vs index + working directory) in an output panel; **Commit History**
  formats the last 50 commits as log lines into the same panel; **Open Git
  Log** spawns a terminal running `git log`. The viewers are read-only and
  share the panel; diffs are not secret-redacted (it's the user's own source,
  and redaction would corrupt the patch).
- Dashboard cards also extend the `git` row with a dirty-file count
  (`branch* ↑2 ↓1 · 3 dirty`).

**Freshness note:** the safety checks read `IGitService.GetSnapshot`, which
reflects local `refs/remotes/origin/*` state — only updated by an actual
fetch. Pull's ahead/behind read is therefore only as current as the last
Fetch (or a repository refresh); this matches the intended flow (Fetch, then
Pull becomes available) and is exercised directly in `GitSyncServiceTests`.

### Execution (`Workbench.Core.ExecutionService`)

Runs the configured build/run/test command via `ProcessRunner` (cmd.exe on
Windows, `/bin/sh` elsewhere; output captured interleaved; configurable
timeout), then:

1. appends the record to the append-only history,
2. journals a timeline entry (with an output-tail detail on failure),
3. journals a **Regression** entry when a previously green build or previously
   passing test suite goes red.

### Runtime health (`Workbench.Core.HealthCheckService`)

The Run card tracks the launch command and the runtime separately: exit 0
from a detached launcher (`podman compose up -d`, `setsid`, backgrounded
scripts) says nothing about service health. After a successful Run start, the
project's launch profile (`Project.Launch`, mirrored in `workbench.json` as
`launchProfile`) drives post-launch validation: wait out a startup grace
period, run the status command (with retries), and parse its output — a
service counts as up when its name shares a line with a healthy token;
required services down ⇒ `Failed` (with the failed component), optional
services down ⇒ `Degraded`, a failed-state pattern anywhere ⇒ `Failed`
immediately, and a failing status command ⇒ `Unknown`, never `Healthy`.
Assessments append to `runtime_health` (newest row = current state, so they
survive restarts) and transitions journal to the timeline under `Status`.
A Start is blocked while the runtime is tracked as Starting/Healthy — the
status command, not the launcher's process lifetime, is what makes detached
runtimes trackable. The Run card also offers Recheck health and a per-service
log action (`{service}` placeholder in the configured log command).

The launch profile additionally carries optional **operations commands** that
turn the Run card into an operations panel:

- `stopCommand` — really tears the runtime down (e.g. `docker compose down`);
  without it, Stop on a detached runtime only stops tracking it. Restart runs
  it before starting again.
- `logsCommand` — recent logs across all services (the Logs button); the
  per-service lights (parsed from the latest assessment's status output with
  the same matching rules) fetch a single service's logs on click.
- `updateCommand` — one-click update (e.g. pull + redeploy), followed by a
  health reassessment. Stop/Update runs journal to the timeline under `Run`.
- `metricsCommand` — emits `Label: value` lines (players, CPU, RAM, ...)
  rendered as tiles; best-effort, refreshed in the background on every reload
  and never gating any action.

The Run card also shows the repository's branch · HEAD commit and offers a
Shell button that opens a terminal at the repository root.

### Docker Compose operations (`Workbench.Core.DockerComposeService`)

The project detail view's **Docker** section offers safe, explicit compose
operations behind `IDockerComposeService`. `ComposeCommandBuilder` is the only
place compose commands are constructed, and it deliberately has no destructive
primitives: no `down`, no `-v`/`--volumes`, no `prune`, no `rm`/`rmi` —
stopping keeps containers, images, and database volumes intact. Every command
names the compose file explicitly (`-f`), so the exact command shown in the
preview panel is the exact command that runs.

- **Inspection** is read-only: `ps --all --format json` (NDJSON) parsed by
  `ComposePsParser` into typed per-service rows (state, health, exit code),
  rolled up per service with the worst state winning. A one-shot service that
  exited 0 (e.g. `db-update`) is `Completed` — a success, not a failure.
- **Actions**: start/stop/restart for the whole project or one service, plus
  recent logs for one service or the whole project. Nothing runs at startup
  beyond the read-only inspection; every mutating action is user-initiated and
  passes through a preview panel showing the exact command. Stop/Restart
  require confirmation, enforced in the service itself
  (`ExecuteAsync(..., confirmed)` refuses otherwise) — the UI gate is only the
  first line. All output is funneled through `SecretRedactor` (password-like
  `KEY=value` / `KEY: value` / JSON pairs become `***`) before reaching the UI
  or the timeline journal.

Which compose file a project uses comes from `Project.ComposeFile` (the
`composeFile` key in `workbench.json`, relative to the repository root or
absolute); when unset, `ComposeFileInspector` auto-detects the conventional
names (`docker-compose.yml`, `docker-compose.yaml`,
`docker-compose.docker.yml`, `compose.yml`, `compose.yaml`). Discovery records
the file it found and synthesizes `-f`-qualified commands for nonstandard
filenames.

### tmux sessions (`Workbench.Core.TmuxService`)

The dashboard's **Development Sessions** panel and the project detail view's
**Sessions** section offer safe, explicit tmux session management behind
`ITmuxService`. `TmuxCommandBuilder` is the only place tmux commands are
constructed, and it deliberately has no fleet-wide primitives: no
`kill-server`, no `kill-session -a`, no free-form shell text — every command
targets exactly one session whose name passed the strict
`[A-Za-z0-9_-]+` validation (tmux itself forbids `.` and `:`), which makes
shell injection impossible by construction.

- **Inspection** is read-only: `list-sessions` with a stable pipe-delimited
  format string, parsed by `TmuxListParser` into typed rows (created time,
  attached/detached, window count, active window). The status distinguishes
  the three honest states — tmux not installed, installed but no server
  running (a normal empty state, not an error), and a running server.
- **Actions**: create (detached, optionally rooted at the project's
  repository path), rename, kill — each scoped to one session. Attach is not a
  mutation: it asks `ITerminalLauncher` to open a graphical terminal running
  `tmux attach-session`. Nothing runs at startup beyond the read-only
  inspection; every mutation previews the exact command, and kill/rename
  require confirmation enforced in the service itself
  (`ExecuteAsync(..., confirmed)` refuses otherwise).
- **Project sessions** come from `Project.TmuxSession` (the `tmuxSession` key
  in `workbench.json`); when unset or invalid, a safe lowercase slug is
  synthesized from the project name (`Banshee Realm 11` → `banshee-realm-11`).

`TerminalLauncher` owns all desktop-terminal knowledge: it detects installed
emulators in preference order (xfce4-terminal, konsole, x-terminal-emulator,
then the rest), honors a `$TERMINAL` override, and is also used by the older
"open a shell here" / "open git log" actions so no view model shells out to a
terminal directly.

### Mobile web dashboard (`Workbench.Web`)

A small ASP.NET Core host serves the Development Lab to a phone over
Tailscale. It reuses the same typed Core/Data/Git services as the desktop
app — system health, project repository, git snapshots, compose inspection,
tmux inspection, timeline journal — and adds no process execution or command
parsing of its own: `DashboardSnapshotService` composes typed snapshots,
`DashboardPageRenderer` / `ProjectDetailPageRenderer` turn them into
self-contained HTML pages (phone-first, dark, branded "Development Lab").

- **GET is read-only; mutations are POST-only**: all pages and their JSON
  twins are GET — `/` (dashboard), `/projects/{id:long}` (project detail),
  `/api/snapshot`, `/api/projects/{id}`, and the Phase 2C code browser:
  `/projects/{id}/files` (directory listing), `/projects/{id}/file`
  (syntax-highlighted, line-numbered view; `?sha=` serves a historical
  version from the commit), `/projects/{id}/search` (filename or text),
  `/projects/{id}/changes` (working-tree diff), and
  `/projects/{id}/commits/{sha}` (commit detail with its changed files).
  The only non-GET routes are the Phase 2D action endpoints below; a GET on
  them (or any other verb anywhere) gets 405. Project routes take the
  database id through a long constraint — never a filesystem path — and
  unknown ids get a styled 404 page.
- **Code browsing confinement**: `ProjectCodeService` composes
  `IProjectFileService`, which resolves every requested path against the
  project's repository root and refuses anything that escapes it — rooted
  paths, `..` segments, backslash variants, and symlinks whose target leaves
  the root all return a "forbidden" error code, never content. Binary files
  (NUL sniff) and files over 512 KB are blocked from view (search reads up
  to 1 MB), `.git`/`bin`/`obj`/`node_modules` are skipped, search caps at
  200 hits (3 per file) with a truncated flag, and commit ids must match
  `^[0-9a-fA-F]{7,40}$` before git is touched. Content problems ride in the
  snapshot's `Error` field and render as an error card — they never throw.
  The code pages are static (no polling endpoint).
- **Pages**: the dashboard shows a fleet summary bar (health distribution,
  active blockers, running/failing services, dirty projects — the shared
  `BuildSummary` totals), system metrics, tmux sessions, and compact project
  cards built from the shared projection: health badge with the state as
  TEXT (reasons in the tooltip), weighted milestone progress ("Progress not
  configured" when null), frontier and blocker summaries, last build/test/
  launch verdicts, and last activity. Cards link to the detail page ("View
  project"); an "Actions" link appears only where the project's mobile
  allowlist has entries. The detail
  page has Overview / Git / Docker / Sessions / Recent Activity sections with
  explicit service rows (state AND health as text badges; a one-shot service
  that exited 0 reads "completed", never failed), plus a static, read-only
  **Blockers** summary (counts by lifecycle status and the open records with
  evidence/next safest step, redacted and HTML-escaped, outside every
  JS-polled container; there are no blocker mutation endpoints). Projects whose repository
  path no longer exists are marked "repo missing", never silently dropped or
  deleted (cleanup is a deliberate action in the desktop app).
- **Live refresh**: each page's embedded script polls its JSON endpoint every
  10s and updates the DOM in place — no full reloads, scroll position
  survives. Polling pauses while the tab is hidden, never overlaps requests,
  shows the last successful refresh time, and marks data "stale" on errors
  without blanking it. Server-side TTL caches cap the actual subsystem
  refresh rates regardless of how many browsers poll: health 10s, Docker and
  tmux 15s, git 30s — so process-spawning and repository-walking load stays
  bounded.
- **Authentication**: `X-Workbench-Token` header (API clients, tests) or the
  `?token=` phone bookmark. A valid query token on a GET page load is
  exchanged once for a random in-memory session id (`SessionStore`) in an
  HttpOnly, SameSite=Strict cookie (Secure only under HTTPS, configurable
  lifetime via `SessionLifetimeMinutes`), then redirected to a clean URL — the
  token does not linger in browser history or screenshots. An empty configured
  token answers 503 on every route. All comparisons are constant-time.
- **Configuration** comes from the `Web` section of `appsettings.json`
  (committed, safe defaults, no secrets) overridden by
  `WORKBENCH_WEB_BindAddress` / `WORKBENCH_WEB_Port` /
  `WORKBENCH_WEB_AuthToken` / `WORKBENCH_WEB_DatabasePath` environment
  variables. The default bind is `127.0.0.1:8471`; binding a public address
  logs a loud warning (loopback, RFC 1918, link-local, and the Tailscale
  CGNAT range 100.64.0.0/10 count as private). No firewall port is opened.
- **Redaction**: journal messages/details pass through `SecretRedactor`
  before leaving the snapshot service; compose errors arrive already
  redacted. No environment values are ever rendered.
- **Mobile actions (Phase 2D)**: the dashboard's only mutations, behind a
  strict per-project allowlist. The effective allowlist is two internal
  read-only actions (`refreshStatus` — invalidates the snapshot TTL caches;
  `tmuxAttachInfo` — shows the exact `tmux attach-session` command, running
  nothing) plus the `mobileActions` array of the project's `workbench.json`.
  Anything not listed cannot run, and unknown type strings fail closed (the
  action renders disabled with a reason). Supported types: `dockerLogsAll` /
  `dockerLogsService` / `dockerRestartService` / `dockerRestartAllowed`
  (restarts each service that has its own `dockerRestartService` entry,
  sequentially), `tmuxCreateSession` (a clean no-op when the session already
  exists), and `predefinedCommand` (`build` / `test` / `launch` mapping to
  the project's `buildCommand` / `testCommand` / `runCommand` — unconfigured
  ids render as blocked "not configured"). Every command comes from the
  existing typed builders (`ComposeCommandBuilder`, `TmuxCommandBuilder`)
  and services (`IDockerComposeService`, `ITmuxService`,
  `IPredefinedCommandRunner` wrapping `ExecutionService`) — the web layer
  constructs no command text and spawns no process of its own, and all
  output is secret-redacted before rendering. `ProjectConfigService.Save`
  carries `mobileActions` over from the on-disk file, so a desktop settings
  save never wipes the allowlist; the Project database model deliberately
  knows nothing about it.
- **Action safety model**: every action has a safety level — `readOnly`,
  `requiresConfirmation`, or `requiresDoubleConfirmation`. Defaults follow
  the type (log viewers read-only; restarts and `launch` double; tmux
  session creation, `build` and `test` single), and a config entry may only
  RAISE its level, never lower it. A hard guard refuses to restart
  database-like services (`database`, `db`, `mysql`, `mariadb`, `postgres`,
  `postgresql`, `mongo`, `mongodb`, `redis` — exact or prefix match,
  case-insensitive) unless the entry sets `allowDatabase: true`. Execution
  is serialized per project: a second concurrent action gets an "already
  running" blocked result, never a queue. Every attempt — attempted,
  blocked, rejected, succeeded, failed — lands in the bounded (last 200)
  in-memory `ActionJournal`, rendered as "Recent Actions" on the detail
  page.
- **Action flow (CSRF + one-time nonces)**: three routes, all behind the
  same auth — `GET /projects/{id}/actions/{actionId}/confirm` renders the
  exact command preview and issues a nonce; double-confirmation actions
  then `POST .../confirm2` (validates the CSRF token and step-one nonce,
  issues a fresh step-two nonce on the "Are you sure?" page); the final
  `POST .../execute` validates the session's CSRF token (403 before anything
  else runs), consumes the nonce, and only then executes. Nonces
  (`ConfirmationNonceService`) are random, bound to (project, action, step),
  expire after 2 minutes, and are consumed on use — replay fails, and the
  step-one nonce cannot execute by purpose binding. The CSRF token
  (`SessionStore`) is a per-session random value stamped into HttpContext
  items by `TokenAuthMiddleware` and embedded as a hidden `csrf` form field;
  header-token requests share one stable keyed-hash session so API clients
  get the same protection.
- The database is opened lazily (WAL mode already allows the desktop app and
  the web host to read concurrently), and the web host never runs discovery
  scans or migrations beyond the existing idempotent ones. Deployment as a
  systemd user service is documented in
  `docs/mobile-dashboard-deployment.md`.

### Dashboard projection (`Workbench.Core.DashboardProjectionBuilder`)

One builder computes everything a dashboard card needs — for BOTH the
desktop app and the mobile web dashboard. It is constructed from the plain
repository interfaces plus `IGitService` (no Avalonia, no ASP.NET, no SQL of
its own), so `AppServices` and the web host's DI each hold one instance and
no health/progress logic is duplicated in either UI.

- **Progress** (`ProgressCalculator`): milestone percent is weighted
  completion over explicit `Milestone.Status` + `Weight`; `Dismissed`
  milestones are excluded from counts and weights; a project with no
  non-dismissed milestones gets a NULL `MilestoneProgress` — "Progress not
  configured", never an implied 0%. Frontier progress is completed/total
  plus the current frontier title (Current wins over the first Next).
- **Health** (`ProjectHealthCalculator`): the verdict is the FIRST matching
  rule in a fixed precedence — Unavailable (repo missing) > Failing (runtime
  / last build / last test failed) > Blocked (active critical/high blocker)
  > Attention (lower active blocker / dirty / ahead / behind) > Healthy
  (explicit healthy evidence) > Unknown. `Reasons` lists every rule that
  fired so both UIs can explain the badge (tooltip on desktop, `title` on
  the web). Milestone progress never feeds into health.
- **Projection cards** also carry branch/dirty/ahead/behind, next safest
  step (top active blocker's, else the project's), blocker counts + highest
  active severity, last build/test/launch verdicts (`DashboardExecutionResult`
  — kind, verdict, when; NEVER output text or commands), runtime health,
  last activity, and the primary action (RunOrStart when a launch profile
  exists, else OpenProject). `BuildSummary` totals the fleet (health
  distribution, active blockers, running/failing services, dirty projects)
  and `BuildRecentActivity` merges the timeline journal with the mobile
  action audit, newest first, everything secret-redacted.
- **Filter/sort** (`DashboardFilters`): the desktop dashboard's chip and
  sort-menu semantics live here too — pure functions over the projection, so
  "Needs attention first" orders by exactly the health precedence above.
- **Web caching**: the web `DashboardSnapshotService` feeds the builder its
  TTL-cached git snapshots via `BuildCard(project, snapshot)`, so phone
  polling never doubles the repository walks.

### Handoff (`Workbench.Core.HandoffService`)

A pure function `HandoffContext → markdown`. The context (project, frontiers,
blockers, milestones, recent timeline, last build/test, repo snapshot) is
assembled by the caller (`AppServices.BuildHandoffContext`). The recommendation
is rule-based, in priority order: fix a failing build → clear a blocker →
investigate failing tests → resume the active task → push the current frontier
→ start the next frontier → next milestone. Being pure makes it trivially
testable, and later phases reuse the same document as AI prompt seed material.

### Configuration (`Workbench.Core.ProjectConfigService`)

Each project can carry a `workbench.json` at its repository root (name,
description, commands, documentation paths, AI context paths, preferred AI
provider, tags). Registering a repository that contains one imports it
automatically; the Settings tab can export/import at any time.

### Project discovery (`Workbench.Core.ProjectDiscoveryService`)

Projects are found on disk instead of seeded or entered by hand. At startup
(and on the dashboard's **Rescan** button) the discovery folder — the
configured `DiscoveryFolder` setting, default `~/dev` — is scanned two levels
deep; any directory with a `.git` entry, a `workbench.json`, or a compose
file is a candidate, deduped against existing `RepositoryPath` values. A
`workbench.json` is adopted via `ProjectConfigService.ApplyTo`; other
candidates get synthesized defaults (`dotnet build`/`dotnet test` for
sln/csproj directories; `docker compose up -d` plus a launch profile — status,
stop, logs commands and `RequiredServices` scraped from the compose file by
`ComposeFileInspector` — for compose directories). The scan is fully guarded:
a missing folder, a disabled setting, or one unreadable directory never
breaks startup. The sample seeders (`Workbench.Data.SampleData`) no longer
run at startup; `samples/` is inert demo data referenced only by tests.

Dashboard cards also probe docker state in the background enrichment pass:
for projects with a compose file (or a configured launch status command) a
short `docker compose ps` counts running services and renders a `docker` row
(`5/5 containers up`, `2/5 up`, `stopped`) under the git row, comfortable
density only.

### Shared Workbench State (`Workbench.Core.SharedStateService`)

Cross-machine project state without syncing SQLite. The user selects a
**Shared State Folder** (App Settings) — their Dropbox folder (default
`~/Dropbox/workbench`), which Dropbox syncs between machines externally;
Workbench never runs sync commands. Each project exports to
`projects/<slug>.workbench.json`: identity (a stable `id` that dedupes repeated
imports), configuration, progress shape (objective, active task, frontier
checklist, milestones), **complete blocker records** (schema version 2: stable
id, title, class, severity, lifecycle status, evidence, next safest step,
owner, tags, source reference, timestamps — all statuses, resolved/dismissed
included; matched across machines by stable id, never by title), the legacy
scalar blocker fields (class/evidence/next safest step, derived from the top
open blocker so schema-1 readers stay compatible), AI fields (assigned model,
prompt rules, context paths), and **machine profiles** — the per-PC repository
path plus availability. The local resolution text, command history, logs,
timeline, build/test records, audit records, tokens and session data never
leave the local SQLite database; the file's top-level shape is a whitelist
enforced by test.

Sync is explicit and manual — no background watchers, no cloud APIs:

- **Import previews first.** `PreviewImport` returns field-level diffs —
  including blocker additions, removals (shown BEFORE any local deletion can
  apply), and per-blocker field diffs matched by stable id — plus conflict
  flags; nothing applies until the user confirms. A missing machine profile
  imports the project as "not available on this machine", never an error.
- **Export warns** when the shared file is newer than this machine's last-sync
  stamp (someone else exported); overwriting requires explicit confirmation.
  The write itself is **atomic**: serialize to a temp file in the same
  directory, then overwrite-move, so a failure leaves the previous valid file
  untouched; abandoned `*.workbench.json.*.tmp` files (and only those) are
  cleaned up by the folder check.
- **Schema versions** guard against silent data loss: files without a
  `schemaVersion` (or at 1–2) load fine; anything newer is rejected with an
  explicit "unsupported schema version" message in the folder check and Import
  All — newer data is never silently discarded.
- **Conflict detection** compares the shared file's `updatedAt` against the
  locally stored sync stamp, and local `updated_at` against the last sync —
  sync bookkeeping is written via `UpdateSharedSync`, which does not bump
  `updated_at`, so syncing never masquerades as a local edit. Per blocker, a
  record changed on BOTH sides since the last sync is surfaced as a conflict
  and skipped (kept local) by `ApplyImport` unless the user explicitly confirms
  the overwrite.
- **Blocker upserts are idempotent** — repeated imports of the same file
  produce zero changes; local blockers absent from a schema ≥ 2 file are
  deleted only after appearing in the preview, and schema-1 files never touch
  local blockers at all.

`AppSettingsService` persists the folder path and machine name in
`settings.json` beside the database. The dashboard surfaces availability and
drift ("shared is newer" / "not exported" / "conflict with shared") per card;
reads happen on the existing background enrichment pass, writes only on user
action.

### UI (`Workbench.App`)

- MVVM with CommunityToolkit.Mvvm source generators; compiled bindings
  (`x:DataType`) throughout, so binding paths are checked at build time.
- `MainWindowViewModel` hosts the current page (dashboard ⇄ project detail)
  and global search. `ViewLocator` maps view models to views by convention.
- The dashboard renders the shared `DashboardProjectionBuilder` projection:
  fleet summary bar, filter chips + sort menu (`DashboardFilters` semantics),
  full projection cards, and the merged recent-activity stream. The
  projection is built off the UI thread (it walks git repositories) and
  applied in one posted pass, so the grid stays responsive with dozens of
  projects. The card's primary action opens the workspace — on the Run
  section when a launch profile exists; execution stays explicit there.
- The Progress page groups milestones by status (Active / Blocked / Not
  Started / Done; Dismissed collapsed), shows the frontier checklist, and
  keeps resolved blockers collapsed behind a toggle.
- The detail page is a `TabControl`; each tab (Overview, Timeline, Milestones,
  Notes, Builds & Tests, Handoff, Settings) has its own view model.
- Item view models (cards, search hits, blockers, milestones) carry their own
  commands so XAML never needs ancestor bindings.

## Phase boundaries

Workbench is a standalone tool — not part of Xenogears, TEN, or any project it
tracks. Out of scope for Phase 1, by design: AI chat, LLM providers, prompt
generation, automatic coding, patch generation, agent orchestration. Workbench
records and reports; it never modifies the code of a tracked project.

The seams later phases will use already exist: `Project.PreferredAiProvider`
(unused reserved field), `Project.AiContextPaths` (declared context inputs,
read by nothing yet), and `IHandoffService` (the generated handoff document).
The full five-phase roadmap — import/execution hardening, handoff/prompt
export, optional provider modules, multi-project cockpit — lives in the
README.
