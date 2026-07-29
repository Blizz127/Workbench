using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Parasite Eve decompilation sample project — parked at disc
/// verification phase, the earliest stage of a decomp project. Minimal
/// progress data reflects a real project that hasn't started decompiling yet.
/// </summary>
public static class ParasiteEveSeeder
{
    public const string ProjectName = "Parasite Eve Decompilation";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Waiting,
            Description = "Early-phase decompilation of Parasite Eve (PSX). " +
                          "Currently parked at disc verification; the retail binary is identified " +
                          "and its structure is catalogued, but decompilation hasn't started yet.",
            RepositoryPath = "/home/blizz/Projects/Parasite-Eve-Decompilation",
            BuildCommand = "python3 tools/verify_disc.py --disc SLUS_006.62",
            RunCommand = "python3 tools/verify_disc.py --disc SLUS_006.62 --verbose",
            TestCommand = "python3 tools/check_sector_hashes.py",
            DocumentationPaths = ["docs/disc-layout.md", "docs/executable-map.md"],
            AiContextPaths = ["docs/disc-layout.md", "notes/psx-executable-format.md"],
            Tags = ["Reverse Engineering", "Game Preservation", "PSX", "Parked"],
            ActiveTask = null,
            CurrentObjective = "Complete disc verification and select the first function to decompile",
            CreatedAt = now.AddDays(-45),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Acquire Retail Disc Image", true, 40),
            ("Verify Disc Sector Hashes", true, 5),
            ("Map Executable Sections", false, 0),
            ("First Function Decompiled", false, 0),
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
            Title = "Verify SLUS_006.62 disc image against redump.org hashes",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-42),
            CompletedAt = now.AddDays(-4),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Map PSX executable sections (TEXT, DATA, RODATA, BSS)",
            Status = FrontierStatus.Current,
            Notes = "Executable identified at sector 24. Header parsed; need to walk the section table and annotate addresses.",
            CreatedAt = now.AddDays(-3),
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Disc image SHA-1 didn't match redump — wrong region dump",
            IsResolved = true,
            Resolution = "Switched to NTSC-U (SLUS_006.62); redump SHA-1 matches",
            CreatedAt = now.AddDays(-40),
            ResolvedAt = now.AddDays(-38),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (45, TimelineCategory.General, "Project registered — Parasite Eve decomp started"),
            (40, TimelineCategory.Blocker, "Disc SHA-1 mismatch — wrong region"),
            (38, TimelineCategory.Blocker, "Resolved — switched to NTSC-U SLUS_006.62"),
            (38, TimelineCategory.Milestone, "Milestone completed: Acquire Retail Disc Image"),
            (7, TimelineCategory.Code, "Wrote disc verification tool"),
            (5, TimelineCategory.Milestone, "Milestone completed: Verify Disc Sector Hashes"),
            (4, TimelineCategory.Frontier, "Completed disc sector verification"),
            (3, TimelineCategory.Frontier, "Started mapping executable sections"),
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
            Title = "Disc layout reference",
            Content =
                """
                # Parasite Eve Disc Layout (SLUS_006.62)

                | Sector | Contents |
                |--------|----------|
                | 0–15   | ISO9660 filesystem header |
                | 16–23  | SYSTEM.CNF + boot files |
                | 24+    | SLUS_006.62 executable |
                | ...    | STR/, MOV/, BGM/ data |

                The main executable is ~2.1 MB. TEXT section starts at
                offset 0x800 within the file, DATA at 0x13A400.

                See `docs/disc-layout.md` for the full map.
                """,
            CreatedAt = now.AddDays(-6),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Why parked?",
            Content =
                """
                # Status: Parked

                This project is parked because the current focus is on
                Xenogears decompilation. Parasite Eve was verified as a
                known-good target — the disc matches, the executable is
                understood — but active decomp hasn't started yet.

                When ready to resume:
                1. Pick a small, self-contained function (e.g. boot init)
                2. Set up the matching build toolchain
                3. Claim the first frontier
                """,
            CreatedAt = now.AddDays(-2),
        });
    }
}
