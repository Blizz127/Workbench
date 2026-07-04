# Parasite Eve sample configuration

A real tracked project in its **earliest lifecycle stage**: repository
scaffolded, disc verification in progress, and deliberately *nothing else*.
It complements the Xenogears sample, which shows a mature matching decomp —
this one shows what Workbench tracks **before** a project earns build, run,
or matching-build commands.

- Repository: <https://github.com/Blizz127/Parasite-Eve-Decompilation>
- Target: Parasite Eve USA / NTSC-U (Disc 1 `SLUS-00662`, Disc 2 `SLUS-00668`)
- Current phase: **Phase 1 — locally complete and corroborated** (both discs recorded; boot EXE and PE.IMG byte-identical across discs; official redump check pending)
- Status: parked / early setup — secondary to Xenogears

## Commands in this sample

| Slot | Command | Purpose |
|------|---------|---------|
| Test | `./scripts/extract_us.sh all` | Verifies user-supplied disc images, extracts and hashes the boot executables locally. |

There is intentionally **no build, run, or matching-build command**: no
disassembly, symbols, or decompiled C exist yet, and the project's own rules
forbid adding them before a checksum/rebuild harness exists. The
`customLogPatterns` pick up the verification script's `OK: disc` / `SKIP:`
markers.

`repositoryPath` is machine-specific (import ignores it; it documents the
expected local checkout). `promptRules` mirror the hard rules in the
project's own `CLAUDE.md`.

A minimal version of this project (milestones for phases 0–2, the current
disc-2 frontier, open blockers, timeline) is seeded into the database on
launch if it is not already present — parked, so the dashboard makes clear
it is in setup/verification, not decompilation.
