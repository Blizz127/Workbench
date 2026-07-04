using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Parasite Eve decompilation project — a real tracked project in
/// its earliest lifecycle stage (Phase 1: disc verification), parked behind
/// Xenogears. Unlike <see cref="XenogearsSeeder"/> (first-run demo data),
/// this seeds if the project is missing, so it appears on existing installs;
/// remove the call in App.axaml.cs if you delete it and want it to stay gone.
/// Deliberately minimal and truthful: no build/run/matching commands and no
/// fabricated history, because no disassembly or decompiled code exists yet.
/// </summary>
public static class ParasiteEveSeeder
{
    private const string ProjectName = "Parasite Eve Decompilation";

    public static void SeedIfMissing(WorkbenchDatabase database)
    {
        var projects = new ProjectRepository(database);
        if (projects.GetAll().Any(p => p.Name == ProjectName))
            return;

        // Phase 0 scaffold and the disc 1 verification pass both landed on
        // this date (Parasite-Eve-Decompilation commits 311ea1c and 8e103fc).
        var verifiedOn = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Research,
            Description = "PS1 decompilation research project for Parasite Eve " +
                          "(USA / NTSC-U, Disc 1: SLUS-00662, Disc 2: SLUS-00668). " +
                          "Phase 1 — disc verification. No disassembly, symbols, or " +
                          "decompiled code exist yet. Parked: secondary to Xenogears.",
            RepositoryPath = "/var/home/blizz/Projects/Parasite-Eve-Decompilation",
            TestCommand = "./scripts/extract_us.sh all",
            DocumentationPaths =
            [
                "docs/project_plan.md",
                "docs/disc_info.md",
                "docs/legal.md",
                "docs/reverse_engineering_notes.md",
            ],
            AiContextPaths = ["docs/ai_context/ACTIVE_HANDOFF.md", "CLAUDE.md"],
            PromptRules =
                """
                Read docs/ai_context/ACTIVE_HANDOFF.md before doing anything.
                Never invent decompiled C; no code under src/ until the checksum/rebuild harness exists (Phase 4+).
                Never claim matching progress without checksum/build verification and the exact command recorded.
                Never commit game data: no disc images, extracted files, assets, or proprietary SDK files.
                Current scope is Phase 1 (disc verification) only — no disassembly, splitting, symbols, or PC-port work.
                """,
            CustomLogPatterns =
            [
                new LogPatternDefinition("disc-verify OK", @"^OK: disc", Category: "DiscVerification"),
                new LogPatternDefinition("disc-verify SKIP", @"^SKIP:", Category: "DiscVerification"),
            ],
            Tags = ["PS1", "Decompilation", "Preservation", "Reverse Engineering", "Square", "Parasite Eve"],
            ActiveTask = "Phase 1 — disc 2 verification + redump cross-check",
            CurrentObjective = "Finish disc verification; no disassembly until Phase 2",
            CreatedAt = verifiedOn,
        };
        projects.Add(project);

        var milestones = new MilestoneRepository(database);
        (string Title, bool Done)[] milestoneData =
        [
            ("Phase 0 — repo scaffold", true),
            ("Phase 1 — disc 1 (SLUS-00662) verified & recorded", true),
            ("Phase 1 — disc 2 (SLUS-00668) verified & recorded", false),
            ("Redump cross-check for both discs", false),
            ("Phase 2 — first splat config", false),
        ];
        for (var i = 0; i < milestoneData.Length; i++)
        {
            var (title, done) = milestoneData[i];
            milestones.Add(new Milestone
            {
                ProjectId = project.Id,
                Title = title,
                IsCompleted = done,
                CompletedAt = done ? verifiedOn : null,
                SortOrder = i,
            });
        }

        var frontiers = new FrontierRepository(database);
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Disc 2 (SLUS-00668) verification pass",
            Status = FrontierStatus.Current,
            Notes = "Disc 2 image is on hand locally. Disc 1 results are recorded in " +
                    "docs/disc_info.md with the commands that produced them.",
            NextSafestStep = "Apply the fail-loudly fix to scripts/extract_us.sh (review blocker), " +
                             "re-run its failure tests, then run scripts/extract_us.sh 2 and record " +
                             "results in docs/disc_info.md.",
            CreatedAt = verifiedOn,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Redump cross-check (disc 1 page: redump.org/disc/116/)",
            Status = FrontierStatus.Next,
            CreatedAt = verifiedOn,
        });

        var blockers = new BlockerRepository(database);
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Redump cross-check pending — redump.org unreachable (ECONNREFUSED, 2026-07-04); " +
                          "local dump hashes are recorded but not yet matched against redump",
            CreatedAt = verifiedOn,
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "extract_us.sh can exit 0 with stale results when given a corrupt image " +
                          "(set -e suppressed in || context) — fix before the disc 2 pass",
            CreatedAt = verifiedOn,
        });

        var timeline = new TimelineRepository(database);
        (TimelineCategory Category, string Message)[] entries =
        [
            (TimelineCategory.Milestone, "Phase 0 scaffold committed (311ea1c): docs, ignore rules, placeholders"),
            (TimelineCategory.Milestone, "Disc 1 verification recorded (8e103fc): image + SLUS_006.62 hashes, " +
                                         "PS-X EXE header, 25-file ISO9660 listing"),
            (TimelineCategory.Blocker, "redump.org unreachable — cross-check recorded as PENDING"),
        ];
        foreach (var (category, message) in entries)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Timestamp = verifiedOn,
                Category = category,
                Message = message,
            });
        }

        var notes = new NoteRepository(database);
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Project links & status",
            Content =
                """
                # Parasite Eve Decompilation — links

                - Repository: https://github.com/Blizz127/Parasite-Eve-Decompilation
                - Local path: /var/home/blizz/Projects/Parasite-Eve-Decompilation
                - Target: USA / NTSC-U — Disc 1 `SLUS-00662`, Disc 2 `SLUS-00668`
                - Working state: `docs/ai_context/ACTIVE_HANDOFF.md` (read first)
                - Roadmap: `docs/project_plan.md` · Disc facts: `docs/disc_info.md` · Boundaries: `docs/legal.md`

                **Current phase: Phase 1 — disc verification.** Disc 1 is verified and
                recorded; disc 2 and the redump cross-check are pending. There is no
                disassembly, no symbols, no decompiled C, and no PC-port work — and
                none is allowed until the phase roadmap reaches them.
                """,
            CreatedAt = verifiedOn,
        });
    }
}
