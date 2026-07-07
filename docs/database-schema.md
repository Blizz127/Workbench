# Database Schema

SQLite, WAL mode, foreign keys enforced. Schema changes are ordered migrations
keyed off `PRAGMA user_version` (see `WorkbenchDatabase.Migrations`); shipped
migrations are never edited, only appended.

Location: `%APPDATA%/Workbench/workbench.db` (Windows) or
`~/.config/Workbench/workbench.db` (Linux). Override the directory with
`WORKBENCH_DATA_DIR`.

Conventions:

- Timestamps are ISO-8601 round-trip strings (`TEXT`), read back as
  `DateTimeOffset`.
- Enums are stored by name (`TEXT`), parsed case-insensitively.
- List-valued configuration (documentation paths, AI context paths) is a JSON
  array in a `TEXT` column; tags are a separate table so they stay queryable.
- All child tables cascade on project delete.
- `build_records` and `test_records` are **append-only** — the repositories
  expose no update or delete.

## Tables (user_version = 4)

Migration numbering note: databases created by the pre-rewrite Workbench
lineage arrive at `user_version = 3` with extra columns/tables this codebase
does not use. The shared-state migration is therefore numbered **4** and adds
its columns individually, guarded by `PRAGMA table_info` — the legacy lineage
already carried `prompt_rules` and `custom_log_patterns`. Versions 2–3 are
reserved (legacy) and intentionally absent from this repo's migration list.

### projects

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | autoincrement |
| name | TEXT NOT NULL | |
| status | TEXT NOT NULL | Active, Waiting, Broken, Research, Complete |
| description | TEXT | |
| repository_path | TEXT | local git clone; per-machine, never shared directly |
| build_command / run_command / test_command | TEXT | run via shell |
| documentation_paths | TEXT NOT NULL | JSON array |
| ai_context_paths | TEXT NOT NULL | JSON array, unused until later phases |
| preferred_ai_provider | TEXT | assigned/preferred model for shared state |
| active_task | TEXT | shown on the dashboard card |
| current_objective | TEXT | shown in the detail view |
| created_at / updated_at | TEXT NOT NULL | |
| shared_id | TEXT | stable cross-machine id; unique when set (partial index) |
| blocker_class | TEXT | shared-state field (v4) |
| evidence | TEXT | shared-state field (v4) |
| next_safest_step | TEXT | shared-state field (v4) |
| prompt_rules | TEXT | JSON array; NULL possible in legacy rows — read defensively |
| custom_log_patterns | TEXT | JSON array; same legacy caveat |
| github_repo_url | TEXT | shared-state field (v4) |
| default_branch | TEXT | shared-state field (v4) |
| shared_last_imported_at / shared_last_exported_at | TEXT | local-only sync bookkeeping, never exported |
| shared_last_synced_stamp | TEXT | shared file's `updatedAt` at last sync; conflict baseline |

Sync bookkeeping is written through `IProjectRepository.UpdateSharedSync`,
which deliberately does **not** bump `updated_at` — syncing is not a user edit,
and the local-changed-since-sync check compares `updated_at` against these
stamps.

### project_tags

`(project_id, tag)` composite PK; cascade delete.

### milestones

`id, project_id, title, is_completed (0/1), completed_at, sort_order, notes`
— indexed on `(project_id, sort_order)`.

### frontiers

`id, project_id, title, status (Next|Current|Completed), notes, created_at,
completed_at` — indexed on `(project_id, status)`. By convention one Current
and one Next per project; advancing completes the previous Current.

### blockers

`id, project_id, description, is_resolved (0/1), resolution, created_at,
resolved_at` — indexed on `(project_id, is_resolved)`.

### timeline_entries

`id, project_id, timestamp, category, message, detail` — indexed on
`(project_id, timestamp DESC)`. Append-only engineering journal. Categories:
General, Build, Run, Test, Code, Milestone, Frontier, Blocker, Note,
Regression, Status.

### build_records

`id, project_id, kind (Build|Run), command, started_at, exit_code (nullable),
duration_ms, output_log, commit_sha` — indexed on
`(project_id, started_at DESC)`. `exit_code` is NULL when the process could
not start or timed out.

### test_records

`id, project_id, command, result (Passed|Failed|Error|TimedOut), started_at,
duration_ms, notes, commit_sha, output_log` — indexed on
`(project_id, started_at DESC)`.
