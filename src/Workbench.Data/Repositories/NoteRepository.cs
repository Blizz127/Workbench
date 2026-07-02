using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class NoteRepository(WorkbenchDatabase database) : INoteRepository
{
    private const string SelectColumns = "id, project_id, title, content, created_at, updated_at";

    public IReadOnlyList<Note> GetForProject(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM notes WHERE project_id = $pid ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$pid", projectId);

        var results = new List<Note>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public Note? GetById(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM notes WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public long Add(Note note)
    {
        var now = DateTimeOffset.Now;
        note.CreatedAt = note.CreatedAt == default ? now : note.CreatedAt;
        note.UpdatedAt = now;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO notes (project_id, title, content, created_at, updated_at)
            VALUES ($pid, $title, $content, $created_at, $updated_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$pid", note.ProjectId);
        command.Parameters.AddWithValue("$title", note.Title);
        command.Parameters.AddWithValue("$content", note.Content);
        command.Parameters.AddWithValue("$created_at", SqliteMapping.ToDb(note.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", SqliteMapping.ToDb(note.UpdatedAt));

        note.Id = (long)command.ExecuteScalar()!;
        return note.Id;
    }

    public void Update(Note note)
    {
        note.UpdatedAt = DateTimeOffset.Now;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE notes SET title = $title, content = $content, updated_at = $updated_at WHERE id = $id;";
        command.Parameters.AddWithValue("$title", note.Title);
        command.Parameters.AddWithValue("$content", note.Content);
        command.Parameters.AddWithValue("$updated_at", SqliteMapping.ToDb(note.UpdatedAt));
        command.Parameters.AddWithValue("$id", note.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM notes WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static Note Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        Title = reader.GetString(2),
        Content = reader.GetString(3),
        CreatedAt = reader.GetDateTimeOffset(4),
        UpdatedAt = reader.GetDateTimeOffset(5),
    };
}
