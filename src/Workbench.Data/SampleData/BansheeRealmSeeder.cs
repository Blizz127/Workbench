using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Banshee Realm 11 project — an FFXI private server (LandSandBoat
/// fork) running as a five-service docker compose stack on the OVH box. The
/// launch profile carries the full operations set (status lights, stop, logs,
/// update, metrics), making the Run card its operations center.
/// </summary>
public static class BansheeRealmSeeder
{
    public const string ProjectName = "Banshee Realm 11";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "FFXI private server (LandSandBoat fork) running as a five-service " +
                          "docker compose stack: database (MariaDB), connect, search, world, map. " +
                          "Lua scripts and settings are live-mounted, so content changes only need " +
                          "a service restart. The stack survives reboots via restart policies + banshee.service.",
            RepositoryPath = "/home/blizz/dev/ffxi/Banshee-Realm-11",
            BuildCommand = "docker build -f docker/ubuntu.Dockerfile --tag localhost/banshee-realm:latest " +
                           "--build-arg COMPILER=gcc --build-arg CMAKE_BUILD_TYPE=Release --build-arg PCH_ENABLE=ON " +
                           "--build-arg REPO_URL=https://github.com/Blizz127/Banshee-Realm-11 " +
                           "--build-arg COMMIT_SHA=$(git rev-parse HEAD) .",
            RunCommand = "docker compose up -d",
            DocumentationPaths = ["README.md", "docs/ai_context/ACTIVE_HANDOFF.md"],
            AiContextPaths = ["AGENTS.md", "docs/ai_context/ACTIVE_HANDOFF.md"],
            Tags = ["FFXI", "Game Server", "Docker", "LandSandBoat"],
            ActiveTask = "Operate the OVH cloud runtime; continue solo-progression content (solomode MSQ, telemetry)",
            CurrentObjective = "Stable always-on OVH server with one-click operations from Workbench",
            GitHubRepoUrl = "https://github.com/Blizz127/Banshee-Realm-11",
            DefaultBranch = "main",
            Launch = new LaunchProfile
            {
                StartupGraceSeconds = 20,
                StatusCommand = "docker compose ps --format '{{.Service}} {{.State}} {{.Health}}'",
                RequiredServices = ["database", "connect", "search", "world", "map"],
                FailedPatterns = ["restarting", "dead", "unhealthy"],
                ServiceLogCommand = "docker compose logs --no-color --tail=200 {service}",
                HealthCheckRetries = 6,
                HealthCheckIntervalSeconds = 10,
                StopCommand = "docker compose down",
                LogsCommand = "docker compose logs --no-color --tail=120",
                UpdateCommand = "git pull --ff-only && docker compose up -d --remove-orphans && " +
                                "docker compose restart connect search world map",
                MetricsCommand =
                    """
                    players=$(docker compose exec -T database sh -lc 'mariadb -N -u xiadmin -p"$MARIADB_PASSWORD" xidb -e "SELECT COUNT(*) FROM accounts_sessions"' 2>/dev/null || echo '?'); echo "Players: $players"; docker stats --no-stream --format '{{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' | awk -F'\t' '$1 ~ /^banshee-realm-11/ {gsub(/%/,"",$2); cpu+=$2; split($3,m," / "); v=m[1]; if (v ~ /GiB/) {gsub(/GiB/,"",v); mem+=v*1024} else if (v ~ /MiB/) {gsub(/MiB/,"",v); mem+=v} else if (v ~ /KiB/) {gsub(/KiB/,"",v); mem+=v/1024}} END {printf "CPU: %.0f%%\nRAM: %.1f GB\n", cpu, mem/1024}'
                    """,
            },
            CreatedAt = now.AddDays(-45),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Server builds and runs via compose (Podman, Bazzite)", true, 40),
            ("Client reaches world (host networking fixes UDP NAT)", true, 38),
            ("Podman → Docker migration on the OVH box", true, 1),
            ("Reboot-safe: external volumes + banshee.service", true, 0),
            ("GitHub main authoritative (laptop → GitHub → OVH)", true, 0),
            ("Workbench operations center wired", true, 0),
            ("Solo-progression MSQ content (solomode modules)", false, 0),
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
            Title = "OVH cloud runtime migration (Podman → Docker)",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-7),
            CompletedAt = now,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Operate + content: solo-progression MSQ and telemetry",
            Status = FrontierStatus.Current,
            Notes = "Server is one-click operable from the Run card (status lights, players/CPU/RAM, " +
                    "stop/logs/update). Content work continues on the solomode MSQ modules and " +
                    "Ferreous Coffin telemetry.",
            CreatedAt = now,
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Automated off-site database backups",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Rootless podman NAT broke FFXI UDP zone traffic — client hung on " +
                          "\"Downloading data\" / FFXI-3001 after character select",
            IsResolved = true,
            Resolution = "Host networking puts the map server on the host stack (no NAT); " +
                         "kept through the Docker migration",
            CreatedAt = now.AddDays(-39),
            ResolvedAt = now.AddDays(-38),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (40, TimelineCategory.Milestone, "Milestone completed: compose stack runs (Podman, Bazzite)"),
            (38, TimelineCategory.Blocker, "UDP NAT blocker resolved with host networking"),
            (1, TimelineCategory.Milestone, "Milestone completed: Podman → Docker migration on OVH"),
            (0, TimelineCategory.Status, "Compose cleaned: docker-compose.yml canonical, Podman version archived, external volumes"),
            (0, TimelineCategory.Status, "banshee.service unit added — stack starts at boot, no login required"),
            (0, TimelineCategory.GitSync, "Branch renamed to main and pushed; GitHub default branch set to main"),
            (0, TimelineCategory.Note, "Workbench operations center wired via workbench.json launch profile"),
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
            Title = "Operations quick reference",
            Content =
                """
                # Banshee Realm 11 — operations

                - **Start/Stop**: `docker compose up -d` / `docker compose down` (repo root),
                  or the Run card buttons.
                - **Boot**: docker.service is enabled; containers carry restart policies.
                  `banshee.service` (tools/banshee.service) adds clean systemd start/stop.
                - **Volumes** (all external — never removed by `compose down -v`):
                  `banshee-realm-11_database`, `navmeshes`, `ximeshes`.
                - **Content changes**: Lua/settings are live-mounted — restart the game
                  services, no image rebuild.
                - **Update flow**: laptop → GitHub → OVH (`git pull --ff-only`), then the
                  Update button redeploys and restarts.
                - **DB access**: loopback only (`127.0.0.1:3306`), credentials in `.env`.
                """,
            CreatedAt = now,
        });
    }
}
