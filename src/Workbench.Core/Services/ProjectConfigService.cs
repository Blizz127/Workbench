using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Serializable per-project configuration, stored as <c>workbench.json</c> at
/// the repository root so it can live alongside the code it describes.
/// </summary>
public sealed class ProjectConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>Version of the portable project metadata contract.</summary>
    public int SchemaVersion { get; set; } = 1;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RepositoryPath { get; set; }
    public string? BuildCommand { get; set; }
    public string? RunCommand { get; set; }
    public string? TestCommand { get; set; }

    /// <summary>Optional separate build command for regression/matching builds.</summary>
    public string? MatchingBuildCommand { get; set; }

    /// <summary>
    /// Explicit compose file selection for Docker operations (filename relative
    /// to the repository root, or an absolute path). Null means auto-detect.
    /// </summary>
    public string? ComposeFile { get; set; }

    /// <summary>
    /// Explicit tmux session name for this project. Null means a slug is
    /// synthesized from the project name.
    /// </summary>
    public string? TmuxSession { get; set; }

    public List<string> DocumentationPaths { get; set; } = [];
    public List<string> AiContextPaths { get; set; } = [];
    public string? PreferredAiProvider { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Standing AI-prompt rules for this project. Stored as a single
    /// newline-separated string in the JSON for readability, but loaded
    /// into the Project model as a list of lines.
    /// </summary>
    public string? PromptRules { get; set; }

    /// <summary>Log-line patterns of interest (guards, markers) for this project.</summary>
    public List<LogPatternConfig> CustomLogPatterns { get; set; } = [];

    /// <summary>Post-launch runtime health validation; null means no health checking.</summary>
    public LaunchProfileConfig? LaunchProfile { get; set; }

    /// <summary>
    /// The mobile web dashboard's action allowlist. Lives only in this file —
    /// the Project database model deliberately knows nothing about it, and
    /// <see cref="ProjectConfigService.Save"/> carries it over from the
    /// on-disk file so a desktop settings save never wipes the allowlist.
    /// </summary>
    public List<MobileActionConfig> MobileActions { get; set; } = [];
}

/// <summary>One allowlisted mobile action in <c>workbench.json</c>.</summary>
public sealed class MobileActionConfig
{
    /// <summary>Stable identifier used in URLs and the action journal.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Button label on the phone.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Action type: dockerLogsAll, dockerLogsService, dockerRestartService,
    /// dockerRestartAllowed, tmuxCreateSession, or predefinedCommand. Unknown
    /// types fail closed — the action renders disabled.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Compose service name for the *Service action types.</summary>
    public string? Service { get; set; }

    /// <summary>Command selector for predefinedCommand: build, test, or launch.</summary>
    public string? CommandId { get; set; }

    /// <summary>
    /// Safety override: readOnly, requiresConfirmation, or
    /// requiresDoubleConfirmation. May only RAISE the type's default level,
    /// never lower it.
    /// </summary>
    public string? Safety { get; set; }

    /// <summary>
    /// Permits restarting database-like services (database, db, mysql,
    /// mariadb, postgres, postgresql, mongo, mongodb, redis) — refused
    /// without this explicit opt-in.
    /// </summary>
    public bool AllowDatabase { get; set; }
}

/// <summary>The <c>launchProfile</c> section of <c>workbench.json</c>.</summary>
public sealed class LaunchProfileConfig
{
    public int? StartupGraceSeconds { get; set; }
    public string? StatusCommand { get; set; }
    public List<string> RequiredServices { get; set; } = [];
    public List<string> OptionalServices { get; set; } = [];
    public List<string> HealthyPatterns { get; set; } = [];
    public List<string> FailedPatterns { get; set; } = [];
    public string? ServiceLogCommand { get; set; }
    public int? HealthCheckRetries { get; set; }
    public int? HealthCheckIntervalSeconds { get; set; }
    public string? StopCommand { get; set; }
    public string? LogsCommand { get; set; }
    public string? UpdateCommand { get; set; }
    public string? MetricsCommand { get; set; }
}

/// <summary>A single log-pattern entry in <c>workbench.json</c>.</summary>
public sealed class LogPatternConfig
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public string Category { get; set; } = "General";
}

public interface IProjectConfigService
{
    string GetConfigPath(string repositoryPath);
    ProjectConfig? Load(string repositoryPath);
    void Save(Project project);
    void ApplyTo(ProjectConfig config, Project project);
    ProjectConfig FromProject(Project project);
}

/// <summary>Reads and writes the per-project <c>workbench.json</c> configuration file.</summary>
public sealed class ProjectConfigService : IProjectConfigService
{
    public const string FileName = "workbench.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string GetConfigPath(string repositoryPath) => Path.Combine(repositoryPath, FileName);

    public ProjectConfig? Load(string repositoryPath)
    {
        var path = GetConfigPath(repositoryPath);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.RepositoryPath) || !Directory.Exists(project.RepositoryPath))
            throw new InvalidOperationException("The project has no valid repository path to save the configuration into.");

        var path = GetConfigPath(project.RepositoryPath);
        var config = FromProject(project);

        // The mobile allowlist lives only in the file, not in the Project
        // model — carry it over so a settings save never wipes it.
        config.MobileActions = Load(project.RepositoryPath)?.MobileActions ?? [];

        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }

    public void ApplyTo(ProjectConfig config, Project project)
    {
        project.Name = string.IsNullOrWhiteSpace(config.Name) ? project.Name : config.Name;
        project.Description = config.Description ?? project.Description;
        project.BuildCommand = config.BuildCommand;
        project.RunCommand = config.RunCommand;
        project.TestCommand = config.TestCommand;
        project.ComposeFile = config.ComposeFile ?? project.ComposeFile;
        project.TmuxSession = config.TmuxSession ?? project.TmuxSession;
        project.DocumentationPaths = [.. config.DocumentationPaths];
        project.AiContextPaths = [.. config.AiContextPaths];
        project.PreferredAiProvider = config.PreferredAiProvider;
        project.Tags = [.. config.Tags];

        // promptRules is a single multi-line string in the JSON; split into
        // non-empty lines for the Project model.
        if (config.PromptRules is { Length: > 0 } rules)
        {
            project.PromptRules = rules
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
        }

        // customLogPatterns: serialize each LogPatternConfig as a compact
        // string the rest of the app can parse (name|pattern|isRegex|category).
        if (config.CustomLogPatterns.Count > 0)
        {
            project.CustomLogPatterns = config.CustomLogPatterns
                .Select(p => $"{p.Name}|{p.Pattern}|{(p.IsRegex ? "1" : "0")}|{p.Category}")
                .ToList();
        }

        if (config.LaunchProfile is { } launch)
        {
            project.Launch = new LaunchProfile
            {
                StartupGraceSeconds = launch.StartupGraceSeconds ?? 5,
                StatusCommand = launch.StatusCommand,
                RequiredServices = [.. launch.RequiredServices],
                OptionalServices = [.. launch.OptionalServices],
                HealthyPatterns = [.. launch.HealthyPatterns],
                FailedPatterns = [.. launch.FailedPatterns],
                ServiceLogCommand = launch.ServiceLogCommand,
                HealthCheckRetries = launch.HealthCheckRetries ?? 3,
                HealthCheckIntervalSeconds = launch.HealthCheckIntervalSeconds ?? 5,
                StopCommand = launch.StopCommand,
                LogsCommand = launch.LogsCommand,
                UpdateCommand = launch.UpdateCommand,
                MetricsCommand = launch.MetricsCommand,
            };
        }
    }

    public ProjectConfig FromProject(Project project) => new()
    {
        Schema = "https://raw.githubusercontent.com/Blizz127/workbench/main/docs/workbench.schema.json",
        SchemaVersion = 1,
        Name = project.Name,
        Description = project.Description,
        RepositoryPath = project.RepositoryPath,
        BuildCommand = project.BuildCommand,
        RunCommand = project.RunCommand,
        TestCommand = project.TestCommand,
        ComposeFile = project.ComposeFile,
        TmuxSession = project.TmuxSession,
        DocumentationPaths = [.. project.DocumentationPaths],
        AiContextPaths = [.. project.AiContextPaths],
        PreferredAiProvider = project.PreferredAiProvider,
        Tags = [.. project.Tags],
        PromptRules = project.PromptRules.Count > 0
            ? string.Join('\n', project.PromptRules)
            : null,
        CustomLogPatterns = [],
        LaunchProfile = HasLaunchProfile(project.Launch)
            ? new LaunchProfileConfig
            {
                StartupGraceSeconds = project.Launch.StartupGraceSeconds,
                StatusCommand = project.Launch.StatusCommand,
                RequiredServices = [.. project.Launch.RequiredServices],
                OptionalServices = [.. project.Launch.OptionalServices],
                HealthyPatterns = [.. project.Launch.HealthyPatterns],
                FailedPatterns = [.. project.Launch.FailedPatterns],
                ServiceLogCommand = project.Launch.ServiceLogCommand,
                HealthCheckRetries = project.Launch.HealthCheckRetries,
                HealthCheckIntervalSeconds = project.Launch.HealthCheckIntervalSeconds,
                StopCommand = project.Launch.StopCommand,
                LogsCommand = project.Launch.LogsCommand,
                UpdateCommand = project.Launch.UpdateCommand,
                MetricsCommand = project.Launch.MetricsCommand,
            }
            : null,
    };

    // A profile only counts (and is only written) once it can actually assess health.
    private static bool HasLaunchProfile(LaunchProfile launch) =>
        !string.IsNullOrWhiteSpace(launch.StatusCommand);
}
