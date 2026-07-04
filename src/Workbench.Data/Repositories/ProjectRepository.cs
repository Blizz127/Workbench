using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class ProjectRepository(WorkbenchDatabase database) : IProjectRepository
{
    private const string SelectColumns =
        """
        id, name, status, description, repository_path, build_command, run_command,
        test_command, matching_build_command, documentation_paths, ai_context_paths,
        preferred_ai_provider, prompt_rules, custom_log_patterns, active_task, current_objective,
        last_known_branch, last_known_commit_sha, last_known_is_dirty, last_known_ahead_by,
        last_known_behind_by, last_refreshed_at, created_at, updated_at
        """;

    public IReadOnlyList<Project> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM projects ORDER BY updated_at DESC;";

        var projects = new List<Project>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                projects.Add(Map(reader));
        }

        foreach (var project in projects)
            project.Tags = LoadTags(connection, project.Id);

        return projects;
    }

    public Project? GetById(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        Project? project = null;
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
                project = Map(reader);
        }

        if (project is not null)
            project.Tags = LoadTags(connection, project.Id);

        return project;
    }

    public long Add(Project project)
    {
        var now = DateTimeOffset.Now;
        project.CreatedAt = project.CreatedAt == default ? now : project.CreatedAt;
        project.UpdatedAt = now;

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO projects (
                name, status, description, repository_path, build_command, run_command,
                test_command, matching_build_command, documentation_paths, ai_context_paths,
                preferred_ai_provider, prompt_rules, custom_log_patterns, active_task, current_objective,
                last_known_branch, last_known_commit_sha, last_known_is_dirty, last_known_ahead_by,
                last_known_behind_by, last_refreshed_at, created_at, updated_at)
            VALUES (
                $name, $status, $description, $repository_path, $build_command, $run_command,
                $test_command, $matching_build_command, $documentation_paths, $ai_context_paths,
                $preferred_ai_provider, $prompt_rules, $custom_log_patterns, $active_task, $current_objective,
                $last_known_branch, $last_known_commit_sha, $last_known_is_dirty, $last_known_ahead_by,
                $last_known_behind_by, $last_refreshed_at, $created_at, $updated_at);
            SELECT last_insert_rowid();
            """;
        BindParameters(command, project);

        project.Id = (long)command.ExecuteScalar()!;
        SaveTags(connection, transaction, project);
        transaction.Commit();
        return project.Id;
    }

    public void Update(Project project)
    {
        project.UpdatedAt = DateTimeOffset.Now;

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE projects SET
                name = $name, status = $status, description = $description,
                repository_path = $repository_path, build_command = $build_command,
                run_command = $run_command, test_command = $test_command,
                matching_build_command = $matching_build_command,
                documentation_paths = $documentation_paths, ai_context_paths = $ai_context_paths,
                preferred_ai_provider = $preferred_ai_provider, prompt_rules = $prompt_rules,
                custom_log_patterns = $custom_log_patterns, active_task = $active_task,
                current_objective = $current_objective,
                last_known_branch = $last_known_branch, last_known_commit_sha = $last_known_commit_sha,
                last_known_is_dirty = $last_known_is_dirty, last_known_ahead_by = $last_known_ahead_by,
                last_known_behind_by = $last_known_behind_by, last_refreshed_at = $last_refreshed_at,
                created_at = $created_at, updated_at = $updated_at
            WHERE id = $id;
            """;
        BindParameters(command, project);
        command.Parameters.AddWithValue("$id", project.Id);
        command.ExecuteNonQuery();

        SaveTags(connection, transaction, project);
        transaction.Commit();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void BindParameters(SqliteCommand command, Project p)
    {
        command.Parameters.AddWithValue("$name", p.Name);
        command.Parameters.AddWithValue("$status", p.Status.ToString());
        command.Parameters.AddWithValue("$description", SqliteMapping.ToDbNullable(p.Description));
        command.Parameters.AddWithValue("$repository_path", SqliteMapping.ToDbNullable(p.RepositoryPath));
        command.Parameters.AddWithValue("$build_command", SqliteMapping.ToDbNullable(p.BuildCommand));
        command.Parameters.AddWithValue("$run_command", SqliteMapping.ToDbNullable(p.RunCommand));
        command.Parameters.AddWithValue("$test_command", SqliteMapping.ToDbNullable(p.TestCommand));
        command.Parameters.AddWithValue("$matching_build_command", SqliteMapping.ToDbNullable(p.MatchingBuildCommand));
        command.Parameters.AddWithValue("$documentation_paths", SqliteMapping.ToJson(p.DocumentationPaths));
        command.Parameters.AddWithValue("$ai_context_paths", SqliteMapping.ToJson(p.AiContextPaths));
        command.Parameters.AddWithValue("$preferred_ai_provider", SqliteMapping.ToDbNullable(p.PreferredAiProvider));
        command.Parameters.AddWithValue("$prompt_rules", SqliteMapping.ToDbNullable(p.PromptRules));
        command.Parameters.AddWithValue("$custom_log_patterns", SqliteMapping.ToJson(p.CustomLogPatterns));
        command.Parameters.AddWithValue("$active_task", SqliteMapping.ToDbNullable(p.ActiveTask));
        command.Parameters.AddWithValue("$current_objective", SqliteMapping.ToDbNullable(p.CurrentObjective));
        command.Parameters.AddWithValue("$last_known_branch", SqliteMapping.ToDbNullable(p.LastKnownBranch));
        command.Parameters.AddWithValue("$last_known_commit_sha", SqliteMapping.ToDbNullable(p.LastKnownCommitSha));
        command.Parameters.AddWithValue("$last_known_is_dirty", (object?)p.LastKnownIsDirty is bool dirty ? (dirty ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$last_known_ahead_by", SqliteMapping.ToDbNullable(p.LastKnownAheadBy));
        command.Parameters.AddWithValue("$last_known_behind_by", SqliteMapping.ToDbNullable(p.LastKnownBehindBy));
        command.Parameters.AddWithValue("$last_refreshed_at", SqliteMapping.ToDb(p.LastRefreshedAt));
        command.Parameters.AddWithValue("$created_at", SqliteMapping.ToDb(p.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", SqliteMapping.ToDb(p.UpdatedAt));
    }

    private static Project Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Name = reader.GetString(1),
        Status = reader.GetEnum<ProjectStatus>(2),
        Description = reader.GetNullableString(3),
        RepositoryPath = reader.GetNullableString(4),
        BuildCommand = reader.GetNullableString(5),
        RunCommand = reader.GetNullableString(6),
        TestCommand = reader.GetNullableString(7),
        MatchingBuildCommand = reader.GetNullableString(8),
        DocumentationPaths = SqliteMapping.FromJson(reader.GetString(9)),
        AiContextPaths = SqliteMapping.FromJson(reader.GetString(10)),
        PreferredAiProvider = reader.GetNullableString(11),
        PromptRules = reader.GetNullableString(12),
        CustomLogPatterns = SqliteMapping.LogPatternsFromJson(reader.GetString(13)),
        ActiveTask = reader.GetNullableString(14),
        CurrentObjective = reader.GetNullableString(15),
        LastKnownBranch = reader.GetNullableString(16),
        LastKnownCommitSha = reader.GetNullableString(17),
        LastKnownIsDirty = reader.IsDBNull(18) ? null : reader.GetInt32(18) != 0,
        LastKnownAheadBy = reader.GetNullableInt32(19),
        LastKnownBehindBy = reader.GetNullableInt32(20),
        LastRefreshedAt = reader.GetNullableDateTimeOffset(21),
        CreatedAt = reader.GetDateTimeOffset(22),
        UpdatedAt = reader.GetDateTimeOffset(23),
    };

    private static List<string> LoadTags(SqliteConnection connection, long projectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tag FROM project_tags WHERE project_id = $id ORDER BY tag;";
        command.Parameters.AddWithValue("$id", projectId);

        var tags = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            tags.Add(reader.GetString(0));
        return tags;
    }

    private static void SaveTags(SqliteConnection connection, SqliteTransaction transaction, Project project)
    {
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM project_tags WHERE project_id = $id;";
            delete.Parameters.AddWithValue("$id", project.Id);
            delete.ExecuteNonQuery();
        }

        foreach (var tag in project.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO project_tags (project_id, tag) VALUES ($id, $tag);";
            insert.Parameters.AddWithValue("$id", project.Id);
            insert.Parameters.AddWithValue("$tag", tag.Trim());
            insert.ExecuteNonQuery();
        }
    }
}
