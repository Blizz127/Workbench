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
            Description = "Decompilation of Xenogears (PSX) toward a native PC port. " +
                          "Matching functions are verified against the retail binary.",
            RepositoryPath = "/home/blizz/Projects/xenogears-decomp",
            BuildCommand = "make -j8",
            RunCommand = "./build/xenogears",
            TestCommand = "python3 tools/check_matches.py",
            DocumentationPaths = ["docs/", "notes/memory-map.md"],
            AiContextPaths = ["docs/decomp-guide.md", "include/"],
            Tags = ["Reverse Engineering", "Game Preservation", "PSX"],
            ActiveTask = "Decompile the animation script interpreter",
            CurrentObjective = "Reach a stable in-game field scene running natively",
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
            CreatedAt = now.AddDays(-120),
        };
        projects.Add(project);

        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Kernel Menu", true, 100),
            ("Field Loading", true, 70),
            ("Render Crash Fixed", true, 21),
            ("Animation Script System", false, 0),
            ("First Playable Scene", false, 0),
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
            Title = "func_800248D4 — opcode dispatch table",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-9),
            CompletedAt = now.AddDays(-2),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "func_8001FBE4 — animation script interpreter",
            Status = FrontierStatus.Current,
            Notes = "Roughly 40% matched. The jump table at 0x8001FC90 still resists.",
            CreatedAt = now.AddDays(-2),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Field actor state machine",
            Status = FrontierStatus.Next,
            CreatedAt = now.AddDays(-2),
        });

        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Render crash when loading Lahan village",
            IsResolved = true,
            Resolution = "TIM upload used a stale VRAM page pointer; fixed in gpu_upload.c",
            CreatedAt = now.AddDays(-30),
            ResolvedAt = now.AddDays(-21),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Animation opcode 0xB7 semantics unknown — behaves differently in battle scenes",
            CreatedAt = now.AddDays(-3),
        });

        (int DaysAgo, TimelineCategory Category, string Message)[] entries =
        [
            (30, TimelineCategory.Blocker, "Render crash when loading Lahan village"),
            (23, TimelineCategory.Build, "Build succeeded"),
            (21, TimelineCategory.Blocker, "Render crash resolved"),
            (21, TimelineCategory.Milestone, "Milestone completed: Render Crash Fixed"),
            (9, TimelineCategory.Frontier, "Started func_800248D4 opcode dispatch table"),
            (5, TimelineCategory.Code, "Implemented func_800248D4 opcode 0xB3"),
            (2, TimelineCategory.Frontier, "Advanced to func_8001FBE4"),
            (1, TimelineCategory.Build, "Build timeout after 10 seconds"),
            (0, TimelineCategory.Build, "Build succeeded"),
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
            Title = "Animation opcode notes",
            Content =
                """
                # Animation Script Opcodes

                | Opcode | Meaning | Status |
                |--------|---------|--------|
                | 0xB3   | set frame delay | matched |
                | 0xB5   | loop begin | matched |
                | 0xB7   | ??? battle-only behavior | **open** |

                The interpreter lives at `func_8001FBE4`. The dispatch table at
                `0x8001FC90` is indexed by `opcode - 0xB0`.
                """,
            CreatedAt = now.AddDays(-4),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Memory map cheatsheet",
            Content =
                """
                # Memory Map

                - `0x80010000` — kernel
                - `0x8001F000` — animation system
                - `0x80024000` — field opcode dispatch

                See `notes/memory-map.md` in the repo for the full table.
                """,
            CreatedAt = now.AddDays(-15),
        });

        builds.Add(new BuildRecord
        {
            ProjectId = project.Id,
            Kind = ExecutionKind.Build,
            Command = "make -j8",
            StartedAt = now.AddDays(-1),
            ExitCode = null,
            Duration = TimeSpan.FromSeconds(10),
            OutputLog = "[workbench] command timed out\ncc1: internal compiler error in gimplify_expr",
        });
        builds.Add(new BuildRecord
        {
            ProjectId = project.Id,
            Kind = ExecutionKind.Build,
            Command = "make -j8",
            StartedAt = now.AddHours(-3),
            ExitCode = 0,
            Duration = TimeSpan.FromSeconds(42),
            OutputLog = "CC src/field/anim.c\nLD build/xenogears\nBuild complete.",
        });

        tests.Add(new TestRecord
        {
            ProjectId = project.Id,
            Command = "python3 tools/check_matches.py",
            Result = TestResult.Passed,
            StartedAt = now.AddHours(-2),
            Duration = TimeSpan.FromSeconds(12),
            Notes = "1042/1893 functions matching (55.0%)",
        });
    }
}
