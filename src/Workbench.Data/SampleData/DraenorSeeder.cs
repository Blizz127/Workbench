using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Draenor-Core build &amp; runtime project — WoW: Warlords of Draenor
/// (6.2.3, build 20779) preservation. Build/login/world/movement baseline is
/// proven on frFR; Phase 4 world playability / content is in progress.
/// </summary>
public static class DraenorSeeder
{
    public const string ProjectName = "Draenor-Core Build & Runtime";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "WoW: Warlords of Draenor 6.2.3.20779 preservation baseline for " +
                          "EyalMK/Draenor-Core on modern Linux (GCC 13 / OpenSSL 3 / ACE 7.1.2). " +
                          "Server builds clean, Firestorm DBs imported, frFR client reaches world " +
                          "with movement. Phase 4 world playability and content fixes are current.",
            RepositoryPath = "/home/blizz/Projects/draenor-dit",
            BuildCommand = "cd /home/blizz/Draenor-Core/build && cmake --build . --target worldserver -j2",
            RunCommand = "podman exec draenor-devbox bash -lc 'cd /home/blizz/Draenor-Core/run-world && rm -f console.fifo && mkfifo console.fifo && exec 9<>console.fifo && exec ./worldserver < console.fifo >> worldserver-live.log 2>&1'",
            TestCommand = "podman exec draenor-devbox bash -lc 'ps -eo pid,ppid,stat,comm,args | grep \"[.]\\/worldserver\" || true; ss -ltnp 2>/dev/null | grep 8085 || true; tail -40 /home/blizz/Draenor-Core/run-world/worldserver-live.log'",
            DocumentationPaths =
            [
                "STATUS.md",
                "docs/CURRENT_STATUS.md",
                "docs/PRESERVATION_PLAN.md",
                "docs/protocol-compatibility-20779.md",
                "runtime/",
            ],
            AiContextPaths =
            [
                "STATUS.md",
                "docs/CURRENT_STATUS.md",
                "docs/SERVER_WORLD_VIEW.md",
                "docs/protocol-compatibility-20779.md",
                "draenor-core-build-fixes.patch",
                "runtime/start_servers.sh",
            ],
            Tags = ["Game Preservation", "Server Revival", "Reverse Engineering", "WoW", "WoD", "Build Engineering"],
            ActiveTask = "Phase 4 world playability: content batches + 20779 gameplay smoke baseline",
            CurrentObjective = "Stable 6.2.3.20779 frFR play session with fixed content gaps; enUS pack re-verify later",
            Launch = new LaunchProfile
            {
                // Detached setsid/distrobox launchers; evidence is the TCP
                // listeners from runtime/start-all.sh (devbox 3307/1119/8085,
                // host :53/:80/:87 from the sudo setup step).
                StartupGraceSeconds = 15,
                StatusCommand = "check() { if distrobox enter draenor-devbox -- bash -lc \"ss -ltn | grep -q ':$2 '\" 2>/dev/null; then echo \"$1 up\"; else echo \"$1 down\"; fi; }; " +
                                "check mariadb 3307; check authserver 1119; check worldserver 8085; " +
                                "if ss -lun 2>/dev/null | grep -q ':53 '; then echo 'dns up'; else echo 'dns down'; fi; " +
                                "if ss -ltn 2>/dev/null | grep -q ':80 '; then echo 'ngdp-http up'; else echo 'ngdp-http down'; fi; " +
                                "if ss -ltn 2>/dev/null | grep -q ':87 '; then echo 'auth-relay up'; else echo 'auth-relay down'; fi",
                RequiredServices = ["mariadb", "authserver", "worldserver"],
                OptionalServices = ["dns", "ngdp-http", "auth-relay"],
                HealthCheckRetries = 6,
                HealthCheckIntervalSeconds = 20,
            },
            CreatedAt = now.AddDays(-30),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Modern Linux Build (GCC 13 / OpenSSL 3)", true, 28),
            ("authserver Operational", true, 26),
            ("worldserver Boots to World Init", true, 25),
            ("Databases Imported (auth/char/world/hotfix/web)", true, 24),
            ("Client Connect + Login + Enter World", true, 22),
            ("In-World Render (Stormwind)", true, 21),
            ("Normal Character Movement (frFR)", true, 20),
            ("Maps/VMaps/MMaps Extracted", true, 18),
            ("Phase 4 World Playability / Content", false, 0),
            ("enUS client pack re-verify", false, 0),
        ];
        for (var i = 0; i < milestoneData.Length; i++)
        {
            var (title, done, daysAgo) = milestoneData[i];
            milestones.Add(new Milestone
            {
                ProjectId = project.Id,
                Title = title,
                IsCompleted = done,
                CompletedAt = done ? now.AddDays(-daysAgo) : null,
                SortOrder = i,
            });
        }

        // --- Frontiers ---
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "frFR movement + maps baseline (Phase 3 done)",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-22),
            CompletedAt = now.AddDays(-20),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Phase 4: world playability and content fixes",
            Status = FrontierStatus.Current,
            Notes = "Batch 0 filled missing quest reward item_template rows. " +
                    "Gameplay baseline smoke test added. Client must stay frFR " +
                    "(enUS WTF on this pack crashes error #134 at world load). " +
                    "Portal: Tailscale 100.111.220.11.",
            CreatedAt = now.AddDays(-20),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "enUS text/speech pack or dedicated enUS client",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "In-world movement not working — unknown encrypted client opcodes",
            IsResolved = true,
            Resolution = "Phase 3 complete on frFR locale pack; movement worked after locale/client baseline (not opcode-alias table from incomplete matrix)",
            CreatedAt = now.AddDays(-22),
            ResolvedAt = now.AddDays(-20),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Forcing enUS on frFR Firestorm pack crashes world load (AuctionHouse.db2 E_NOT_AVAILABLE, error #134)",
            CreatedAt = now.AddDays(-20),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (28, TimelineCategory.Build, "Clean 100% build — authserver + worldserver (build-fixes patch)"),
            (26, TimelineCategory.Milestone, "Milestone completed: authserver Operational"),
            (25, TimelineCategory.Build, "worldserver reaches World initialized"),
            (24, TimelineCategory.Milestone, "Milestone completed: Databases Imported (Firestorm WoD)"),
            (22, TimelineCategory.Frontier, "Client connects, logs in, enters world"),
            (21, TimelineCategory.Milestone, "Milestone completed: In-World Render (Stormwind)"),
            (20, TimelineCategory.Milestone, "Milestone completed: Normal Character Movement (frFR)"),
            (20, TimelineCategory.Blocker, "enUS locale on this pack crashes at load — stay frFR"),
            (18, TimelineCategory.Milestone, "Milestone completed: Maps/VMaps/MMaps Extracted"),
            (20, TimelineCategory.Frontier, "Advanced to Phase 4 world playability"),
            (15, TimelineCategory.Code, "Batch 0: missing quest reward item_template FK fix"),
            (1, TimelineCategory.Code, "Add WoD 20779 gameplay baseline smoke test"),
        ];
        foreach (var (daysAgo, category, message) in timelineData)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Timestamp = now.AddDays(-daysAgo),
                Category = category,
                Message = message,
            });
        }

        // --- Notes ---
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Hard constraints",
            Content =
                """
                # Hard constraints still in force

                - WoD **6.2.3 build 20779** only
                - Use **frFR** locale (EU pack); enUS WTF → error #134
                - Draenor MariaDB on **port 3307** (never 3306 — that's cata)
                - Do not touch `cata-devbox` or `/var/home/blizz/cata-server/`
                - PCH stays ON; prefer `make -j2` on low-RAM boxes
                - No bots/Playerbots/AHBots this phase
                - Portal: Tailscale `100.111.220.11`

                Full live snapshot: `STATUS.md` + `docs/CURRENT_STATUS.md`
                """,
            CreatedAt = now.AddDays(-20),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Build fix catalogue (summary)",
            Content =
                """
                # Build fixes (modern Linux)

                Upstream targets Windows/VS + OpenSSL 1.x. On GCC 13 / OpenSSL 3
                / ACE 7.1.2 / Boost 1.83:

                1. OpenSSL 3 RC4 → EVP API
                2. ACE 7.x API drift
                3. Boost 1.83 filesystem v3
                4. GCC 13 `-Werror` relaxations (known-safe)
                5. PCH on; `-j2` to avoid OOM on large PCH TUs

                Full set: `draenor-core-build-fixes.patch` (23 files).
                """,
            CreatedAt = now.AddDays(-26),
        });
    }
}