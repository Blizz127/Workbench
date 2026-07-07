using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class FrontierChecklistRepository(WorkbenchDatabase database) : IFrontierChecklistRepository
{
    public IReadOnlyList<FrontierChecklistItem> GetForFrontier(long frontierId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, frontier_id, text, status, sort_order
            FROM frontier_checklist_items WHERE frontier_id = $fid ORDER BY sort_order, id;
            """;
        command.Parameters.AddWithValue("$fid", frontierId);

        var results = new List<FrontierChecklistItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public long Add(FrontierChecklistItem item)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO frontier_checklist_items (frontier_id, text, status, sort_order)
            VALUES ($fid, $text, $status, $order);
            SELECT last_insert_rowid();
            """;
        Bind(command, item);
        item.Id = (long)command.ExecuteScalar()!;
        return item.Id;
    }

    public void Update(FrontierChecklistItem item)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE frontier_checklist_items SET text = $text, status = $status, sort_order = $order
            WHERE id = $id;
            """;
        Bind(command, item);
        command.Parameters.AddWithValue("$id", item.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM frontier_checklist_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, FrontierChecklistItem item)
    {
        command.Parameters.AddWithValue("$fid", item.FrontierId);
        command.Parameters.AddWithValue("$text", item.Text);
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$order", item.SortOrder);
    }

    private static FrontierChecklistItem Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        FrontierId = reader.GetInt64(1),
        Text = reader.GetString(2),
        Status = reader.GetEnum<ChecklistItemStatus>(3),
        SortOrder = reader.GetInt32(4),
    };
}
