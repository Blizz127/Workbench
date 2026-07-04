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
- List-valued configuration (documentation paths, AI context paths, custom log
  patterns) is a JSON array in a `TEXT` column; tags are a separate table so
  they stay queryable.
- All child tables cascade on project delete.
- `build_records`, `test_records`, and `command_runs` are **append-only** —
  the repositories expose no update or delete.
- `output_log` columns are excluded from list queries and fetched per record
  (`GetOutputLog`), so history views never load every stored log. Logs are
  bounded at capture time by `ProcessRunner` (head + tail, marker line when
  content is dropped), so a runaway command cannot bloat the database.

## Migrations

| user_version | Change |
|--------------|--------|
| 1 | Initial schema (projects, milestones, frontiers, blockers, timeline, notes, build/test history). |
| 2 | Rewrites all v1 timestamps from local-offset to UTC (same instant, full tick precision, done in C#). Unparseable values are left untouched. |
| 3 | Adds matching-build command, prompt rules, custom log patterns, and last-known-git-state cache columns to `projects`; adds symbol/evidence/blocker-class/next-safest-step/assigned-model columns to `frontiers`; adds `project_commands` and `command_runs` tables. |

## Tables (user_version = 3)

### projects

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | autoincrement |
| name | TEXT NOT NULL | |
| status | TEXT NOT NULL | Active, Waiting, Broken, Research, Complete |
| description | TEXT | |
| repository_path | TEXT | local git clone |
| build_command / run_command / test_command | TEXT | run via shell |
| matching_build_command | TEXT | v3 — a build invoked specifically to check match/regression status |
| documentation_paths | TEXT NOT NULL | JSON array |
| ai_context_paths | TEXT NOT NULL | JSON array — declared inputs for later prompt-export phases |
| preferred_ai_provider | TEXT | reserved for later phases |
| prompt_rules | TEXT | v3 — free text included verbatim in the generated model prompt |
| custom_log_patterns | TEXT NOT NULL | v3 — JSON array of `{name, pattern, isRegex, category}` |
| active_task | TEXT | shown on the dashboard card |
| current_objective | TEXT | shown in the detail view |
| last_known_branch / last_known_commit_sha | TEXT | v3 — set only by the Refresh Project action |
| last_known_is_dirty | INTEGER (0/1, nullable) | v3 |
| last_known_ahead_by / last_known_behind_by | INTEGER (nullable) | v3 — null when there is no upstream tracking branch |
| last_refreshed_at | TEXT | v3 — null until the first Refresh Project |
| created_at / updated_at | TEXT NOT NULL | |

### project_tags

`(project_id, tag)` composite PK; cascade delete.

### milestones

`id, project_id, title, is_completed (0/1), completed_at, sort_order, notes`
— indexed on `(project_id, sort_order)`.

### frontiers

`id, project_id, title, status (Next|Current|Completed), notes, symbol,
evidence, blocker_class, next_safest_step, assigned_model, created_at,
completed_at` — indexed on `(project_id, status)`. By convention one Current
and one Next per project; advancing completes the previous Current. The five
columns after `notes` were added in v3 so a frontier carries the detail a
handoff or model prompt actually needs — the exact symbol, what supports the
current understanding, a free-text blocker classification, the next safest
step, and which model/person is nominally assigned (plain metadata, not an
API call).

### blockers

`id, project_id, description, is_resolved (0/1), resolution, created_at,
resolved_at` — indexed on `(project_id, is_resolved)`.

### timeline_entries

`id, project_id, timestamp, category, message, detail` — indexed on
`(project_id, timestamp DESC)`. Append-only engineering journal. Categories:
General, Build, Run, Test, Code, Milestone, Frontier, Blocker, Note,
Regression, Status, GitSync. `GitSync` needed no migration — it's a new value
of an existing `TEXT` column, not a schema change.

### build_records

`id, project_id, kind (Build|Run), command, started_at, exit_code (nullable),
duration_ms, output_log, commit_sha` — indexed on
`(project_id, started_at DESC)`. `exit_code` is NULL when the process could
not start, timed out, or was cancelled.

### test_records

`id, project_id, command, result (Passed|Failed|Error|TimedOut), started_at,
duration_ms, notes, commit_sha, output_log` — indexed on
`(project_id, started_at DESC)`.

### project_commands *(v3)*

`id, project_id, name, command_text, sort_order` — indexed on
`(project_id, sort_order)`. User-named commands beyond the fixed
build/run/test/matching-build slots.

### command_runs *(v3)*

`id, project_id, command_name, command_text, working_directory, started_at,
exit_code (nullable), duration_ms, output_log, output_truncated (0/1),
git_head_sha, was_dirty (0/1), regression_detected (0/1), cancelled (0/1)` —
indexed on `(project_id, started_at DESC)` and
`(project_id, command_name, started_at DESC)`. Append-only history for
matching-build and custom command executions — a second history table rather
than extending `build_records`/`test_records` to a generic name, so the
original build/run/test path stays untouched. `regression_detected` is set
when the previous run of the *same command name* had succeeded and this one
didn't (and it wasn't a cancel).
