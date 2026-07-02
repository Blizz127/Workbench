# Workbench

Workbench is a cross-platform desktop application for managing long-running
software engineering, reverse engineering, preservation, and research projects —
think Visual Studio, GitHub Desktop, Jira, and a CI dashboard combined, built
for solo developers and preservation work.

It is **not** an AI chatbot. Workbench tracks project state, progress,
frontiers, builds, tests, notes, and handoffs across many independent projects.
AI providers arrive in later phases as interchangeable plug-in modules that
consume the state Workbench maintains.

## What it does (Phase 1)

- **Project dashboard** — every registered project as a card: status, repository,
  branch, last commit, last build/test result, current frontier, active task.
  Filter by Active / Waiting / Broken / Research / Complete.
- **Project details** — repository info, recent commits, git status, modified
  files, open TODO comments, frontiers, blockers, recent notes, current
  milestone and objective.
- **Progress tracking** — milestones, frontiers (current / next / completed),
  blockers (open / resolved), all persisted in SQLite and surviving restarts.
- **Timeline** — an append-only engineering journal per project
  (`Build succeeded`, `Implemented func_800248D4 opcode 0xB3`, …). Build, test,
  and regression events are journaled automatically.
- **Notes** — free-form markdown notes, searchable.
- **Handoff** — a generated markdown summary of current state, last completed
  work, blockers, and a recommended next step. Later phases feed this to AI
  prompts.
- **Build / run / test execution** — run the project's configured commands from
  the app; exit code, duration, and full output log are recorded in
  append-only history. A previously green command going red is journaled as a
  regression.
- **Tags and global search** — search spans projects, notes, milestones,
  timeline entries, and frontiers.
- **Per-project configuration** — each project can carry a `workbench.json` at
  its repository root (repository path, build/run/test commands, documentation
  paths, AI context paths, preferred AI provider) so configuration travels with
  the code. See [`samples/xenogears/workbench.json`](samples/xenogears/workbench.json).

## Getting started

Requirements: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
Runs on Windows and Linux.

```bash
dotnet run --project src/Workbench.App
```

On first launch Workbench seeds a sample project (Xenogears Decompilation) so
every subsystem has something to show. Data is stored in
`%APPDATA%/Workbench/workbench.db` (Windows) or `~/.config/Workbench/workbench.db`
(Linux); override the directory with the `WORKBENCH_DATA_DIR` environment
variable.

Run the tests:

```bash
dotnet test
```

## Solution layout

| Project | Purpose |
|---------|---------|
| `src/Workbench.Core` | Domain models and service contracts. No external dependencies. |
| `src/Workbench.Data` | SQLite persistence: schema migrations, repositories, search. |
| `src/Workbench.Git`  | LibGit2Sharp-backed git snapshotting and TODO scanning. |
| `src/Workbench.App`  | Avalonia UI (MVVM via CommunityToolkit.Mvvm). |
| `tests/Workbench.Tests` | xunit tests for the data layer and core services. |

See [docs/architecture.md](docs/architecture.md) for the design and
[docs/database-schema.md](docs/database-schema.md) for the schema.

## Roadmap

- **Phase 1 (this)** — project management foundation.
- **Later phases** — AI provider modules (Claude, GPT, local models, …) that
  plug into Workbench, consume the handoff/context data, and act on projects.
  Providers are plugins; Workbench never depends on any one model.
