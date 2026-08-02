namespace Workbench.Web;

/// <summary>
/// In-memory record of every mobile action attempt — attempted, blocked,
/// rejected, succeeded, or failed. Bounded to the most recent 200 entries
/// (a restart simply starts a fresh journal, exactly like sessions) and
/// thread-safe; rendered on the project detail page.
/// </summary>
public sealed class ActionJournal
{
    private const int Capacity = 200;

    private readonly object _gate = new();
    private readonly LinkedList<ActionJournalEntry> _entries = new(); // newest first

    public void Record(ActionJournalEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity)
                _entries.RemoveLast();
        }
    }

    /// <summary>Newest first, one project only.</summary>
    public IReadOnlyList<ActionJournalEntry> Recent(long projectId, int count)
    {
        lock (_gate)
        {
            var result = new List<ActionJournalEntry>(Math.Min(count, _entries.Count));
            foreach (var entry in _entries)
            {
                if (result.Count >= count)
                    break;
                if (entry.ProjectId == projectId)
                    result.Add(entry);
            }

            return result;
        }
    }
}
