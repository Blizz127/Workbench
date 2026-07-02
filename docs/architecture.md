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
│                      ProjectConfigService              │
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
| Timeline | `TimelineEntry` | Append-only engineering journal. Categories: Build, Run, Test, Code, Milestone, Frontier, Blocker, Note, Regression, Status, General. |
| Build history | `BuildRecord` | Append-only (`ExecutionKind.Build` or `.Run`): command, timestamp, exit code, duration, full output log, HEAD sha. |
| Test history | `TestRecord` | Append-only: command, result (Passed/Failed/Error/TimedOut), runtime, notes, associated commit. |
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

### Execution (`Workbench.Core.ExecutionService`)

Runs the configured build/run/test command via `ProcessRunner` (cmd.exe on
Windows, `/bin/sh` elsewhere; output captured interleaved; configurable
timeout), then:

1. appends the record to the append-only history,
2. journals a timeline entry (with an output-tail detail on failure),
3. journals a **Regression** entry when a previously green build or previously
   passing test suite goes red.

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

### UI (`Workbench.App`)

- MVVM with CommunityToolkit.Mvvm source generators; compiled bindings
  (`x:DataType`) throughout, so binding paths are checked at build time.
- `MainWindowViewModel` hosts the current page (dashboard ⇄ project detail)
  and global search. `ViewLocator` maps view models to views by convention.
- The dashboard loads project cards synchronously from SQLite (fast), then
  enriches git fields on a background task per repository — the grid stays
  responsive with dozens of projects.
- The detail page is a `TabControl`; each tab (Overview, Timeline, Milestones,
  Notes, Builds & Tests, Handoff, Settings) has its own view model.
- Item view models (cards, search hits, blockers, milestones) carry their own
  commands so XAML never needs ancestor bindings.

## Phase boundaries

Out of scope for Phase 1, by design: AI chat, LLM providers, prompt
generation, automatic coding, patch generation, agent orchestration. The seams
they will use already exist: `Project.PreferredAiProvider`,
`Project.AiContextPaths`, and `IHandoffService`.
