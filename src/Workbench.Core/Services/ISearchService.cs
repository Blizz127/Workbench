using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Full-application search across projects, notes, milestones, timeline, and frontiers.</summary>
public interface ISearchService
{
    IReadOnlyList<SearchResult> Search(string query, int limit = 100);
}
