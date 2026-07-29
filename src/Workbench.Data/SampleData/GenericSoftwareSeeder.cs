using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Generic Software sample project — a minimal example showing
/// Workbench works for ordinary non-game projects.
/// </summary>
public static class GenericSoftwareSeeder
{
    public const string ProjectName = "Example Web Service";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "An ordinary software project — Workbench does not care whether " +
                          "the frontier is a function address or a feature ticket.",
            RepositoryPath = "/home/blizz/Projects/sbisd-compromise-containment-dotnet",
            BuildCommand = "npm run build",
            RunCommand = "npm run dev",
            TestCommand = "npm test",
            DocumentationPaths = ["README.md", "docs/"],
            AiContextPaths = ["docs/architecture.md", "src/api/routes.ts"],
            Tags = ["Software", "Automation"],
            ActiveTask = "Add rate limiting to the public API",
            CurrentObjective = "Ship v2.1 with rate-limited API and improved error responses",
            CreatedAt = now.AddDays(-60),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("v2.0 — Auth & Users", true, 45),
            ("v2.0.1 — Hotfix: JWT expiry", true, 30),
            ("v2.1 — Rate Limiting", false, 0),
            ("v2.2 — Webhook Support", false, 0),
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
            Title = "JWT refresh token rotation",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-32),
            CompletedAt = now.AddDays(-28),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Per-route rate limit middleware",
            Status = FrontierStatus.Current,
            Notes = "Token-bucket algorithm chosen. Need to decide on per-user vs per-IP defaults.",
            CreatedAt = now.AddDays(-7),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Structured error response format (RFC 7807)",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "JWT refresh endpoint returning 500 under load — Redis pool exhaustion",
            IsResolved = true,
            Resolution = "Increased Redis connection pool from 10 to 50; added connection timeout",
            CreatedAt = now.AddDays(-35),
            ResolvedAt = now.AddDays(-31),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Rate limit headers conflict with existing CORS middleware",
            CreatedAt = now.AddDays(-3),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (35, TimelineCategory.Blocker, "JWT refresh 500s under load"),
            (31, TimelineCategory.Blocker, "Resolved JWT Redis pool exhaustion"),
            (30, TimelineCategory.Milestone, "Milestone completed: v2.0.1 Hotfix"),
            (28, TimelineCategory.Frontier, "Completed JWT refresh token rotation"),
            (10, TimelineCategory.Code, "Sketched rate-limit middleware interface"),
            (7, TimelineCategory.Frontier, "Started per-route rate limit middleware"),
            (5, TimelineCategory.Build, "Build succeeded"),
            (3, TimelineCategory.Blocker, "Rate limit headers conflict with CORS middleware"),
            (2, TimelineCategory.Build, "Build succeeded — 47 tests pass"),
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
            Title = "Rate limiting design",
            Content =
                """
                # Rate Limiting Design

                ## Algorithm: Token Bucket
                - Each route gets a bucket with configurable refill rate
                - Default: 100 req/min per IP, burst of 10
                - Authenticated users: 1000 req/min

                ## Headers (RFC 6585 style)
                - `X-RateLimit-Limit`
                - `X-RateLimit-Remaining`
                - `X-RateLimit-Reset`

                ## TODO
                - [ ] Decide per-IP vs per-user default
                - [ ] Handle CORS header collision
                - [ ] Add Redis-backed counters for horizontal scaling
                """,
            CreatedAt = now.AddDays(-6),
        });
    }
}
