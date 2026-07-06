# Cataclysm Dead-Internet Server sample configuration

A real tracked project in its **pre-setup stage**: the plan and sources are
locked, but no fork is checked out yet. It shares the *server revival* shape
with the [`crimson-sands/`](../crimson-sands/) sample, but instead of
reconstructing a dead protocol it forks a live open-source core and ports a
bot system onto it.

The goal is a **solo "dead internet" World of Warcraft: Cataclysm server**:
one human player in a world otherwise populated entirely by autonomous bots
that quest, use LFG, and PvP — and that you can group with to solo dungeon and
raid content. It is the same intent as the owner's existing WOTLK and Warlords
bot builds, rebuilt for Cataclysm (4.3.4.15595).

- Repository: <https://github.com/Blizz127/catacalsym-DIT>
- Base core: [The Cataclysm Preservation Project](https://github.com/The-Cataclysm-Preservation-Project/TrinityCore) (TrinityCore 4.3.4, GPL-2.0)
- Bot source: [mod-playerbots](https://github.com/liyunfan1223/mod-playerbots) lineage (living world + group-with-bots)

## The two pieces

| Piece | Source | Why |
|-------|--------|-----|
| Base core | [The-Cataclysm-Preservation-Project/TrinityCore](https://github.com/The-Cataclysm-Preservation-Project/TrinityCore) | The only actively-worked, script-complete, open-source (GPL-2.0) Cata 4.3.4 core. Same TrinityCore DNA as the Warlords build. |
| Bots | [liyunfan1223/mod-playerbots](https://github.com/liyunfan1223/mod-playerbots) (+ its [core fork](https://github.com/liyunfan1223/azerothcore-wotlk)) | The only bot system that delivers *both* a living world *and* group-with-bots solo play. Same lineage as the WOTLK build. |

Reference-only lineages worth mining but not porting directly:
[trickerer/Trinity-Bots](https://github.com/trickerer/Trinity-Bots) (NPCBots —
TrinityCore-native but 3.3.5-only and group-hire, not living-world) and
[cmangos/playerbots](https://github.com/cmangos/playerbots) (the original ike3
living-world logic on MaNGOS).

## Why this is a port, not a plug-in

There is **no open-source player-bot system for 4.3.4 today** — every mature
option targets WOTLK 3.3.5a. mod-playerbots is not even a clean module on stock
AzerothCore: it needs a *forked core* with hooks the module calls into. So the
work is (1) reimplement those core hooks in the Cataclysm Preservation core,
(2) translate AzerothCore API calls to TrinityCore ones, and (3) rebuild the
bot content for Cata mechanics (the rewritten talent trees, Mastery, reforging,
and the revamped 1–60 questing zones).

## Phased frontier

| Phase | Frontier | Done when |
|-------|----------|-----------|
| 0 — Core standup | Fork TCPP; build worldserver/authserver; extract 4.3.4 client data | A real character logs in and moves in the world |
| 1 — Bot foundation | Reimplement the minimal mod-playerbots core hooks in TCPP | One bot spawns, stands in world, and is controllable (no AI) |
| 2 — Solo-party bots | Alt-login bots: invite, follow, combat rotation | You can clear a 5-man dungeon with a bot party |
| 3 — Living world | Random bot pool spawns, levels, quests, LFGs, PvPs | The world feels populated with no other humans |
| 4 — Cata tuning | 4.3.4 talents/Mastery/reforge in bot logic; revamped-zone quest routes; Cata dungeon/raid scripting | Bots play Cata content correctly, not WOTLK-flavored |

## Commands in this sample

| Slot | Command | Purpose |
|------|---------|---------|
| Build | `cmake --build build --config RelWithDebInfo -j` | Standard TrinityCore/TCPP CMake build. |
| Run | `./build/bin/worldserver` | Boots the world server (authserver runs alongside). |

There is intentionally **no test command**: TrinityCore ships no meaningful
automated test suite, and this is a server whose behavior is verified by
connecting a client and watching bots act — not by unit tests. The
`customLogPatterns` pick up the `World initialized` boot marker and
`PlayerbotMgr` bot-spawn lines.

`repositoryPath` is machine-specific (import ignores it; it documents the
expected local checkout). `promptRules` capture the hard rules: keep the fork
GPL-2.0, never commit Blizzard client data, and never claim a bot feature works
without observing it in a connected client.

A minimal version of this project (phase milestones, the current Phase 0
frontier, open blockers, timeline) is seeded into the database on launch if it
is not already present — pre-setup, so the dashboard makes clear it is planning
the fork, not yet building it.
