using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class MilestoneRepository(WorkbenchDatabase database) : IMilestoneRepository
{
    public IReadOnlyList<Milestone> GetForProject(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, title, is_completed, completed_at, sort_order, notes
            FROM milestones WHERE project_id = $pid ORDER BY sort_order, id;
            """;
        command.Parameters.AddWithValue("$pid", projectId);

        var results = new List<Milestone>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public long Add(Milestone milestone)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO milestones (project_id, title, is_completed, completed_at, sort_order, notes)
            VALUES ($pid, $title, $done, $completed_at, $order, $notes);
            SELECT last_insert_rowid();
            """;
        Bind(command, milestone);
        milestone.Id = (long)command.ExecuteScalar()!;
        return milestone.Id;
    }

    public void Update(Milestone milestone)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE milestones SET title = $title, is_completed = $done, completed_at = $completed_at,
                sort_order = $order, notes = $notes
            WHERE id = $id;
            """;
        Bind(command, milestone);
        command.Parameters.AddWithValue("$id", milestone.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM milestones WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Milestone m)
    {
        command.Parameters.AddWithValue("$pid", m.ProjectId);
        command.Parameters.AddWithValue("$title", m.Title);
        command.Parameters.AddWithValue("$done", m.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$completed_at", SqliteMapping.ToDb(m.CompletedAt));
        command.Parameters.AddWithValue("$order", m.SortOrder);
        command.Parameters.AddWithValue("$notes", SqliteMapping.ToDbNullable(m.Notes));
    }

    private static Milestone Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        Title = reader.GetString(2),
        IsCompleted = reader.GetInt32(3) != 0,
        CompletedAt = reader.GetNullableDateTimeOffset(4),
        SortOrder = reader.GetInt32(5),
        Notes = reader.GetNullableString(6),
    };
}
