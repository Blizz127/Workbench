using System.Linq;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Parasite Eve decompilation project — active matching decomp
/// (Phase 5, 224 exact C leaves as of 2026-07-30). Retail target is
/// NTSC-U SLUS-006.62; progress dashboard at parasite-eve-progress.
/// </summary>
public static class ParasiteEveSeeder
{
    public const string ProjectName = "Parasite Eve Decompilation";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes,
        VisualTestRepository visualTests, DateTimeOffset now)
    {
        // Idempotent: if the project already exists, don't double-seed.
        if (projects.GetAll().Any(p => p.Name == ProjectName))
            return;

        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Matching decompilation of Parasite Eve (PSX, NTSC-U SLUS-006.62). " +
                          "Phase 5 leaf matching with dual toolchain (GCC 14.2 + era cc1/maspsx). " +
                          "224 exact C leaves; exact SHA-1 rebuild via scripts/build_us.sh. " +
                          "PC port is out of scope.",
            RepositoryPath = "/home/blizz/Projects/Parasite-Eve-Decompilation",
            BuildCommand = "scripts/build_us.sh",
            RunCommand = "scripts/verify_us.sh",
            TestCommand = "scripts/verify_us.sh",
            DocumentationPaths =
            [
                "docs/project_plan.md",
                "docs/ai_context/ACTIVE_HANDOFF.md",
                "docs/splitting.md",
                "docs/disc_info.md",
            ],
            AiContextPaths =
            [
                "docs/ai_context/ACTIVE_HANDOFF.md",
                "docs/ai_context/parked_blockers.json",
                "CLAUDE.md",
                "configs/USA/disc1.yaml",
            ],
            Tags = ["Reverse Engineering", "Game Preservation", "PSX", "Matching Decomp"],
            ActiveTask = "Extend MASPSX three-word symbol loads; pick next volume leaf after 2F970/2F9CC table twins",
            CurrentObjective = "Climb main's call chain and keep exact-match leaf count growing past 224",
            CreatedAt = now.AddDays(-90),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Disc image + extract pipeline", true, 85),
            ("Exact asm rebuild harness (Phase 4)", true, 60),
            ("First matching C leaf (Phase 4J)", true, 55),
            ("Era dual-toolchain (cc1 + maspsx)", true, 40),
            ("Boot Rung 1: main → 3E680 chain matched", true, 5),
            ("1000 matching C leaves", false, 0),
            ("Full game exact match", false, 0),
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
            Title = "func_8002F970 — slot-table pointer-match clear (leaf 224)",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-2),
            CompletedAt = now.AddDays(-1),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "MASPSX three-word symbol loads + next volume leaf after table twins",
            Status = FrontierStatus.Current,
            Notes = "phase5ff extends MASPSX_THREE_WORD_SYMBOL_STORE to loads. " +
                    "Table twins 2F9CC/2F970 matched with SlotRecord typing + three-word knob. " +
                    "Parked: func_800698D4 (dbr_sched), func_8006E834 (register-home), func_80055724 (scheduling).",
            CreatedAt = now.AddDays(-1),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "func_8003F3C4 — next main-chain candidate after parked mount leaves",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "ASPSX delay-slot sw vs pre-jr+nop shapes blocked era matching",
            IsResolved = true,
            Resolution = "Vendored maspsx LOCAL PATCH MASPSX_FILL_STORE_DELAY_SLOT=1 (opt-in per leaf); 14/14 delay-slot sw family closed in 5EF",
            CreatedAt = now.AddDays(-35),
            ResolvedAt = now.AddDays(-25),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "ccpsx-vs-2.7.2 skew: dbr_sched $v0-liveness (698D4) and call-result register-home (6E834) — not source-expressible",
            CreatedAt = now.AddDays(-3),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (90, TimelineCategory.General, "Project registered — Parasite Eve matching decomp"),
            (60, TimelineCategory.Milestone, "Milestone completed: Exact asm rebuild harness"),
            (55, TimelineCategory.Milestone, "Milestone completed: First matching C leaf"),
            (40, TimelineCategory.Code, "Era dual-toolchain online (cc1 + maspsx 2.21)"),
            (25, TimelineCategory.Blocker, "Resolved: maspsx delay-slot sw fill (5EF family closed)"),
            (10, TimelineCategory.Code, "MASPSX_THREE_WORD_SYMBOL_STORE opt-in landed"),
            (5, TimelineCategory.Milestone, "Milestone completed: Boot Rung 1 (main→3E680)"),
            (4, TimelineCategory.Frontier, "Parked func_800698D4 / func_8006E834 (ccpsx skew)"),
            (2, TimelineCategory.Code, "Matched func_8002F9CC — table clear, leaf 223"),
            (1, TimelineCategory.Code, "Matched func_8002F970 — pointer-match clear, leaf 224"),
            (0, TimelineCategory.Code, "phase5ff: extend three-word symbol store to loads"),
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
            Title = "How to count progress",
            Content =
                """
                # Progress counting (do not hand-maintain)

                ```bash
                grep -c ',\s*c,' configs/USA/disc1.yaml   # C leaves (source of truth)
                grep -c ',\s*asm\]' configs/USA/disc1.yaml # yaml asm segments (NOT fn count)
                scripts/verify_us.sh                       # phase label + exact rebuild
                ```

                Yaml `asm` segments are not remaining functions — one segment can
                hold dozens of glabels. Never count `asm/disc1/*.s` from disk
                (git-ignored; orphans and pads).

                Progress dashboard: https://blizz127.github.io/parasite-eve-progress/
                Target SHA-1: `452fb033f2eaa4b18aa20a5bca60b8125af3a37b`
                """,
            CreatedAt = now.AddDays(-5),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Era toolchain rules",
            Content =
                """
                # Era dual-toolchain

                - Default leaves: GCC 14.2 in Distrobox `pe-mipsel`
                - Era leaves: `era_compile` = cpp → cc1 → maspsx → GNU as
                  (typically `-O2 -G0`; some leaves `-O1 -G0` or +`-fschedule-insns2`)
                - Maspsx: `ERA_ASPSX_VER=2.21` + `--dont-expand-li`
                - Opt-in env per leaf:
                  - `MASPSX_FILL_STORE_DELAY_SLOT=1` — sw in `j $31` delay
                  - `MASPSX_THREE_WORD_SYMBOL_STORE=1` — 3-word indexed symbol store/load

                Proven levers: `-O1` for per-use constant materialization;
                aggregate element type for addressing mode; sched2 for
                store-adjacent `li` placement.

                See `docs/ai_context/ACTIVE_HANDOFF.md` for the full fingerprint table.
                """,
            CreatedAt = now.AddDays(-2),
        });

        // --- Visual test profiles ---
        var repoPath = project.RepositoryPath ?? "/home/blizz/dev/parasite-eve";
        var retailExe = "build/extracted/disc1/SLUS_006.62";
        var rebuiltExe = "build/disc1.candidate.exe";
        var emuBin = "/home/blizz/apps/pcsx-redux/build293-3e10093a/PCSX-Redux-build293-3e10093a.AppImage";

        AddVisualTestProfile(visualTests, project.Id, now, "pe-retail-boot", "PE Retail Reference — Boot",
            VisualTestLaunchKind.Emulator, VisualTestArtifactKind.RetailReference,
            $"xvfb-run -a {emuBin} -run -interlaced {retailExe}",
            "PCSX-Redux", "PCSX-Redux", "parasite-eve/retail-boot",
            [("boot", 8, null, "01_boot.png", "Title screen or boot logo")]);

        AddVisualTestProfile(visualTests, project.Id, now, "pe-matching-boot", "PE Matching Build — Boot",
            VisualTestLaunchKind.Emulator, VisualTestArtifactKind.MatchingBuild,
            $"xvfb-run -a {emuBin} -run -interlaced {rebuiltExe}",
            "PCSX-Redux", "PCSX-Redux", "parasite-eve/matching-boot",
            [("boot", 8, null, "01_boot.png", "Must match retail reference")],
            "scripts/build_us.sh");

        AddVisualTestProfile(visualTests, project.Id, now, "pe-native-boot", "PE Native Port — Boot",
            VisualTestLaunchKind.Unavailable, VisualTestArtifactKind.NativePort,
            "(not yet built)",
            "parasite_eve_port", "Parasite Eve", "parasite-eve/native-boot",
            [("boot", 5, null, "01_boot.png", "Must match retail reference")]);

        AddVisualTestProfile(visualTests, project.Id, now, "pe-native-intro", "PE Native Port — Intro",
            VisualTestLaunchKind.Unavailable, VisualTestArtifactKind.NativePort,
            "(not yet built)",
            "parasite_eve_port", "Parasite Eve", "parasite-eve/native-intro",
            [("boot", 5, null, "01_boot.png", "Boot screen"),
             ("intro", 15, "Return", "02_intro.png", "Intro cinematic or menu")]);
    }

    private static void AddVisualTestProfile(VisualTestRepository repo, long projectId,
        DateTimeOffset now, string stableId, string name,
        VisualTestLaunchKind launchKind, VisualTestArtifactKind artifactKind,
        string launchCommand, string processName, string? windowTitlePattern,
        string screenshotFolder,
        (string name, int delay, string? input, string filename, string? notes)[] checkpoints,
        string? buildCommand = null)
    {
        var profile = new VisualTestProfile
        {
            ProjectId = projectId, StableId = stableId, Name = name,
            LaunchKind = launchKind, ArtifactKind = artifactKind,
            BuildCommand = buildCommand, LaunchCommand = launchCommand,
            Display = ":10.0", StartupTimeoutSeconds = 30,
            ProcessName = processName, WindowTitlePattern = windowTitlePattern,
            ScreenshotFolder = screenshotFolder,
            RetainLastRuns = 5, StopAfterCapture = true,
            CreatedAt = now, UpdatedAt = now,
        };
        repo.AddProfile(profile);

        for (int i = 0; i < checkpoints.Length; i++)
        {
            var (cpName, delay, input, filename, notes) = checkpoints[i];
            repo.AddCaptureCheckpoint(new VisualCaptureCheckpoint
            {
                ProfileId = profile.Id,
                SortOrder = i,
                Name = cpName,
                DelayAfterSeconds = delay,
                InputAction = input,
                ScreenshotFilename = filename,
                WindowOnly = true,
                Required = i == 0,
                Notes = notes,
            });
        }
    }

}
