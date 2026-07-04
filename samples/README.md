# Sample project configurations

Example `workbench.json` files for the shapes of project Workbench is built
to track. Drop a file like these at the root of a repository and register that
path in Workbench (Dashboard → Add project) — the configuration is imported
automatically. Any project's Settings tab can export/import the same file.

Workbench is project-shape agnostic; these differ only in what their commands
and frontiers look like:

| Sample | Shape | Typical frontier |
|--------|-------|------------------|
| [`xenogears/`](xenogears/) | Matching decompilation toward a native PC port | a function address (`func_8001FBE4`) |
| [`parasite-eve/`](parasite-eve/) | Early-phase decomp: disc verification/setup only, parked | a disc or executable verification step |
| [`ten-revival/`](ten-revival/) | Reviving a dead online-game network service | a protocol message or lobby flow |
| [`crimson-sands/`](crimson-sands/) | MMO server emulation from packet captures | a packet opcode or game system |
| [`generic-software/`](generic-software/) | Any ordinary software project | a feature or subsystem |

Notes that apply to all samples:

- Paths in `documentationPaths` and `aiContextPaths` are relative to the
  repository root.
- `aiContextPaths` declare what an AI assistant should be handed as context
  alongside the generated handoff document. In Phase 1 nothing reads them
  automatically — they are declared inputs for the later export phases.
- The Xenogears sample also exists as seeded data (milestones, frontiers,
  blockers, timeline, history) on first launch of the app.
- The Parasite Eve sample is likewise seeded (parked, minimal truthful
  history) whenever it is missing from the database — it is a real tracked
  project still in its disc-verification phase, not yet in decompilation.
