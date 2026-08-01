# Sample project configurations

Example `workbench.json` files for the shapes of project Workbench is built
to track. Drop a file like these at the root of a repository and register that
path in Workbench (Dashboard → Add project) — the configuration is imported
automatically. Any project's Settings tab can export/import the same file.

Workbench is project-shape agnostic; these differ only in what their commands
and frontiers look like:

| Sample | Shape | Typical frontier |
|--------|-------|------------------|
| [`xenogears/`](xenogears/) | Matching decompilation toward a native PC port | a field/menu subsystem or red ROM pin |
| [`parasite-eve/`](parasite-eve/) | Active matching decomp (Phase 5 leaf harvest) | a C leaf / era fingerprint (`func_8002F970`) |
| [`ten-revival/`](ten-revival/) | Reviving a dead online-game network service | a client patch or name-service resolve step |
| [`crimson-sands/`](crimson-sands/) | TEN-exclusive MMO preservation (depends on TEN) | first packet capture / game-service adapter |
| [`draenor/`](draenor/) | WoW WoD 6.2.3 server build + runtime baseline | world playability / content batch |
| [`banshee-realm/`](banshee-realm/) | FFXI private server operations (docker compose stack with stop/logs/update/metrics commands) | a content batch / runtime stability fix |
| [`ffxi-events-dump/`](ffxi-events-dump/) | FFXI event bytecode parser | multi-event CALL chain / opcode coverage |
| [`generic-software/`](generic-software/) | Any ordinary software project | a feature or subsystem |

Notes that apply to all samples:

- Paths in `documentationPaths` and `aiContextPaths` are relative to the
  repository root.
- `aiContextPaths` declare what an AI assistant should be handed as context
  alongside the generated handoff document. In Phase 1 nothing reads them
  automatically — they are declared inputs for the later export phases.
- Sample projects are **inert demo data** — the startup seeders no longer
  run, so nothing here is registered automatically. Copy a `workbench.json`
  into your own repository (or drop the repo under the discovery folder,
  default `~/dev`) and it is adopted on the next scan. Sample content tracks
  real local project state as of the last workbench update — especially
  Parasite Eve (active Phase 5 matching), Xenogears (field/menu + red pins),
  TEN/Dark Sun, and Draenor.
