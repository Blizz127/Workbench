using Workbench.Core.Models;

namespace Workbench.Core.Services;

public interface IProjectDiscoveryService
{
    /// <summary>
    /// Scans the configured discovery folder for unregistered projects and
    /// registers them; returns how many were newly registered. A no-op when
    /// discovery is disabled or the folder is missing, and never throws.
    /// </summary>
    int Scan();
}

/// <summary>
/// Registers projects found on disk instead of requiring manual entry.
/// The discovery folder (default <c>~/dev</c>) is scanned two levels deep —
/// deep enough for layouts like <c>ffxi/Banshee-Realm-11</c> — and any
/// directory carrying a <c>.git</c> folder, a <c>workbench.json</c>, or a
/// compose file counts as a project. A <c>workbench.json</c> is adopted
/// as-is; other candidates get dotnet/docker defaults synthesized from what
/// defaults synthesized from what the directory contains.
/// </summary>
public sealed class ProjectDiscoveryService : IProjectDiscoveryService
{
    private static readonly HashSet<string> SkippedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

    private readonly IProjectRepository _projects;
    private readonly IProjectConfigService _config;
    private readonly ITimelineRepository _timeline;
    private readonly IAppSettingsService _appSettings;

    public ProjectDiscoveryService(
        IProjectRepository projects,
        IProjectConfigService config,
        ITimelineRepository timeline,
        IAppSettingsService appSettings)
    {
        _projects = projects;
        _config = config;
        _timeline = timeline;
        _appSettings = appSettings;
    }

    public int Scan()
    {
        try
        {
            if (!_appSettings.Settings.DiscoveryEnabled)
                return 0;

            var root = _appSettings.DiscoveryFolder;
            if (!Directory.Exists(root))
                return 0;

            var known = _projects.GetAll()
                .Select(p => p.RepositoryPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => NormalizePath(p!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var registered = 0;
            foreach (var candidate in EnumerateCandidates(root))
            {
                if (!known.Add(NormalizePath(candidate)))
                    continue;

                try
                {
                    Register(candidate, root);
                    registered++;
                }
                catch (Exception)
                {
                    // One unreadable directory must never break the scan.
                }
            }

            return registered;
        }
        catch (Exception)
        {
            // Discovery runs at startup; it must never take the app down.
            return 0;
        }
    }

    /// <summary>Directories at depth 1 and 2 below the root that look like projects.</summary>
    private static IEnumerable<string> EnumerateCandidates(string root)
    {
        foreach (var child in EnumerateVisibleDirectories(root))
        {
            if (IsCandidate(child))
                yield return child;

            foreach (var grandchild in EnumerateVisibleDirectories(child))
            {
                if (IsCandidate(grandchild))
                    yield return grandchild;
            }
        }
    }

    private static IEnumerable<string> EnumerateVisibleDirectories(string directory)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(directory);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.') || SkippedDirectoryNames.Contains(name))
                continue;

            yield return entry;
        }
    }

    /// <summary>A directory counts as a project when it carries its own root markers.</summary>
    private static bool IsCandidate(string directory) =>
        // .git is a directory for clones, a file for worktrees/submodules.
        Directory.Exists(Path.Combine(directory, ".git")) ||
        File.Exists(Path.Combine(directory, ".git")) ||
        File.Exists(Path.Combine(directory, ProjectConfigService.FileName)) ||
        ComposeFileInspector.HasComposeFile(directory);

    private void Register(string directory, string root)
    {
        var project = new Project
        {
            Name = Path.GetFileName(NormalizePath(directory)),
            RepositoryPath = Path.GetFullPath(directory),
            Status = ProjectStatus.Active,
        };

        // A repository that already carries a workbench.json is adopted as-is;
        // anything else gets defaults synthesized from what the directory holds.
        if (_config.Load(directory) is { } config)
        {
            _config.ApplyTo(config, project);
        }
        else
        {
            if (Directory.EnumerateFiles(directory, "*.sln").Any() ||
                Directory.EnumerateFiles(directory, "*.csproj").Any())
            {
                project.BuildCommand = "dotnet build";
                project.TestCommand = "dotnet test";
            }

            if (ComposeFileInspector.FindComposeFile(directory) is { } composeFile)
            {
                project.RunCommand = "docker compose up -d";
                project.Launch = new LaunchProfile
                {
                    StatusCommand = "docker compose ps --format '{{.Service}} {{.State}} {{.Health}}'",
                    StopCommand = "docker compose down",
                    LogsCommand = "docker compose logs --no-color --tail=120",
                    RequiredServices = [.. ComposeFileInspector.GetServiceNames(composeFile)],
                };
            }
        }

        _projects.Add(project);
        _timeline.Add(new TimelineEntry
        {
            ProjectId = project.Id,
            Category = TimelineCategory.Status,
            Message = $"Project discovered under {root}",
        });
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
