using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Draenor-Core build & runtime project — a WoW: Warlords of Draenor
/// (6.2.3, build 20779) server preservation effort. Tracks the modern-Linux
/// build fixes, database import, and the remaining in-world movement blocker.
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
            Description = "Build tracking and fix documentation for getting " +
                          "EyalMK/Draenor-Core (World of Warcraft: Warlords of Draenor, " +
                          "patch 6.2.3, build 20779) to configure and compile on a modern " +
                          "Linux toolchain — GCC 13 / OpenSSL 3.x / ACE 7.1.2 / Boost 1.83.",
            RepositoryPath = "/home/blizz/Projects/draenor-dit",
            BuildCommand = "cd /home/blizz/Draenor-Core/build && cmake --build . --target worldserver -j2",
            RunCommand = "podman exec draenor-devbox bash -lc 'cd /home/blizz/Draenor-Core/run-world && rm -f console.fifo && mkfifo console.fifo && exec 9<>console.fifo && exec ./worldserver < console.fifo >> worldserver-live.log 2>&1'",
            TestCommand = "podman exec draenor-devbox bash -lc 'ps -eo pid,ppid,stat,comm,args | grep \"[.]\\/worldserver\" || true; ss -ltnp 2>/dev/null | grep 8085 || true; tail -40 /home/blizz/Draenor-Core/run-world/worldserver-live.log'",
            DocumentationPaths = ["BUILD.md", "PRESERVATION.md", "STATUS.md", "PHASE1-REPORT.md", "docs/protocol-compatibility-20779.md", "runtime/"],
            AiContextPaths = ["BUILD.md", "PRESERVATION.md", "STATUS.md", "PHASE1-REPORT.md", "docs/protocol-compatibility-20779.md", "draenor-core-build-fixes.patch", "runtime/start_servers.sh", "runtime/extract_maps.sh", "runtime/db/"],
            Tags = ["Game Preservation", "Server Revival", "Reverse Engineering", "WoW", "WoD", "Build Engineering"],
            ActiveTask = "Resolve in-world movement: map unknown client opcodes to real-input actions",
            CurrentObjective = "Get a 6.2.3.20779 client to connect, login, enter world, render, and move normally",
            CreatedAt = now.AddDays(-8),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Modern Linux Build (GCC 13 / OpenSSL 3)", true, 7),
            ("authserver Operational", true, 6),
            ("worldserver Boots to World Init", true, 5),
            ("Databases Imported (auth/char/world/hotfix/web)", true, 4),
            ("Client Connect + Login + Enter World", true, 3),
            ("In-World Render (Stormwind)", true, 2),
            ("Normal Character Movement", false, 0),
            ("Maps/VMaps/MMaps Extracted", false, 0),
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
            Title = "Build fixes patch (23 files) applied — 100% build",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-7),
            CompletedAt = now.AddDays(-6),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "In-world movement: unknown opcode → action mapping",
            Status = FrontierStatus.Current,
            Notes = "Unknown-opcode instrumentation active in WorldSocket.cpp. Need a real-input movement matrix that produces action-correlated packets before aliasing.",
            CreatedAt = now.AddDays(-2),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Map/VMAP/MMAP extraction against WoD 6.2.3 client",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "World/characters DB not populated — worldserver aborts at world-DB init",
            IsResolved = true,
            Resolution = "Imported canonical Firestorm WoD SQL sets into Draenor's own MariaDB on port 3307 (auth/characters/world/hotfix/web).",
            CreatedAt = now.AddDays(-6),
            ResolvedAt = now.AddDays(-4),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "In-world movement not working — unknown encrypted client opcodes (0x0534, 0x05EF, 0x0434, 0x04CD, 0x09A9, 0x10E3, 0x111A, 0x006C) not mapped to actions",
            CreatedAt = now.AddDays(-2),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (7, TimelineCategory.Code, "Applied draenor-core-build-fixes.patch (23 files)"),
            (7, TimelineCategory.Build, "Clean 100% build — authserver + worldserver produced"),
            (6, TimelineCategory.Blocker, "World DB missing — worldserver aborts at init"),
            (6, TimelineCategory.Build, "Reconstructed auth schema from prepared statements"),
            (5, TimelineCategory.Milestone, "Milestone completed: authserver Operational"),
            (5, TimelineCategory.Build, "worldserver reaches World initialized"),
            (4, TimelineCategory.Blocker, "Imported canonical Firestorm WoD databases"),
            (4, TimelineCategory.Milestone, "Milestone completed: Databases Imported"),
            (3, TimelineCategory.Frontier, "Client connects, logs in, enters world"),
            (2, TimelineCategory.Milestone, "Milestone completed: In-World Render (Stormwind)"),
            (2, TimelineCategory.Frontier, "Advanced to in-world movement mapping"),
            (2, TimelineCategory.Blocker, "Movement blocked — unknown opcodes unaliased"),
            (1, TimelineCategory.Code, "Added PROBE_UNKNOWN_CLIENT_OPCODE instrumentation to WorldSocket.cpp"),
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
            Title = "Build fix catalogue (12 categories)",
            Content =
                """
                # Build Fix Catalogue

                The upstream Draenor-Core targets Windows/VS + OpenSSL 1.x. On
                modern Linux (GCC 13 / OpenSSL 3 / ACE 7.1.2 / Boost 1.83) the
                following root causes needed fixes:

                1. **OpenSSL 3 RC4** — `ARC4.cpp` crashed because RC4 is not in
                   the default provider. Replaced with EVP API.
                2. **ACE 7.x API drift** — several `ACE_*` calls changed
                   signatures; updated to 7.1.2.
                3. **Boost 1.83** — `boost::filesystem` v3 is mandatory; removed
                   v2 shims.
                4. **GCC 13 strictness** — `-Werror` on new warnings; relaxed a
                   handful of known-safe cases.
                5. **PCH stays ON** — do NOT pass `-DNOPCH=1`.
                6. **Parallelism** — use `make -j2` on low-RAM boxes; `-j4`
                   OOM-kills `cc1plus` on largest PCH TUs.

                Full set: `draenor-core-build-fixes.patch` (23 files).
                """,
            CreatedAt = now.AddDays(-6),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Movement matrix protocol",
            Content =
                """
                # Movement Matrix (required before aliasing)

                Before each manual client action, append a marker to
                `worldserver-live.log`, then extract only the marked run:

                ```
                grep -E "MATRIX|PROBE_UNKNOWN_CLIENT_OPCODE|UNKNOWN OPCODE" \
                  /home/blizz/Draenor-Core/run-world/worldserver-live.log | tail -300
                ```

                Required actions:
                1. idle 10s  2. W down 2s  3. W release  4. S down 2s
                5. S release  6. A/D turn  7. right-click camera  8. jump
                9. click guard/NPC  10. ESC/menu

                Record: opcode, packet size, first 32 payload bytes, count,
                repeats-while-held, appears-on-release, position before/after.

                **Do NOT add movement opcode aliases until a real-input matrix
                produces action-correlated evidence.**
                """,
            CreatedAt = now.AddDays(-2),
        });
    }
}
