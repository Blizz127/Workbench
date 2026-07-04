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
│                      ExecutionService, CommandRunner-  │
│                      Service, ProcessRunner,           │
│                      ProjectConfigService,             │
│                      LogPatternAnalyzer,               │
│                      HandoffCheckpointWriter,          │
│                      PromptGeneratorService,           │
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
| Project | `Project` | Status (Active/Waiting/Broken/Research/Complete), repo path, build/run/test/matching-build commands, doc & AI context paths, prompt rules, custom log patterns, tags, active task, objective, last-known git state (see §Refresh below). |
| Milestone | `Milestone` | Ordered checklist; completion timestamps journal to the timeline. |
| Frontier | `Frontier` | The current edge of progress: title, exact symbol, evidence, blocker class, next safest step, assigned model (plain metadata, no API call), status. One `Current`, one `Next`; advancing completes the old current. Completed frontiers are history. |
| Blocker | `Blocker` | Open blockers surface on the overview and in handoffs; resolving keeps them as history with a resolution text. |
| Timeline | `TimelineEntry` | Append-only engineering journal. Categories: Build, Run, Test, Code, Milestone, Frontier, Blocker, Note, Regression, Status, GitSync, General. |
| Build history | `BuildRecord` | Append-only (`ExecutionKind.Build` or `.Run`): command, timestamp, exit code, duration, full output log, HEAD sha. |
| Test history | `TestRecord` | Append-only: command, result (Passed/Failed/Error/TimedOut), runtime, notes, associated commit. |
| Command | `ProjectCommand` | A user-named command beyond the fixed build/run/test/matching-build slots. |
| Command run | `CommandRun` | Append-only history for matching-build/custom command executions: name, command text, working directory, exit code, duration, output log, HEAD sha, dirty flag, regression flag, cancelled flag. |
| Log pattern | `LogPatternDefinition` / `LogPatternMatch` | A named failure signature (regex or literal) and a matched hit (line number, category, text). Built-in defaults cover generic toolchain/crash patterns; projects add their own via `CustomLogPatterns`. |
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

- `GitService.GetSnapshot` returns a `RepositorySnapshot`: branch, last commit,
  recent commits, modified files, dirty flag, commits ahead/behind the
  upstream tracking branch (null if there is none), a `git diff --stat`-style
  summary, and `git diff --check`-style warnings (trailing whitespace,
  unresolved conflict markers on added lines — computed in C# from the
  LibGit2Sharp patch, not by shelling out to a `git` binary). It never throws
  — invalid paths produce `RepositorySnapshot.Invalid(reason)`. LibGit2Sharp
  types never leak out of the assembly, so the backend can be replaced by a
  git CLI abstraction.
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

Commands run through the same `IProcessRunner` used for build/test/custom
commands, invoking the user's own `git` CLI rather than LibGit2Sharp's network
stack — so whatever credential helper, SSH agent, or cached token the user
already has configured is reused as-is, instead of Workbench reimplementing
git authentication.

**Freshness note:** the safety checks read `IGitService.GetSnapshot`, which
reflects local `refs/remotes/origin/*` state — only updated by an actual
fetch. Pull's ahead/behind read is therefore only as current as the last
Fetch (or a Refresh Project); this matches the intended flow (Fetch, then
Pull becomes available) and is exercised directly in `GitSyncServiceTests`.

### Refresh Project (`OverviewViewModel.RefreshProject`)

Reads a full `RepositorySnapshot`, then compares it to the project's
`LastKnown*` fields (populated **only** by this action, not by the dashboard's
passive per-card git lookup). Differences in branch, HEAD, dirty status, or
ahead/behind are journaled as `Status` timeline entries; the first-ever
refresh journals one baseline entry instead of a diff. Diff-stat and
diff-check warnings are displayed every refresh but not journaled — they
supplement the headline state. This is a read-only action: it updates
Workbench's own records, never the repository.

### Execution (`Workbench.Core.ExecutionService`)

Runs the configured build/run/test command via `ProcessRunner` (cmd.exe on
Windows, `/bin/sh` elsewhere; output captured interleaved; configurable
timeout; cancellation kills the process tree). Output capture is **bounded**:
the first and last ~200 KB are kept with a marker line noting how many lines
were dropped in between, so a runaway command cannot grow memory or the
database without bound. Then:

1. appends the record to the append-only history,
2. journals a timeline entry (with an output-tail detail on failure),
3. journals a **Regression** entry when a previously green build or previously
   passing test suite goes red — timeouts count as failures, user-initiated
   cancels do not.

The UI (Builds & Tests tab) holds a `CancellationTokenSource` per run and
shows a Cancel button while a command is executing; a cancelled run is
recorded in history like any other outcome. History list views load records
without their logs; the full log is fetched per record when selected.

### Named commands (`Workbench.Core.CommandRunnerService`)

The matching-build slot and any project-defined `ProjectCommand` share a
second, unified append-only table (`command_runs`) rather than extending
`BuildRecord`/`TestRecord` to a generic name — a deliberately additive design
that keeps the original build/run/test path untouched. `CommandRunnerService`
mirrors `ExecutionService`'s behavior exactly: same bounded output capture,
same timeline journaling, same regression rule (previous run of the *same
command name* succeeded, this one didn't, and it wasn't a user cancel) —
tracked independently per command name, so a failing `field-repro` doesn't
mask or get masked by a passing `matching-build`.

### Log pattern analysis (`Workbench.Core.LogPatternAnalyzer`)

Scans a stored output log line-by-line against `LogPatternAnalyzer.DefaultPatterns`
(generic C/C++ toolchain and crash signatures: undefined reference, multiple
definition, relocation truncated, small-data section too large, fatal error,
SIGSEGV, Aborted, assert) merged with a project's `CustomLogPatterns`. Each
pattern is a regex or literal string with a category; matches carry the line
number, category, pattern name, and text. Used by the History tab's findings
panel and by the model prompt generator — never automatically, always on a
log the user is already looking at or generating a prompt from.

### Handoff (`Workbench.Core.HandoffService`)

A pure function `HandoffContext → markdown`. The context (project, frontiers,
blockers, milestones, recent timeline, last build/test, repo snapshot) is
assembled by the caller (`AppServices.BuildHandoffContext`). The recommendation
is rule-based, in priority order: fix a failing build → clear a blocker →
investigate failing tests → resume the active task → push the current frontier
→ start the next frontier → next milestone. Being pure makes it trivially
testable, and later phases reuse the same document as AI prompt seed material.

### Handoff checkpoints (`Workbench.Core.HandoffCheckpointWriter`)

Appends a timestamped copy of the handoff document to a fixed path,
`docs/ai_context/ACTIVE_HANDOFF.md`, inside the project's own repository —
the one place Workbench writes into a tracked project's working tree, in the
same category as exporting `workbench.json`. Always append-only (creates the
file and a header on first write; every subsequent call appends behind a
`---` separator, never truncating). The UI never calls `Append` directly from
a button click — `HandoffViewModel` always shows the exact text in a preview
panel first, and only calls it from a separate `Confirm` action. No git
operation (add/commit/push) is ever invoked.

### Model prompts (`Workbench.Core.PromptGeneratorService`)

A second pure function, `PromptContext → text`, distinct from the handoff
document: current frontier's full detail, the most recent failing command
found across build/run/test *and* `command_runs` (`AppServices.FindLastFailedCommand`
merges all three sources by timestamp), a bounded excerpt of that failure's
log, the log-pattern findings within it, the project's free-text prompt
rules, and git status (branch, ahead/behind, files changed, diff stat). Like
the handoff document, this only ever produces text for the user to copy —
there is no code path in this service or its caller that sends the result
anywhere.

### Configuration (`Workbench.Core.ProjectConfigService`)

Each project can carry a `workbench.json` at its repository root (name,
description, commands including matching-build, documentation paths, AI
context paths, preferred AI provider, prompt rules, custom log patterns,
tags). Registering a repository that contains one imports it automatically;
the Settings tab can export/import at any time. Custom `ProjectCommand`
entries are not yet part of this file format — they round-trip through the
database only, configured once via the Builds & Tests tab.

### UI (`Workbench.App`)

- MVVM with CommunityToolkit.Mvvm source generators; compiled bindings
  (`x:DataType`) throughout, so binding paths are checked at build time.
- `MainWindowViewModel` hosts the current page (dashboard ⇄ project detail)
  and global search. `ViewLocator` maps view models to views by convention.
- The dashboard loads project cards synchronously from SQLite (fast), then
  enriches git fields on a background task per repository — the grid stays
  responsive with dozens of projects.
- The detail page is a `TabControl`; each tab (Overview, Timeline, Milestones,
  Notes, Builds & Tests, Handoff, Settings) has its own view model, constructed
  once when the detail page opens. Tabs do not cross-refresh each other — an
  action in one tab that changes shared state (e.g. Refresh Project journaling
  a timeline entry) is visible in another tab only after it's reconstructed
  (closing and reopening the project, or restarting the app).
- Item view models (cards, search hits, blockers, milestones, command buttons,
  log findings) carry their own commands so XAML never needs ancestor bindings.

## Phase boundaries

Workbench is a standalone tool — not part of Xenogears, TEN, or any project it
tracks. Out of scope through Phase 2, by design: AI chat, LLM API calls,
automatic coding, patch generation, agent orchestration. Workbench records and
reports; it never modifies a tracked project's source code. The one
deliberate exception — appending a checkpoint to
`docs/ai_context/ACTIVE_HANDOFF.md` — is scoped narrowly (one fixed file,
append-only, preview-then-confirm, no git operations) and follows the same
precedent as exporting `workbench.json`: Workbench-owned context artifacts,
not code.

The seams later phases will use already exist: `Project.PreferredAiProvider`
(unused reserved field), `Project.AiContextPaths` (declared context inputs,
read by nothing yet), `IHandoffService` (the generated handoff document), and
`IPromptGeneratorService` (the generated model prompt — text assembly today,
the seed for an actual API call in Phase 4). The full five-phase roadmap —
execution hardening (done), prompt-export hardening, optional provider
modules, multi-project cockpit — lives in the README.
