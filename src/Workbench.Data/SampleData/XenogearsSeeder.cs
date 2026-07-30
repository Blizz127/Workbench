using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Xenogears decompilation sample project on first run so a fresh
/// install demonstrates every subsystem: milestones, frontiers, blockers,
/// timeline, notes, and build/test history.
/// </summary>
public static class XenogearsSeeder
{
    public const string ProjectName = "Xenogears Decompilation";

    /// <summary>Legacy entry point kept for existing callers; delegates to <see cref="Seed"/>.</summary>
    public static void SeedIfEmpty(WorkbenchDatabase database)
    {
        var projects = new ProjectRepository(database);
        if (projects.GetAll().Count > 0)
            return;

        var now = DateTimeOffset.Now;
        Seed(
            projects,
            new MilestoneRepository(database),
            new FrontierRepository(database),
            new BlockerRepository(database),
            new TimelineRepository(database),
            new NoteRepository(database),
            new BuildHistoryRepository(database),
            new TestHistoryRepository(database),
            now);
    }

    public static void Seed(
        ProjectRepository projects,
        MilestoneRepository milestones,
        FrontierRepository frontiers,
        BlockerRepository blockers,
        TimelineRepository timeline,
        NoteRepository notes,
        BuildHistoryRepository builds,
        TestHistoryRepository tests,
        DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Matching decompilation of Xenogears (PSX, SLUS-006.64) plus a " +
                          "native PC port under pc_port/. Field load path and menu nav arc " +
                          "are largely working; current pressure is red ROM pins vs port work.",
            RepositoryPath = "/home/blizz/Projects/xenogears-decomp",
            BuildCommand = "./pc_port/build_port.sh",
            RunCommand = "./build/xenogears",
            TestCommand = "make rom-check",
            DocumentationPaths = ["docs/", "docs/wiki/", "docs/ai_context/"],
            AiContextPaths =
            [
                "docs/ai_context/ACTIVE_HANDOFF.md",
                "docs/wiki/Active-Frontier.md",
                "docs/OPEN_ISSUES.md",
                "include/",
            ],
            Tags = ["Reverse Engineering", "Game Preservation", "PSX", "PC Port"],
            ActiveTask = "Lahan field scope: actor movement opcode 0x4B + repair four red rom-check pins",
            CurrentObjective = "Stable Lahan field scene + decide matching-vs-nonmatching source policy for red pins",
            PromptRules =
            [
                "Never modify original decomp source under asm/ or src/ without matching evidence.",
                "Confirm matches via the matching-build command before claiming a function is done.",
                "Prefer minimal diffs; the PC port lives entirely under pc_port/."
            ],
            CustomLogPatterns =
            [
                "RUN_RC=|RUN_RC=|1|TestHarness",
                "[stub]|\\[stub\\]|1|TestHarness",
                "ParsePrimitive|\\bParsePrimitive\\b|1|FieldEngine",
                "field-diag|field-diag|1|FieldEngine"
            ],
            CreatedAt = now.AddDays(-180),
        };
        projects.Add(project);

        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Kernel Menu", true, 150),
            ("Field Loading", true, 100),
            ("Map1 → Map15 reload path", true, 22),
            ("Menu nav arc N2 (Items) complete", true, 10),
            ("Lahan field playable scope", false, 0),
            ("Four red rom-check pins repaired", false, 0),
            ("Battle Transition", false, 0),
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

        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Menu nav N2c-4 — Items functionally complete",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-20),
            CompletedAt = now.AddDays(-10),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Lahan field: opcode 0x4B actor movement + matrix/camera fixes",
            Status = FrontierStatus.Current,
            Notes = "Recent: field actor matrix composition, opcode 0x4B movement, camera translation for actor sprites, measured retail party roster seed. " +
                    "Parallel debt: four red rom-check pins (slus_006.64, field.bin, member_change_menu.bin, shop_menu.bin).",
            CreatedAt = now.AddDays(-4),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Decide matching-vs-nonmatching policy; repair MenuExecute / red pins",
            Status = FrontierStatus.Next,
            CreatedAt = now.AddDays(-4),
        });

        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Matching ELF build failed on unresolved inline gte_* helpers",
            IsResolved = true,
            Resolution = "include/gte_macros.inc + labels.inc tracked; clean make -B build completes all overlays (2026-07-26)",
            CreatedAt = now.AddDays(-30),
            ResolvedAt = now.AddDays(-4),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Four overlays fail make rom-check (slus / field / member_change_menu / shop_menu) — source regressions, not toolchain drift",
            CreatedAt = now.AddDays(-4),
        });

        (int DaysAgo, TimelineCategory Category, string Message)[] entries =
        [
            (30, TimelineCategory.Blocker, "Matching build blocked on unresolved gte_* helpers"),
            (22, TimelineCategory.Milestone, "Milestone completed: Map1 → Map15 reload path"),
            (10, TimelineCategory.Milestone, "Milestone completed: Menu nav arc N2 (Items)"),
            (8, TimelineCategory.Code, "port(menu): Abilities lifecycle opens/cancels clean"),
            (6, TimelineCategory.Code, "port(menu): Abilities content + labels complete for category 0"),
            (4, TimelineCategory.Blocker, "Resolved gte_* link; rom-check shows four red pins"),
            (4, TimelineCategory.Frontier, "Pivoted to Lahan field scope"),
            (3, TimelineCategory.Code, "port(field): opcode 0x4B actor-script movement"),
            (2, TimelineCategory.Code, "Fix field actor matrix column composition"),
            (0, TimelineCategory.Code, "pc: seed measured retail party roster"),
        ];
        foreach (var (daysAgo, category, message) in entries)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Timestamp = now.AddDays(-daysAgo),
                Category = category,
                Message = message,
            });
        }

        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Active frontier (July 2026)",
            Content =
                """
                # Active Frontier

                Menu nav arc complete through N2c-4 (Items). Higher priority than
                N3 submenus: **four red `make rom-check` pins** with named causes.

                | Overlay | Notes |
                |---------|-------|
                | `slus_006.64` | MenuExecute decompiled nonmatching; orphaned jump tables as `u32` — image ~7 KB short |
                | `field.bin` | First-red small; bisect clone at `Projects/f27pin/xenogears-decomp` |
                | `member_change_menu.bin` | Source regression |
                | `shop_menu.bin` | Source regression |

                Values call: decompiled-but-nonmatching (red ROM) vs matched-via-asm
                coexistence (green ROM). Do not drift into this by accident.

                Field path: Map1→Map15 reload verified; current work is Lahan
                playable scope (movement, camera, party roster).

                See `docs/wiki/Active-Frontier.md` and `docs/OPEN_ISSUES.md`.
                """,
            CreatedAt = now.AddDays(-4),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Build environments",
            Content =
                """
                # Build / run

                - **PC port:** `./pc_port/build_port.sh` (distrobox `xenogears-dev`)
                - **Matching:** `make -B build` then `make rom-check`
                - Progress site: https://decomp.dev/ladysilverberg/xenogears-decomp

                Git runs on the host (`/home/blizz/Projects/xenogears-decomp`);
                git is not on PATH inside the distrobox.
                """,
            CreatedAt = now.AddDays(-10),
        });

        builds.Add(new BuildRecord
        {
            ProjectId = project.Id,
            Kind = ExecutionKind.Build,
            Command = "./pc_port/build_port.sh",
            StartedAt = now.AddDays(-1),
            ExitCode = 0,
            Duration = TimeSpan.FromSeconds(95),
            OutputLog = "PC port build complete.\n",
        });
        builds.Add(new BuildRecord
        {
            ProjectId = project.Id,
            Kind = ExecutionKind.Build,
            Command = "make -B build",
            StartedAt = now.AddHours(-6),
            ExitCode = 0,
            Duration = TimeSpan.FromMinutes(12),
            OutputLog = "Matching build complete; overlays emitted.\n",
        });

        tests.Add(new TestRecord
        {
            ProjectId = project.Id,
            Command = "make rom-check",
            Result = TestResult.Failed,
            StartedAt = now.AddHours(-5),
            Duration = TimeSpan.FromSeconds(45),
            Notes = "Four red pins: slus_006.64, field.bin, member_change_menu.bin, shop_menu.bin",
        });
    }
}
