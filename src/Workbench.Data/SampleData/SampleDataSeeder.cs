using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Master entry point for sample-data seeding. Scans the configured projects
/// folder (default: &lt;repo-root&gt;/samples) for subdirectories containing
/// <c>workbench.json</c> files and seeds each recognized project with its
/// progress history (milestones, frontiers, blockers, timeline, notes).
///
/// Seeding is idempotent: projects are skipped if they already exist in the
/// database (matched by name).
/// </summary>
public static class SampleDataSeeder
{
    /// <summary>
    /// Scans <paramref name="projectsFolder"/> and seeds every recognized
    /// sample project that isn't already in the database.
    /// </summary>
    public static void SeedAll(WorkbenchDatabase database, string? projectsFolder = null)
    {
        var projects = new ProjectRepository(database);

        // Resolve the samples folder. Look for a "samples" directory relative
        // to the entry assembly, then fall back to a few common locations.
        projectsFolder ??= ResolveDefaultProjectsFolder();
        if (projectsFolder is null || !Directory.Exists(projectsFolder))
            return;

        var existing = projects.GetAll();
        var existingNames = new HashSet<string>(existing.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.Now;
        var milestones = new MilestoneRepository(database);
        var frontiers = new FrontierRepository(database);
        var blockers = new BlockerRepository(database);
        var timeline = new TimelineRepository(database);
        var notes = new NoteRepository(database);
        var builds = new BuildHistoryRepository(database);
        var tests = new TestHistoryRepository(database);

        // Map project-name constants to their seed actions.
        var seeders = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            [XenogearsSeeder.ProjectName] = () =>
                XenogearsSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, builds, tests, now),

            [CrimsonSandsSeeder.ProjectName] = () =>
                CrimsonSandsSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now),

            [TenRevivalSeeder.ProjectName] = () =>
                TenRevivalSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now),

            [GenericSoftwareSeeder.ProjectName] = () =>
                GenericSoftwareSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now),

            [ParasiteEveSeeder.ProjectName] = () =>
                ParasiteEveSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now),

            [DraenorSeeder.ProjectName] = () =>
                DraenorSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now),

            [FfxiEventsDumpSeeder.ProjectName] = () =>
                FfxiEventsDumpSeeder.Seed(projects, milestones, frontiers, blockers, timeline, notes, now),
        };

        foreach (var (projectName, seedAction) in seeders)
        {
            if (existingNames.Contains(projectName))
                continue;

            seedAction();
        }
    }

    private static string? ResolveDefaultProjectsFolder()
    {
        // Walk up from the entry assembly to find the repo root, then look
        // for "samples/" next to "src/".
        var baseDir = AppContext.BaseDirectory;
        var candidate = baseDir;
        while (candidate is not null)
        {
            var samples = Path.Combine(candidate, "samples");
            if (Directory.Exists(samples))
                return samples;

            var parent = Path.GetDirectoryName(candidate);
            if (parent == candidate)
                break;
            candidate = parent;
        }

        return null;
    }
}
