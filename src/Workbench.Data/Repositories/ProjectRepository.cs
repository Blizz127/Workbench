using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class ProjectRepository(WorkbenchDatabase database) : IProjectRepository
{
    private const string SelectColumns =
        """
        id, name, status, description, repository_path, build_command, run_command,
        test_command, documentation_paths, ai_context_paths, preferred_ai_provider,
        active_task, current_objective, created_at, updated_at,
        shared_id, blocker_class, evidence, next_safest_step, prompt_rules,
        custom_log_patterns, github_repo_url, default_branch,
        shared_last_imported_at, shared_last_exported_at, shared_last_synced_stamp,
        launch_grace_seconds, launch_status_command, launch_required_services,
        launch_optional_services, launch_healthy_patterns, launch_failed_patterns,
        launch_service_log_command, launch_health_retries, launch_health_interval_seconds,
        launch_stop_command, launch_logs_command, launch_update_command, launch_metrics_command
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
                test_command, documentation_paths, ai_context_paths, preferred_ai_provider,
                active_task, current_objective, created_at, updated_at,
                shared_id, blocker_class, evidence, next_safest_step, prompt_rules,
                custom_log_patterns, github_repo_url, default_branch,
                shared_last_imported_at, shared_last_exported_at, shared_last_synced_stamp,
                launch_grace_seconds, launch_status_command, launch_required_services,
                launch_optional_services, launch_healthy_patterns, launch_failed_patterns,
                launch_service_log_command, launch_health_retries, launch_health_interval_seconds,
                launch_stop_command, launch_logs_command, launch_update_command, launch_metrics_command)
            VALUES (
                $name, $status, $description, $repository_path, $build_command, $run_command,
                $test_command, $documentation_paths, $ai_context_paths, $preferred_ai_provider,
                $active_task, $current_objective, $created_at, $updated_at,
                $shared_id, $blocker_class, $evidence, $next_safest_step, $prompt_rules,
                $custom_log_patterns, $github_repo_url, $default_branch,
                $shared_last_imported_at, $shared_last_exported_at, $shared_last_synced_stamp,
                $launch_grace_seconds, $launch_status_command, $launch_required_services,
                $launch_optional_services, $launch_healthy_patterns, $launch_failed_patterns,
                $launch_service_log_command, $launch_health_retries, $launch_health_interval_seconds,
                $launch_stop_command, $launch_logs_command, $launch_update_command, $launch_metrics_command);
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
                documentation_paths = $documentation_paths, ai_context_paths = $ai_context_paths,
                preferred_ai_provider = $preferred_ai_provider, active_task = $active_task,
                current_objective = $current_objective, created_at = $created_at, updated_at = $updated_at,
                shared_id = $shared_id, blocker_class = $blocker_class, evidence = $evidence,
                next_safest_step = $next_safest_step, prompt_rules = $prompt_rules,
                custom_log_patterns = $custom_log_patterns, github_repo_url = $github_repo_url,
                default_branch = $default_branch, shared_last_imported_at = $shared_last_imported_at,
                shared_last_exported_at = $shared_last_exported_at,
                shared_last_synced_stamp = $shared_last_synced_stamp,
                launch_grace_seconds = $launch_grace_seconds,
                launch_status_command = $launch_status_command,
                launch_required_services = $launch_required_services,
                launch_optional_services = $launch_optional_services,
                launch_healthy_patterns = $launch_healthy_patterns,
                launch_failed_patterns = $launch_failed_patterns,
                launch_service_log_command = $launch_service_log_command,
                launch_health_retries = $launch_health_retries,
                launch_health_interval_seconds = $launch_health_interval_seconds,
                launch_stop_command = $launch_stop_command,
                launch_logs_command = $launch_logs_command,
                launch_update_command = $launch_update_command,
                launch_metrics_command = $launch_metrics_command
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

    public void UpdateSharedSync(Project project)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE projects SET
                shared_id = $shared_id,
                shared_last_imported_at = $imported,
                shared_last_exported_at = $exported,
                shared_last_synced_stamp = $stamp
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$shared_id", SqliteMapping.ToDbNullable(project.SharedId));
        command.Parameters.AddWithValue("$imported", SqliteMapping.ToDb(project.SharedLastImportedAt));
        command.Parameters.AddWithValue("$exported", SqliteMapping.ToDb(project.SharedLastExportedAt));
        command.Parameters.AddWithValue("$stamp", SqliteMapping.ToDb(project.SharedLastSyncedStamp));
        command.Parameters.AddWithValue("$id", project.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Bumps updated_at without touching other fields (e.g. after appending history).</summary>
    public void Touch(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE projects SET updated_at = $now WHERE id = $id;";
        command.Parameters.AddWithValue("$now", SqliteMapping.ToDb(DateTimeOffset.Now));
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
        command.Parameters.AddWithValue("$documentation_paths", SqliteMapping.ToJson(p.DocumentationPaths));
        command.Parameters.AddWithValue("$ai_context_paths", SqliteMapping.ToJson(p.AiContextPaths));
        command.Parameters.AddWithValue("$preferred_ai_provider", SqliteMapping.ToDbNullable(p.PreferredAiProvider));
        command.Parameters.AddWithValue("$active_task", SqliteMapping.ToDbNullable(p.ActiveTask));
        command.Parameters.AddWithValue("$current_objective", SqliteMapping.ToDbNullable(p.CurrentObjective));
        command.Parameters.AddWithValue("$created_at", SqliteMapping.ToDb(p.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", SqliteMapping.ToDb(p.UpdatedAt));
        command.Parameters.AddWithValue("$shared_id", SqliteMapping.ToDbNullable(p.SharedId));
        command.Parameters.AddWithValue("$blocker_class", SqliteMapping.ToDbNullable(p.BlockerClass));
        command.Parameters.AddWithValue("$evidence", SqliteMapping.ToDbNullable(p.Evidence));
        command.Parameters.AddWithValue("$next_safest_step", SqliteMapping.ToDbNullable(p.NextSafestStep));
        command.Parameters.AddWithValue("$prompt_rules", SqliteMapping.ToJson(p.PromptRules));
        command.Parameters.AddWithValue("$custom_log_patterns", SqliteMapping.ToJson(p.CustomLogPatterns));
        command.Parameters.AddWithValue("$github_repo_url", SqliteMapping.ToDbNullable(p.GitHubRepoUrl));
        command.Parameters.AddWithValue("$default_branch", SqliteMapping.ToDbNullable(p.DefaultBranch));
        command.Parameters.AddWithValue("$shared_last_imported_at", SqliteMapping.ToDb(p.SharedLastImportedAt));
        command.Parameters.AddWithValue("$shared_last_exported_at", SqliteMapping.ToDb(p.SharedLastExportedAt));
        command.Parameters.AddWithValue("$shared_last_synced_stamp", SqliteMapping.ToDb(p.SharedLastSyncedStamp));
        command.Parameters.AddWithValue("$launch_grace_seconds", SqliteMapping.ToDbNullable(p.Launch.StartupGraceSeconds));
        command.Parameters.AddWithValue("$launch_status_command", SqliteMapping.ToDbNullable(p.Launch.StatusCommand));
        command.Parameters.AddWithValue("$launch_required_services", SqliteMapping.ToJson(p.Launch.RequiredServices));
        command.Parameters.AddWithValue("$launch_optional_services", SqliteMapping.ToJson(p.Launch.OptionalServices));
        command.Parameters.AddWithValue("$launch_healthy_patterns", SqliteMapping.ToJson(p.Launch.HealthyPatterns));
        command.Parameters.AddWithValue("$launch_failed_patterns", SqliteMapping.ToJson(p.Launch.FailedPatterns));
        command.Parameters.AddWithValue("$launch_service_log_command", SqliteMapping.ToDbNullable(p.Launch.ServiceLogCommand));
        command.Parameters.AddWithValue("$launch_health_retries", SqliteMapping.ToDbNullable(p.Launch.HealthCheckRetries));
        command.Parameters.AddWithValue("$launch_health_interval_seconds", SqliteMapping.ToDbNullable(p.Launch.HealthCheckIntervalSeconds));
        command.Parameters.AddWithValue("$launch_stop_command", SqliteMapping.ToDbNullable(p.Launch.StopCommand));
        command.Parameters.AddWithValue("$launch_logs_command", SqliteMapping.ToDbNullable(p.Launch.LogsCommand));
        command.Parameters.AddWithValue("$launch_update_command", SqliteMapping.ToDbNullable(p.Launch.UpdateCommand));
        command.Parameters.AddWithValue("$launch_metrics_command", SqliteMapping.ToDbNullable(p.Launch.MetricsCommand));
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
        DocumentationPaths = SqliteMapping.FromJson(reader.GetString(8)),
        AiContextPaths = SqliteMapping.FromJson(reader.GetString(9)),
        PreferredAiProvider = reader.GetNullableString(10),
        ActiveTask = reader.GetNullableString(11),
        CurrentObjective = reader.GetNullableString(12),
        CreatedAt = reader.GetDateTimeOffset(13),
        UpdatedAt = reader.GetDateTimeOffset(14),
        SharedId = reader.GetNullableString(15),
        BlockerClass = reader.GetNullableString(16),
        Evidence = reader.GetNullableString(17),
        NextSafestStep = reader.GetNullableString(18),
        // Nullable reads: rows written by the pre-rewrite lineage hold NULL here.
        PromptRules = SqliteMapping.FromJson(reader.GetNullableString(19)),
        CustomLogPatterns = SqliteMapping.FromJson(reader.GetNullableString(20)),
        GitHubRepoUrl = reader.GetNullableString(21),
        DefaultBranch = reader.GetNullableString(22),
        SharedLastImportedAt = reader.GetNullableDateTimeOffset(23),
        SharedLastExportedAt = reader.GetNullableDateTimeOffset(24),
        SharedLastSyncedStamp = reader.GetNullableDateTimeOffset(25),
        Launch = new LaunchProfile
        {
            StartupGraceSeconds = reader.GetNullableInt32(26) ?? 5,
            StatusCommand = reader.GetNullableString(27),
            RequiredServices = SqliteMapping.FromJson(reader.GetNullableString(28)),
            OptionalServices = SqliteMapping.FromJson(reader.GetNullableString(29)),
            HealthyPatterns = SqliteMapping.FromJson(reader.GetNullableString(30)),
            FailedPatterns = SqliteMapping.FromJson(reader.GetNullableString(31)),
            ServiceLogCommand = reader.GetNullableString(32),
            HealthCheckRetries = reader.GetNullableInt32(33) ?? 3,
            HealthCheckIntervalSeconds = reader.GetNullableInt32(34) ?? 5,
            StopCommand = reader.GetNullableString(35),
            LogsCommand = reader.GetNullableString(36),
            UpdateCommand = reader.GetNullableString(37),
            MetricsCommand = reader.GetNullableString(38),
        },
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
