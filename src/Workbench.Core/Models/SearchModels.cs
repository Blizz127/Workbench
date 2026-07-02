namespace Workbench.Core.Models;

/// <summary>Which subsystem a search hit came from.</summary>
public enum SearchDomain
{
    Project,
    Note,
    Milestone,
    Timeline,
    Frontier,
}

/// <summary>One hit from the global search.</summary>
public sealed record SearchResult(
    long ProjectId,
    string ProjectName,
    SearchDomain Domain,
    long ItemId,
    string Title,
    string Snippet);
