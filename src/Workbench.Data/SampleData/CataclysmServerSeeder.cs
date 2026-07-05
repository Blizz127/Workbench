using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Cataclysm Dead-Internet Server project — a real tracked project in
/// its pre-setup stage: the plan and sources are locked, but no fork is checked
/// out yet. Like <see cref="ParasiteEveSeeder"/> (and unlike the first-run-only
/// <see cref="XenogearsSeeder"/>), this seeds if the project is missing, so it
/// appears on existing installs; remove the call in App.axaml.cs if you delete
/// it and want it to stay gone. Deliberately truthful: the goal is a solo
/// "dead internet" WoW: Cataclysm (4.3.4) server forking The Cataclysm
/// Preservation Project and porting the mod-playerbots living-world bot system
/// from AzerothCore-WOTLK. No fork, build, or bot code exists yet.
/// </summary>
public static class CataclysmServerSeeder
{
    private const string ProjectName = "Cataclysm Dead-Internet Server";

    public static void SeedIfMissing(WorkbenchDatabase database)
    {
        var projects = new ProjectRepository(database);
        if (projects.GetAll().Any(p => p.Name == ProjectName))
            return;

        // Planning locked on this date: base core (TCPP) and bot source
        // (mod-playerbots lineage) chosen after surveying the 4.3.4 ecosystem.
        var plannedOn = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Research,
            Description = "Solo 'dead internet' World of Warcraft: Cataclysm (4.3.4.15595) " +
                          "private server. Fork of The Cataclysm Preservation Project " +
                          "(TrinityCore 4.3.4) as the base core, with the mod-playerbots " +
                          "living-world bot system ported from AzerothCore-WOTLK so a single " +
                          "human player is surrounded by autonomous bots that quest, LFG, and " +
                          "PvP, and can be grouped with to solo dungeon/raid content. Same " +
                          "intent as the owner's WOTLK and Warlords bot builds, rebuilt for " +
                          "Cataclysm. Pre-setup: no fork checked out yet.",
            RepositoryPath = "/var/home/blizz/Projects/Cataclysm-Server",
            BuildCommand = "cmake --build build --config RelWithDebInfo -j",
            RunCommand = "./build/bin/worldserver",
            DocumentationPaths =
            [
                "docs/build.md",
                "docs/client-data.md",
                "docs/porting/mod-playerbots-port.md",
                "docs/porting/core-hooks.md",
                "docs/bots/cata-content.md",
            ],
            AiContextPaths =
            [
                "docs/porting/mod-playerbots-port.md",
                "docs/porting/core-hooks.md",
                "src/server/game/AI/PlayerBots/",
                "CLAUDE.md",
            ],
            PromptRules =
                """
                Base core is The-Cataclysm-Preservation-Project/TrinityCore (GPL-2.0, 4.3.4.15595) — keep the fork GPL-2.0 and publish derivative source.
                Bot target is the mod-playerbots lineage (liyunfan1223), which needs core-side hooks, not just a drop-in module — plan for a core fork, not a clean plugin.
                Never commit Blizzard client data: no MPQ archives, extracted DBC/DB2, maps/vmaps/mmaps, or proprietary assets. The owner supplies these from their own 4.3.4 client.
                Never claim a bot feature works without launching worldserver and observing it in a connected client — this is a server, not a library.
                This is for private, single-player / solo use only.
                """,
            CustomLogPatterns =
            [
                new LogPatternDefinition("worldserver ready", "World initialized", IsRegex: false, Category: "ServerBoot"),
                new LogPatternDefinition("bot spawn", @"^PlayerbotMgr:.*added bot", Category: "Bots"),
                new LogPatternDefinition("core error", "ERROR", IsRegex: false, Category: "Errors"),
            ],
            Tags =
            [
                "Game Preservation", "Server Emulation", "MMO", "World of Warcraft",
                "Cataclysm", "TrinityCore", "Player Bots",
            ],
            ActiveTask = "Phase 0 — fork TCPP and get worldserver/authserver building",
            CurrentObjective = "Stand up the base 4.3.4 core so a real character can log in and move; bot port begins in Phase 1",
            CreatedAt = plannedOn,
        };
        projects.Add(project);

        var milestones = new MilestoneRepository(database);
        (string Title, bool Done)[] milestoneData =
        [
            ("Base core chosen — The Cataclysm Preservation Project (TrinityCore 4.3.4, GPL-2.0)", true),
            ("Bot source chosen — mod-playerbots lineage (living world + group-with-bots)", true),
            ("Phase 0 — fork builds; character logs in and moves", false),
            ("Phase 1 — mod-playerbots core hooks reimplemented in TCPP; one bot spawns", false),
            ("Phase 2 — alt-login bots clear a 5-man dungeon as a party", false),
            ("Phase 3 — random bot pool populates the world (quest/LFG/PvP)", false),
            ("Phase 4 — Cata mechanics in bot logic (talents/Mastery/reforge, revamped-zone routes)", false),
        ];
        for (var i = 0; i < milestoneData.Length; i++)
        {
            var (title, done) = milestoneData[i];
            milestones.Add(new Milestone
            {
                ProjectId = project.Id,
                Title = title,
                IsCompleted = done,
                CompletedAt = done ? plannedOn : null,
                SortOrder = i,
            });
        }

        var frontiers = new FrontierRepository(database);
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Phase 0 — fork TCPP and get the core building",
            Status = FrontierStatus.Current,
            Symbol = "worldserver / authserver",
            Notes = "Base core: github.com/The-Cataclysm-Preservation-Project/TrinityCore " +
                    "(only actively-worked, script-complete, open-source Cata 4.3.4 core; " +
                    "GPL-2.0; same TrinityCore DNA as the Warlords build). Standard CMake " +
                    "build; requires 4.3.4 client data (MPQ/DBC + extracted maps/vmaps/mmaps) " +
                    "supplied from the owner's own client — never committed.",
            BlockerClass = "Setup: no fork checked out yet",
            NextSafestStep = "Fork the TCPP repo, build worldserver + authserver, load a TDB 434 " +
                             "world DB, extract client data, and confirm a real character logs " +
                             "in and moves. No bot work until this is green.",
            CreatedAt = plannedOn,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Phase 1 — reimplement mod-playerbots core hooks in TCPP",
            Status = FrontierStatus.Next,
            Symbol = "PlayerbotMgr / bot AI update loop",
            Notes = "mod-playerbots (github.com/liyunfan1223/mod-playerbots) is not a clean " +
                    "module even on stock AzerothCore — it calls into a forked core " +
                    "(liyunfan1223/azerothcore-wotlk). The port is: (1) identify the minimal " +
                    "core hooks it needs, (2) reimplement them in the Cataclysm Preservation " +
                    "core, (3) translate AzerothCore APIs to TrinityCore ones. No open-source " +
                    "bot system targets 4.3.4 today — this is the highest-risk item.",
            BlockerClass = "Port: AzerothCore-WOTLK to TrinityCore-Cataclysm API translation",
            NextSafestStep = "After Phase 0, fork mod-playerbots and diff liyunfan's AC core fork " +
                             "against stock AC to enumerate the required hooks; get one bot to " +
                             "spawn and stand in the world before adding any AI.",
            CreatedAt = plannedOn,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Phase 4 — Cata mechanics in bot logic (talents/Mastery/reforge, quest routes)",
            Status = FrontierStatus.Next,
            Notes = "Cataclysm rewrote the talent trees, added Mastery and reforging, and revamped " +
                    "the 1–60 questing zones. Ported bot content will be WOTLK-flavored until this " +
                    "is redone for 4.3.4. Reference-only lineages to mine: trickerer/Trinity-Bots " +
                    "(NPCBots, TrinityCore-native but 3.3.5-only) and cmangos/playerbots (ike3 logic).",
            CreatedAt = plannedOn,
        });

        var blockers = new BlockerRepository(database);
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "No open-source player-bot system targets WoW 4.3.4 — every mature option " +
                          "(mod-playerbots, trickerer NPCBots, cmangos playerbots) is WOTLK 3.3.5a. " +
                          "The living-world + group-with-bots experience requires porting the " +
                          "mod-playerbots lineage from AzerothCore, which needs core-side hooks.",
            CreatedAt = plannedOn,
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "TCPP release cadence has slowed (last tagged TDB 434 was Jan 2022), so the " +
                          "fork may need to self-maintain DB and script fixes rather than pull upstream.",
            CreatedAt = plannedOn,
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "trickerer's NPCBots (the TrinityCore-native bot system) is 3.3.5-only; the " +
                          "maintainer tried 4.3.4 years ago, dropped it, and has no plans to revisit — " +
                          "so it is a logic reference, not a shortcut.",
            IsResolved = true,
            Resolution = "Confirmed via the maintainer's own reply on Trinity-Bots discussion #336 " +
                         "(2026-07-05 research). Decision: port the mod-playerbots lineage instead, " +
                         "which delivers both the living world and group-with-bots solo play.",
            CreatedAt = plannedOn,
            ResolvedAt = plannedOn,
        });

        var timeline = new TimelineRepository(database);
        (TimelineCategory Category, string Message)[] entries =
        [
            (TimelineCategory.Status, "Surveyed the Cataclysm 4.3.4 emulator ecosystem for a forkable core + living-world bots"),
            (TimelineCategory.Milestone, "Base core chosen: The Cataclysm Preservation Project (TrinityCore 4.3.4, GPL-2.0) — only viable open-source Cata core"),
            (TimelineCategory.Blocker, "Confirmed no open-source bot system targets 4.3.4; all mature options are WOTLK 3.3.5a"),
            (TimelineCategory.Milestone, "Bot source chosen: mod-playerbots lineage (liyunfan1223) — living world + group-with-bots; a core-hook port, not a plugin"),
            (TimelineCategory.Frontier, "Phase 0 set as current frontier: fork TCPP and get the core building before any bot work"),
        ];
        foreach (var (category, message) in entries)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Timestamp = plannedOn,
                Category = category,
                Message = message,
            });
        }

        var notes = new NoteRepository(database);
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Project links & porting strategy",
            Content =
                """
                # Cataclysm Dead-Internet Server — links & strategy

                **Goal:** a solo "dead internet" WoW: Cataclysm (4.3.4.15595) server —
                one human in a world otherwise full of autonomous bots that quest, LFG,
                and PvP, and that you can group with to solo dungeon/raid content. Same
                intent as the owner's WOTLK and Warlords bot builds.

                ## The two pieces
                - **Base core (fork this):** The Cataclysm Preservation Project —
                  https://github.com/The-Cataclysm-Preservation-Project/TrinityCore
                  (GPL-2.0, 4.3.4.15595; the only actively-worked, script-complete,
                  open-source Cata core; same TrinityCore DNA as the Warlords build).
                - **Bots (port this):** mod-playerbots —
                  https://github.com/liyunfan1223/mod-playerbots and its core fork
                  https://github.com/liyunfan1223/azerothcore-wotlk (the only system that
                  delivers *both* a living world *and* group-with-bots solo play).

                ## Reference-only (mine, do not port directly)
                - trickerer/Trinity-Bots — https://github.com/trickerer/Trinity-Bots
                  (NPCBots; TrinityCore-native but 3.3.5-only and group-hire, not living-world).
                - cmangos/playerbots — https://github.com/cmangos/playerbots (original ike3
                  living-world logic on MaNGOS).

                ## Why this is a port, not a plug-in
                No open-source player-bot system targets 4.3.4 today — every mature option
                is WOTLK 3.3.5a. mod-playerbots is not even a clean module on stock
                AzerothCore: it calls into a forked core. So the work is (1) reimplement
                those core hooks in the Cataclysm Preservation core, (2) translate
                AzerothCore APIs to TrinityCore ones, and (3) rebuild bot content for Cata
                mechanics (rewritten talent trees, Mastery, reforging, revamped 1–60 zones).

                ## Phases
                0. Core standup — fork TCPP; build worldserver/authserver; extract 4.3.4
                   client data; a real character logs in and moves.
                1. Bot foundation — reimplement the minimal mod-playerbots core hooks in
                   TCPP; one bot spawns and is controllable (no AI yet).
                2. Solo-party bots — alt-login bots invite/follow/fight; clear a 5-man.
                3. Living world — random bot pool spawns, levels, quests, LFGs, PvPs.
                4. Cata tuning — 4.3.4 talents/Mastery/reforge in bot logic; revamped-zone
                   quest routes; Cata dungeon/raid scripting for bot groups.

                ## Hard rules
                - Keep the fork GPL-2.0 and publish derivative source.
                - Never commit Blizzard client data (MPQ/DBC/DB2, maps/vmaps/mmaps, assets) —
                  supplied locally from the owner's own 4.3.4 client.
                - Private, single-player / solo use only.
                """,
            CreatedAt = plannedOn,
        });
    }
}
