# Xenogears sample configuration

This is an example of the per-project `workbench.json` configuration file.
Drop a file like this at the root of a repository and register that path in
Workbench (Dashboard → Add project) — the configuration is imported
automatically. The Settings tab of any project can export/import the same file.

Note on scope: `workbench.json` currently round-trips the fixed command slots
(build/run/test/matching-build), documentation and AI context paths, prompt
rules, and custom log patterns. The **custom command** (`field-repro` below)
is not yet part of the file format — add it once through the Builds & Tests
tab's "+ Add" form and it's saved to the project from then on.

## Commands in this sample

| Slot | Command | Purpose |
|------|---------|---------|
| Build | `./pc_port/build_port.sh` | Builds the native PC port. |
| Run | `./build/xenogears` | Launches the port. |
| Test | `python3 tools/check_matches.py` | Reports function match percentage. |
| Matching build | `make -B build` | A build invoked specifically to check match/regression status, separate from the day-to-day PC port build. |
| Custom: `field-repro` | `cd pc_port && XENO_FIELD_TEST=1 XENO_KERNEL_SEL=0 timeout -s KILL 10 build_native/xeno-port` | Boots straight into a field scene under a kill timeout — a targeted repro, not the full build/run cycle. |

`customLogPatterns` teaches the log-finding scanner this project's own
markers (`RUN_RC=`, `[stub]`, `ParsePrimitive`, `field-diag`) on top of the
built-in generic toolchain/crash patterns (undefined reference, SIGSEGV,
Aborted, assert, …).

A richer version of this project (milestones, frontiers with the full
symbol/evidence/blocker-class detail, blockers, timeline, notes, build/test
history, and matching-build/custom command history with a caught regression)
is seeded into the database on first launch so a fresh install demonstrates
every subsystem.
