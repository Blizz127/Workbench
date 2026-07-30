using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the TEN Revival project — local reconstruction of the Total
/// Entertainment Network protocol so original Win98 clients can auth, lobby,
/// and launch games. Duke3D multiplayer is proven; Dark Sun launch is current.
/// </summary>
public static class TenRevivalSeeder
{
    public const string ProjectName = "TEN Revival";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Local lab revival of the Total Entertainment Network: reverse-engineer " +
                          "the Win98 TEN client protocol and reimplement enough auth/lobby/game " +
                          "services so original clients can connect without the dead production network. " +
                          "Duke Nukem 3D multiplayer is end-to-end proven; focus is Dark Sun Online launch.",
            RepositoryPath = "/home/blizz/Projects/ten-revival",
            BuildCommand = "python3 -m pytest tests/test_golden.py -q",
            RunCommand = "./tools/start_fake_ten_monitor_experiment.sh",
            TestCommand = "python3 -m pytest tests/test_golden.py -q",
            DocumentationPaths =
            [
                "STATUS.md",
                "docs/CLIENT_PATCHES.md",
                "docs/architecture/",
            ],
            AiContextPaths =
            [
                "STATUS.md",
                "docs/CLIENT_PATCHES.md",
                "tools/fake_ten_monitor_experiment.py",
                "tools/patch_ten_exe.py",
            ],
            Tags = ["Networking", "Game Preservation", "Reverse Engineering", "Server Revival", "TEN"],
            ActiveTask = "Verify P10 cgs-darksun local INI resolve in the 86Box VM (correct ten_darks.exe binary)",
            CurrentObjective = "Original TEN client launches mdark.exe with a valid multiplayer session handoff",
            CreatedAt = now.AddDays(-120),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Win98 VM + TEN install", true, 100),
            ("FTP patch check + auth/session", true, 80),
            ("Lobby + game selector", true, 50),
            ("Duke3D two-client multiplayer", true, 29),
            ("Dark Sun ENTER creates session (P9)", true, 16),
            ("cgs-darksun resolve → mdark.exe launch (P10)", false, 0),
            ("Real TEN name service (remove P10)", false, 0),
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
            Title = "Duke3D: two original TEN clients in shared multiplayer",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-40),
            CompletedAt = now.AddDays(-29),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "P10: force cgs-darksun resolver to local tenserv.ini",
            Status = FrontierStatus.Current,
            Notes = "P10 redirects resolver 0x40bc50 so name-service flags are set and [cgs-darksun] is read from tenserv.ini. " +
                    "Code committed; 2026-07-14 VM test ran the wrong binary — re-verify with ten_darks.exe. " +
                    "Parallel: name-service recon docs for replacing P10 for real.",
            CreatedAt = now.AddDays(-16),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Stand up TEN name service; retire P10 scaffold",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "cgs-darksun resolve fails: name service never connects → \"Sorry, service is not available\"",
            IsResolved = true,
            Resolution = "P10 scaffold forces connected + directory-loaded flags so resolver reads local tenserv.ini (opt-in via patch_ten_exe.py --darks-resolve)",
            CreatedAt = now.AddDays(-20),
            ResolvedAt = now.AddDays(-16),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "P10 not yet VM-verified — 2026-07-14 run used wrong binary; need ten_darks.exe deploy retest",
            CreatedAt = now.AddDays(-16),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (100, TimelineCategory.General, "TEN lab: Win98 SE in 86Box, TEN installs and reaches login"),
            (80, TimelineCategory.Milestone, "Milestone completed: FTP patch check + auth/session"),
            (50, TimelineCategory.Milestone, "Milestone completed: Lobby + game selector"),
            (29, TimelineCategory.Milestone, "Milestone completed: Duke3D two-client multiplayer (324 relayed frames)"),
            (20, TimelineCategory.Blocker, "Dark Sun ENTER blocked on cgs-darksun name-service resolve"),
            (16, TimelineCategory.Code, "P9 ENTER creates arena session; P10 darks-resolve scaffold"),
            (16, TimelineCategory.Blocker, "P10 coded but not VM-verified (wrong binary on first test)"),
            (14, TimelineCategory.Code, "Docs: name-service recon path to replacing P10"),
            (2, TimelineCategory.Code, "gamesel-code2bc short-circuit ~90s login stall"),
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
            Title = "Active client patches",
            Content =
                """
                # Client patches (opt-in via tools/patch_ten_exe.py)

                | Patch | Purpose |
                |-------|---------|
                | P1 | SiteView lobby rooms draw |
                | P2 | Fast-return PatchReq |
                | P3 | Force good per-zone bandwidth |
                | P4 | Bypass Mr. Bandwidth Create/Join gates |
                | P5 | Suppress kill-apps popup |
                | P6 | Suppress TESTING arena label |
                | P8 | Guard NULL arena game-list crash |
                | P9 | Dark Sun ENTER creates session |
                | P10 | Force cgs-darksun local INI resolve |

                Duke build: `--all` → `ten_fast.exe`
                Dark Sun: `--all --darks-enter --darks-resolve` → `ten_darks.exe`

                See `docs/CLIENT_PATCHES.md` and `STATUS.md`.
                """,
            CreatedAt = now.AddDays(-16),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "P10 VM verify checklist",
            Content =
                """
                # Verify P10 in 86Box

                1. Deploy `analysis/installed-ten/ten_darks.exe` to `C:\PROGRA~1\TEN\`
                2. Ensure `TENSERV.INI` has:
                   ```ini
                   [cgs-darksun]
                   IPADDR=100.111.220.11
                   PORT=7504
                   ```
                3. Launch TEN → Dark Sun → GO → ENTER
                4. Expected: no "service not available"; `tenaddr.ini` written; `mdark.exe` spawned
                5. Watch `captures/fake-ten-monitor.log`

                If fail: `TEN.EXE -CONDEBUG` with DEBUGLEVEL=9 / VERBOSELEVEL=9.
                """,
            CreatedAt = now.AddDays(-14),
        });
    }
}
