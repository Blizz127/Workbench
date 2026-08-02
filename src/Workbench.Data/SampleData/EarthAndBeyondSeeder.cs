using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Earth &amp; Beyond DIT project — preservation-first Net-7
/// modernization on Linux with an isolated MariaDB and historical import.
/// Current phase is E2-C (stable database-backed idle server); Net-7 process
/// start is still manual evidence, not a one-click healthy runtime.
/// </summary>
public static class EarthAndBeyondSeeder
{
    public const string ProjectName = "Earth & Beyond DIT";

    /// <summary>VPS working repository path verified on discovery.</summary>
    public const string DefaultRepositoryPath =
        "/home/blizz/dev/earth-and-beyond/worktrees/earth-and-beyond-dit-server";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Preservation-first Net-7 modernization on Linux: isolated MariaDB 10.11, " +
                          "historical database import, and a native Linux Net-7 binary that reaches " +
                          "item loading. DIT player/object/movement seams come later.",
            RepositoryPath = DefaultRepositoryPath,
            BuildCommand =
                "cd tooling/linux-build && docker build -t net7-linux-build . && " +
                "docker run --rm -v \"$(pwd)\":/output net7-linux-build bash -c " +
                "\"make -f Makefile.linux -j$(nproc) && cp Net7 /output/Net7-linux\"",
            // Run starts the isolated DB only — full Net-7 process control is E2-E (enbctl).
            RunCommand = "docker compose --project-name enb-e2 -f deploy/e2/compose.yml up -d",
            TestCommand = "bash database/scripts/validate-schema.sh",
            ComposeFile = "deploy/e2/compose.yml",
            TmuxSession = "earth-and-beyond-dit",
            DocumentationPaths =
            [
                "docs/README.md",
                "docs/STATUS.md",
                "docs/ACTIVE_HANDOFF.md",
                "docs/ROADMAP.md",
                "docs/LINUX_RUNTIME_STATUS.md",
                "docs/SCHEMA_COMPATIBILITY.md",
                "docs/MODERNIZATION_AND_LOADABLE_BASELINE_PLAN.md",
                "docs/WORKBENCH_INTEGRATION.md",
            ],
            AiContextPaths =
            [
                "docs/ACTIVE_HANDOFF.md",
                "docs/STATUS.md",
                "docs/MODERNIZATION_AND_LOADABLE_BASELINE_PLAN.md",
                "docs/LINUX_RUNTIME_STATUS.md",
                "docs/SCHEMA_COMPATIBILITY.md",
                "docs/ROADMAP.md",
            ],
            Tags = ["Game Preservation", "Game Server", "Net-7", "Earth and Beyond", "DIT", "MariaDB"],
            ActiveTask = "E2-C: content data provenance for migration-created empty tables; stable idle server",
            CurrentObjective = "Preserve and modernize Net-7 on Linux; establish reliable client " +
                               "login/sector/movement/persistence before DIT behavior",
            GitHubRepoUrl = "https://github.com/Blizz127/Earth-and-Beyond-DIT",
            // Live branch on the VPS at registration time; re-check with git status.
            DefaultBranch = "feature/e2c-content-provenance",
            PromptRules =
            [
                "Do not modify the archival clone at sources/net7-kyp/.",
                "Do not expose any service to public interfaces; localhost only.",
                "Do not use net-7.org or local.net-7.org hostnames.",
                "Do not normalize line endings or bulk-format historical source.",
                "Do not edit the historical dump at database/historical/; use migrations.",
                "Do not start E3 client work before the E2-D ABI audit passes.",
                "Do not disturb FFXI, WoW, Xenogears, or Workbench running services.",
            ],
            Launch = new LaunchProfile
            {
                StartupGraceSeconds = 15,
                // Required service is MariaDB only — Net-7 / sector / proxy are optional until E2-E.
                StatusCommand =
                    "check() { if ss -ltn 2>/dev/null | grep -q \":$2 \"; then echo \"$1 up\"; else echo \"$1 down\"; fi; }; " +
                    "check enb-database 13306; " +
                    "if pgrep -f 'Net7-linux' >/dev/null 2>&1; then echo 'net7 up'; else echo 'net7 down'; fi; " +
                    "if ss -ltn 2>/dev/null | grep -q ':3501 '; then echo 'sector up'; else echo 'sector down'; fi; " +
                    "if ss -ltn 2>/dev/null | grep -q ':443 '; then echo 'proxy-auth up'; else echo 'proxy-auth down'; fi",
                RequiredServices = ["enb-database"],
                OptionalServices = ["net7", "sector", "proxy-auth"],
                HealthyPatterns = ["up"],
                FailedPatterns = ["error", "fatal"],
                ServiceLogCommand =
                    "case \"{service}\" in " +
                    "enb-database) docker compose --project-name enb-e2 -f deploy/e2/compose.yml logs --no-color --tail=200 enb-database ;; " +
                    "net7|sector|proxy-auth) tail -200 tooling/logs/Net7_server_* 2>/dev/null | tail -200 ;; " +
                    "*) echo \"No log mapping for {service}\" ;; esac",
                HealthCheckRetries = 5,
                HealthCheckIntervalSeconds = 5,
                StopCommand =
                    "docker compose --project-name enb-e2 -f deploy/e2/compose.yml stop; " +
                    "pkill -f 'Net7-linux' 2>/dev/null || true",
                LogsCommand =
                    "docker compose --project-name enb-e2 -f deploy/e2/compose.yml logs --no-color --tail=80; " +
                    "echo '---'; tail -80 tooling/logs/Net7_server_* 2>/dev/null || true",
            },
            SharedId = "d6ff48531df64854be6a686c0947554b",
            CreatedAt = now.AddDays(-1),
        };
        projects.Add(project);

        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("E0: Source preservation and project setup", true, 1),
            ("E1: Historical Windows build reproduction (blocked; reference-only)", false, 0),
            ("E2: Isolated database import", true, 1),
            ("E2-B: Schema compatibility and migrations", true, 0),
            ("E2-C: Stable database-backed idle server", false, 0),
            ("E2-D: Packet/ABI compatibility audit", false, 0),
            ("E2-E: One-command runtime (enbctl)", false, 0),
            ("E3: Local client login", false, 0),
            ("E4: Sector entry and movement baseline", false, 0),
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
            Title = "E2-B: Schema compatibility (migrations + item load)",
            Status = FrontierStatus.Completed,
            Notes = "Migrations 0001–0003; validation PASS; server past item loading; sector registers.",
            CreatedAt = now.AddDays(-1),
            CompletedAt = now,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "E2-C: Content data for migration-created tables",
            Status = FrontierStatus.Current,
            Notes = "buffs/effects/skills/missions/mobs/sectors/starbases empty; " +
                    "provenance via Net7Tools INI parsers / XML Exporter — do not invent data.",
            CreatedAt = now,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "E2-D: Packet/ABI audit before any client",
            Status = FrontierStatus.Next,
            Notes = "64-bit long vs Win32 wire layout; no E3 client work until this passes.",
            CreatedAt = now,
        });

        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Migration-created content tables are empty (data gap, not schema gap)",
            BlockerClass = "content-data",
            Severity = "medium",
            Status = BlockerStatus.Active,
            Evidence = "docs/ACTIVE_HANDOFF.md; evidence/e2b/server-startup-after-migrations.txt",
            NextSafestStep = "Trace Net7Tools INI/XML exporters for buffs/skills/missions/mobs/sectors",
            CreatedAt = now,
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Port 443 bind requires CAP_NET_BIND_SERVICE; Net7Proxy/auth not running",
            BlockerClass = "runtime",
            Severity = "low",
            Status = BlockerStatus.Monitoring,
            Evidence = "tooling/logs Net7 startup lines: Listener unable to bind 127.0.0.1:443",
            NextSafestStep = "Defer full auth/proxy until E2-E enbctl and E3 client plan",
            CreatedAt = now,
        });

        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (1, TimelineCategory.Milestone, "Milestone completed: E0 source preservation and setup"),
            (1, TimelineCategory.Milestone, "Milestone completed: E2 isolated MariaDB import on 127.0.0.1:13306"),
            (0, TimelineCategory.Milestone, "Milestone completed: E2-B schema migrations; item load clean"),
            (0, TimelineCategory.Build, "Linux Net7-linux binary rebuilt (~11 MB ELF)"),
            (0, TimelineCategory.Frontier, "Advanced to E2-C content data provenance"),
            (0, TimelineCategory.Note, "Workbench registration: DB start/stop/schema check wired; Net-7 one-click deferred to E2-E"),
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
                # Earth & Beyond — VPS facts (do not invent)

                | Path | Role |
                |------|------|
                | `~/dev/earth-and-beyond/` | Workspace root |
                | `worktrees/earth-and-beyond-dit-server/` | Working git repo |
                | `sources/net7-kyp/` | Archival clone — do not modify |
                | `tooling/linux-build/Net7-linux` | Linux binary |
                | `deploy/e2/compose.yml` | MariaDB 10.11 on `127.0.0.1:13306` |

                **Branch on disk:** `feature/e2b-schema-compatibility` (not `feature/e2-isolated-database`).
                **Remote:** https://github.com/Blizz127/Earth-and-Beyond-DIT

                ## Commands (cwd = working repo)

                ```bash
                # Build Net-7 (Docker + Makefile.linux)
                cd tooling/linux-build && docker build -t net7-linux-build . && \
                  docker run --rm -v "$(pwd)":/output net7-linux-build \
                  bash -c "make -f Makefile.linux -j$(nproc) && cp Net7 /output/Net7-linux"

                # Database
                docker compose --project-name enb-e2 -f deploy/e2/compose.yml up -d
                docker compose --project-name enb-e2 -f deploy/e2/compose.yml stop
                bash database/scripts/validate-schema.sh

                # Manual Net-7 smoke (not one-click healthy runtime)
                cd tooling/linux-build && timeout 60 stdbuf -oL -eL ./Net7-linux
                ```

                Login smoke test and enbctl process control are **not configured** yet (E2-E / E5).
                """,
            CreatedAt = now,
        });
    }
}
