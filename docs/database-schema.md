# Database Schema

SQLite, WAL mode, foreign keys enforced. Schema changes are ordered migrations
keyed off `PRAGMA user_version` (see `WorkbenchDatabase.Migrations`); shipped
migrations are never edited, only appended.

Location: `%APPDATA%/Workbench/workbench.db` (Windows) or
`~/.config/Workbench/workbench.db` (Linux). Override the directory with
`WORKBENCH_DATA_DIR`.

Conventions:

- Timestamps are ISO-8601 round-trip strings (`TEXT`), stored in **UTC** since
  schema v2 so lexicographic `ORDER BY` matches chronological order. Read back
  as `DateTimeOffset`.
- Enums are stored by name (`TEXT`), parsed case-insensitively.
- List-valued configuration (documentation paths, AI context paths) is a JSON
  array in a `TEXT` column; tags are a separate table so they stay queryable.
- All child tables cascade on project delete.
- `build_records` and `test_records` are **append-only** — the repositories
  expose no update or delete.
- `output_log` columns are excluded from list queries and fetched per record
  (`GetOutputLog`), so history views never load every stored log. Logs are
  bounded at capture time by `ProcessRunner` (head + tail, marker line when
  content is dropped), so a runaway command cannot bloat the database.

## Migrations

| user_version | Change |
|--------------|--------|
| 1 | Initial schema (all tables below). |
| 2 | Rewrites all v1 timestamps from local-offset to UTC (same instant, full tick precision, done in C#). Unparseable values are left untouched. |

## Tables (user_version = 2)

### projects

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | autoincrement |
| name | TEXT NOT NULL | |
| status | TEXT NOT NULL | Active, Waiting, Broken, Research, Complete |
| description | TEXT | |
| repository_path | TEXT | local git clone |
| build_command / run_command / test_command | TEXT | run via shell |
| documentation_paths | TEXT NOT NULL | JSON array |
| ai_context_paths | TEXT NOT NULL | JSON array, unused until later phases |
| preferred_ai_provider | TEXT | reserved for later phases |
| active_task | TEXT | shown on the dashboard card |
| current_objective | TEXT | shown in the detail view |
| created_at / updated_at | TEXT NOT NULL | |

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
