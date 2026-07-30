using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Dark Sun Online: Crimson Sands preservation project.
/// Phases 1–4 (research through protocol deep-dive) are complete; next is
/// first live packet capture once TEN can launch mdark.exe.
/// </summary>
public static class CrimsonSandsSeeder
{
    public const string ProjectName = "Crimson Sands Server Revival";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Preservation and server revival for Dark Sun Online: Crimson Sands (SSI, 1996), " +
                          "a TEN-exclusive graphical MMO. Depends on ten-revival for the TEN protocol layer; " +
                          "this repo owns DSO-specific services, formats, and the unmodified Win95 client path.",
            RepositoryPath = "/home/blizz/Projects/dso-crimson-sands",
            BuildCommand = "ls docs/experiments/",
            RunCommand = "ls service/ analysis/ 2>/dev/null; head -40 README.md",
            TestCommand = "test -f docs/experiments/exp-001-first-connection.md && echo ok",
            DocumentationPaths =
            [
                "README.md",
                "docs/",
                "docs/experiments/",
            ],
            AiContextPaths =
            [
                "README.md",
                "docs/experiments/exp-001-first-connection.md",
                "CLAUDE.md",
            ],
            Tags = ["Game Preservation", "Server Revival", "Reverse Engineering", "MMO", "D&D", "TEN"],
            ActiveTask = "Blocked on TEN P10: first packet capture EXP-001 once mdark.exe launches",
            CurrentObjective = "Original unmodified Windows 95 client connecting to reconstructed DSO services",
            CreatedAt = now.AddDays(-90),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Phase 1 — Research & inventory", true, 80),
            ("Phase 2 — CD recovery / extraction", true, 60),
            ("Phase 3 — Binary analysis", true, 40),
            ("Phase 4 — Protocol/format deep-dive", true, 29),
            ("EXP-001 — First live packet capture", false, 0),
            ("Character select → starting zone", false, 0),
            ("Combat / basic play loop", false, 0),
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
            Title = "Phases 1–4 complete (research → protocol postmortem)",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-90),
            CompletedAt = now.AddDays(-29),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "EXP-001: first connection packet capture",
            Status = FrontierStatus.Current,
            Notes = "Waiting on ten-revival P10 (cgs-darksun resolve → mdark.exe). " +
                    "Duke-vs-DSO comparison already found launch-handoff root cause (2026-07-01). " +
                    "DSO does not fork ten-revival — it depends on it.",
            CreatedAt = now.AddDays(-29),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "DSO game-service adapter after first capture",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Launch handoff root cause unknown — TEN client never reached mdark.exe",
            IsResolved = true,
            Resolution = "Duke-vs-DSO comparison (launch-handoff spec §8): root cause found; TEN-side P9/P10 address ENTER + cgs-darksun resolve",
            CreatedAt = now.AddDays(-40),
            ResolvedAt = now.AddDays(-29),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Cannot capture DSO game traffic until TEN launches mdark.exe (depends on ten-revival P10 VM verify)",
            CreatedAt = now.AddDays(-16),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (90, TimelineCategory.General, "DSO: Crimson Sands preservation workspace opened"),
            (80, TimelineCategory.Milestone, "Milestone completed: Phase 1 research & inventory"),
            (60, TimelineCategory.Milestone, "Milestone completed: Phase 2 CD recovery/extraction"),
            (40, TimelineCategory.Milestone, "Milestone completed: Phase 3 binary analysis"),
            (29, TimelineCategory.Milestone, "Milestone completed: Phase 4 protocol/format deep-dive"),
            (29, TimelineCategory.Code, "Launch-handoff root cause via Duke-vs-DSO comparison"),
            (29, TimelineCategory.Frontier, "Advanced to EXP-001 first packet capture"),
            (16, TimelineCategory.Blocker, "Parked on TEN P10 mdark.exe launch"),
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
            Title = "Relationship to TEN Revival",
            Content =
                """
                # Architecture

                ```
                ten-revival/          ← TEN protocol (auth, lobby, matchmaking)
                    └── Duke3D works   ← proves TEN layer
                dso-crimson-sands/    ← DSO-specific game services
                    └── depends on TEN core (does not fork it)
                ```

                Target: original unmodified Windows 95 client connecting to
                reconstructed services. Prefer protocol compatibility and
                clean-room implementations; mark every assumption.

                Next milestone doc: `docs/experiments/exp-001-first-connection.md`
                """,
            CreatedAt = now.AddDays(-29),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Philosophy",
            Content =
                """
                # Preservation-first, not a remake

                - Prefer original clients
                - Prefer original network behavior
                - Prefer protocol compatibility
                - Every verified fact includes its source
                - Never invent information

                Parent status lives in ten-revival `STATUS.md` (Dark Sun focus).
                """,
            CreatedAt = now.AddDays(-60),
        });
    }
}
