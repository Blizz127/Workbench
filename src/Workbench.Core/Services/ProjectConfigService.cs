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
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RepositoryPath { get; set; }
    public string? BuildCommand { get; set; }
    public string? RunCommand { get; set; }
    public string? TestCommand { get; set; }
    public List<string> DocumentationPaths { get; set; } = [];
    public List<string> AiContextPaths { get; set; } = [];
    public string? PreferredAiProvider { get; set; }
    public List<string> Tags { get; set; } = [];
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
        File.WriteAllText(path, JsonSerializer.Serialize(FromProject(project), JsonOptions));
    }

    public void ApplyTo(ProjectConfig config, Project project)
    {
        project.Name = string.IsNullOrWhiteSpace(config.Name) ? project.Name : config.Name;
        project.Description = config.Description ?? project.Description;
        project.BuildCommand = config.BuildCommand;
        project.RunCommand = config.RunCommand;
        project.TestCommand = config.TestCommand;
        project.DocumentationPaths = [.. config.DocumentationPaths];
        project.AiContextPaths = [.. config.AiContextPaths];
        project.PreferredAiProvider = config.PreferredAiProvider;
        project.Tags = [.. config.Tags];
    }

    public ProjectConfig FromProject(Project project) => new()
    {
        Name = project.Name,
        Description = project.Description,
        RepositoryPath = project.RepositoryPath,
        BuildCommand = project.BuildCommand,
        RunCommand = project.RunCommand,
        TestCommand = project.TestCommand,
        DocumentationPaths = [.. project.DocumentationPaths],
        AiContextPaths = [.. project.AiContextPaths],
        PreferredAiProvider = project.PreferredAiProvider,
        Tags = [.. project.Tags],
    };
}
