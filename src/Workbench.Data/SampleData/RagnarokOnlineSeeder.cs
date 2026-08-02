using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Ragnarok Online DIT project — Hercules-based private server with
/// planned observation-only HPM DIT bridge. R0: preservation and baseline only;
/// no autonomous actors or LLM behavior.
/// </summary>
public static class RagnarokOnlineSeeder
{
    public const string ProjectName = "Ragnarok Online DIT";

    /// <summary>
    /// Actual working git repo on the VPS. Note: the intended path under
    /// <c>worktrees/ragnarok-dit-server/</c> is empty; the live repo is at the
    /// workspace root sibling <c>ragnarok-dit-server/</c>.
    /// </summary>
    public const string DefaultRepositoryPath =
        "/home/blizz/dev/ragnarok-online/ragnarok-dit-server";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Private Hercules-based Ragnarok Online server for the DIT Human Behavior " +
                          "Platform. Preservation-focused solo server with planned observation-only " +
                          "HPM bridge. R0 has no autonomous actors or LLM behavior.",
            RepositoryPath = DefaultRepositoryPath,
            BuildCommand = "cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j$(nproc)",
            // Tooling lives one directory above the git repo (workspace root).
            RunCommand = "bash ../tooling/start-all.sh",
            TestCommand = "bash ../tooling/health-check.sh",
            ComposeFile = "../database/docker-compose.yml",
            TmuxSession = "ragnarok-dit",
            DocumentationPaths =
            [
                "../docs/README.md",
                "../docs/STATUS.md",
                "../docs/ACTIVE_HANDOFF.md",
                "../docs/HERCULES_ARCHITECTURE.md",
                "../docs/DIT_BRIDGE_ARCHITECTURE.md",
                "../docs/ROADMAP.md",
                "../docs/WORKBENCH_INTEGRATION.md",
                "README.md",
            ],
            AiContextPaths =
            [
                "../docs/ACTIVE_HANDOFF.md",
                "../docs/STATUS.md",
                "../docs/DIT_BRIDGE_ARCHITECTURE.md",
                "../docs/BUILD_AND_DEPLOYMENT_PLAN.md",
                "../docs/ROADMAP.md",
            ],
            Tags = ["Game Preservation", "Game Server", "Ragnarok Online", "Hercules", "DIT", "C", "HPM"],
            ActiveTask = "R2: isolated MariaDB on 3307 + Hercules schema init (not started yet)",
            CurrentObjective = "Preserve upstream Hercules behavior, establish a private Linux server, " +
                               "document client/database compatibility, later add observation-only HPM DIT bridge",
            GitHubRepoUrl = "https://github.com/Blizz127/Ragnarok-Online-DIT",
            DefaultBranch = "main",
            PromptRules =
            [
                "Preserve Hercules upstream compatibility.",
                "No autonomous actors or LLM behavior during R0.",
                "DIT bridge remains observation-only and HPM-based.",
                "Do not modify sources/hercules-upstream or sources/rathena-reference.",
                "Do not invent client packet versions without client evidence.",
                "Do not expose services beyond localhost / Tailscale without explicit approval.",
            ],
            Launch = new LaunchProfile
            {
                StartupGraceSeconds = 15,
                StatusCommand =
                    "check() { if ss -ltn 2>/dev/null | grep -q \":$2 \"; then echo \"$1 up\"; else echo \"$1 down\"; fi; }; " +
                    "check mariadb 3307; check login-server 6900; check char-server 6121; check map-server 5121",
                // Servers are required for Healthy; MariaDB optional so a cold
                // box without DB started does not look like a hard failure when
                // only inspecting process ports after a partial start.
                RequiredServices = ["login-server", "char-server", "map-server"],
                OptionalServices = ["mariadb"],
                HealthyPatterns = ["up"],
                FailedPatterns = ["down", "error", "fatal"],
                ServiceLogCommand = "tail -200 ../runtime/logs/{service}.log",
                HealthCheckRetries = 5,
                HealthCheckIntervalSeconds = 5,
                StopCommand = "bash ../tooling/stop-all.sh",
                LogsCommand = "tail -120 ../runtime/logs/*.log 2>/dev/null || echo 'No runtime logs yet'",
            },
            SharedId = "a8c4e2f91b3d4e5f6a7b8c9d0e1f2a3b",
            CreatedAt = now.AddDays(-1),
        };
        projects.Add(project);

        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("R0: Project setup and provenance docs", true, 1),
            ("R1: Clean Hercules Linux build (login/char/map/api)", true, 1),
            ("R2: Isolated MariaDB + schema initialization", false, 0),
            ("R3: Client compatibility research", false, 0),
            ("R4: Login smoke path (no DIT actors)", false, 0),
            ("R5: Observation-only HPM DIT bridge", false, 0),
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
            Title = "R1: Hercules v2026.07 builds on Ubuntu 26.04",
            Status = FrontierStatus.Completed,
            Notes = "Binaries in bin/; 12 HPM plugins built; zero source modifications required.",
            CreatedAt = now.AddDays(-1),
            CompletedAt = now,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "R2: MariaDB on 3307 + schema import",
            Status = FrontierStatus.Current,
            Notes = "database/docker-compose.yml ready; .env not yet populated; container not started.",
            CreatedAt = now,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "R5: Observation-only HPM DIT bridge",
            Status = FrontierStatus.Next,
            Notes = "Architecture documented; no invasive core mods; no autonomous actors in R0.",
            CreatedAt = now,
        });

        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "MariaDB not started and database/.env not populated (R2 not begun)",
            BlockerClass = "database",
            Severity = "medium",
            Status = BlockerStatus.Active,
            Evidence = "docs/STATUS.md — Database section",
            NextSafestStep = "Populate database/.env from .env.example, then docker compose up -d on port 3307",
            CreatedAt = now,
        });

        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (1, TimelineCategory.Milestone, "Milestone completed: R0 project setup and provenance"),
            (1, TimelineCategory.Build, "Hercules v2026.07 clean build (login/char/map/api + HPM plugins)"),
            (1, TimelineCategory.Milestone, "Milestone completed: R1 Linux build"),
            (0, TimelineCategory.Note, "Workbench registration: tooling at ../tooling; intended worktrees/ path is empty"),
            (0, TimelineCategory.Frontier, "Next: R2 isolated MariaDB and schema init"),
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

        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Discovered VPS layout",
            Content =
                """
                # Ragnarok Online DIT — VPS facts (do not invent)

                | Path | Role |
                |------|------|
                | `~/dev/ragnarok-online/` | Workspace (+ docs, tooling, database) |
                | `ragnarok-dit-server/` | **Actual** working git repo (Hercules fork) |
                | `worktrees/ragnarok-dit-server/` | **Empty** — intended path not populated |
                | `sources/hercules-upstream/` | Clean Hercules reference |
                | `sources/rathena-reference/` | Clean rAthena reference |
                | `tooling/*.sh` | start-all / stop-all / health-check / init-database |
                | `database/docker-compose.yml` | MariaDB on host port **3307** |

                **Remote:** https://github.com/Blizz127/Ragnarok-Online-DIT
                **Branch:** `main` (tracks Hercules `upstream/stable`, ahead)

                ## Commands (cwd = ragnarok-dit-server)

                ```bash
                cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j$(nproc)
                bash ../tooling/start-all.sh
                bash ../tooling/stop-all.sh
                bash ../tooling/health-check.sh
                bash ../tooling/init-database.sh   # requires database/.env
                ```

                Binaries live under `bin/` (`login-server`, `char-server`, `map-server`, `api-server`).
                Login smoke test is **not configured** until R3/R4.
                Runtime health stays **Unknown** until services are started and the health check passes.
                """,
            CreatedAt = now,
        });
    }
}
