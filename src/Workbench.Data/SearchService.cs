using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data;

/// <summary>
/// Case-insensitive LIKE search across projects, notes, milestones, timeline
/// entries, and frontiers, in one round trip per domain.
/// </summary>
public sealed class SearchService(WorkbenchDatabase database) : ISearchService
{
    public IReadOnlyList<SearchResult> Search(string query, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var pattern = $"%{EscapeLike(query.Trim())}%";
        var results = new List<SearchResult>();

        using var connection = database.OpenConnection();

        // Each SELECT yields: project_id, project_name, item_id, title, snippet.
        results.AddRange(Run(connection, SearchDomain.Project, pattern, limit,
            """
            SELECT p.id, p.name, p.id, p.name,
                   COALESCE(p.description, COALESCE(p.active_task, ''))
            FROM projects p
            WHERE p.name LIKE $q ESCAPE '\'
               OR p.description LIKE $q ESCAPE '\'
               OR p.active_task LIKE $q ESCAPE '\'
               OR p.current_objective LIKE $q ESCAPE '\'
               OR EXISTS (SELECT 1 FROM project_tags t WHERE t.project_id = p.id AND t.tag LIKE $q ESCAPE '\')
            LIMIT $limit;
            """));

        results.AddRange(Run(connection, SearchDomain.Note, pattern, limit,
            """
            SELECT n.project_id, p.name, n.id, n.title, substr(n.content, 1, 160)
            FROM notes n JOIN projects p ON p.id = n.project_id
            WHERE n.title LIKE $q ESCAPE '\' OR n.content LIKE $q ESCAPE '\'
            ORDER BY n.updated_at DESC LIMIT $limit;
            """));

        results.AddRange(Run(connection, SearchDomain.Milestone, pattern, limit,
            """
            SELECT m.project_id, p.name, m.id, m.title, COALESCE(m.notes, '')
            FROM milestones m JOIN projects p ON p.id = m.project_id
            WHERE m.title LIKE $q ESCAPE '\' OR m.notes LIKE $q ESCAPE '\'
            LIMIT $limit;
            """));

        results.AddRange(Run(connection, SearchDomain.Timeline, pattern, limit,
            """
            SELECT e.project_id, p.name, e.id, e.message, COALESCE(e.detail, '')
            FROM timeline_entries e JOIN projects p ON p.id = e.project_id
            WHERE e.message LIKE $q ESCAPE '\' OR e.detail LIKE $q ESCAPE '\'
            ORDER BY e.timestamp DESC LIMIT $limit;
            """));

        results.AddRange(Run(connection, SearchDomain.Frontier, pattern, limit,
            """
            SELECT f.project_id, p.name, f.id, f.title, COALESCE(f.notes, '')
            FROM frontiers f JOIN projects p ON p.id = f.project_id
            WHERE f.title LIKE $q ESCAPE '\' OR f.notes LIKE $q ESCAPE '\'
            LIMIT $limit;
            """));

        return results.Take(limit).ToList();
    }

    private static List<SearchResult> Run(
        SqliteConnection connection, SearchDomain domain, string pattern, int limit, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$q", pattern);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<SearchResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SearchResult(
                ProjectId: reader.GetInt64(0),
                ProjectName: reader.GetString(1),
                Domain: domain,
                ItemId: reader.GetInt64(2),
                Title: reader.GetString(3),
                Snippet: reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }
        return results;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
