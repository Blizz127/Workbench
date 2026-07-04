using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class ProjectCommandRepository(WorkbenchDatabase database) : IProjectCommandRepository
{
    private const string SelectColumns = "id, project_id, name, command_text, sort_order";

    public IReadOnlyList<ProjectCommand> GetForProject(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM project_commands WHERE project_id = $pid ORDER BY sort_order, id;";
        command.Parameters.AddWithValue("$pid", projectId);

        var results = new List<ProjectCommand>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public long Add(ProjectCommand command)
    {
        using var connection = database.OpenConnection();
        using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText =
            """
            INSERT INTO project_commands (project_id, name, command_text, sort_order)
            VALUES ($pid, $name, $command_text, $order);
            SELECT last_insert_rowid();
            """;
        Bind(sqlCommand, command);
        command.Id = (long)sqlCommand.ExecuteScalar()!;
        return command.Id;
    }

    public void Update(ProjectCommand command)
    {
        using var connection = database.OpenConnection();
        using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText =
            "UPDATE project_commands SET name = $name, command_text = $command_text, sort_order = $order WHERE id = $id;";
        Bind(sqlCommand, command);
        sqlCommand.Parameters.AddWithValue("$id", command.Id);
        sqlCommand.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM project_commands WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand sqlCommand, ProjectCommand command)
    {
        sqlCommand.Parameters.AddWithValue("$pid", command.ProjectId);
        sqlCommand.Parameters.AddWithValue("$name", command.Name);
        sqlCommand.Parameters.AddWithValue("$command_text", command.CommandText);
        sqlCommand.Parameters.AddWithValue("$order", command.SortOrder);
    }

    private static ProjectCommand Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        Name = reader.GetString(2),
        CommandText = reader.GetString(3),
        SortOrder = reader.GetInt32(4),
    };
}
