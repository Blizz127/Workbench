namespace Workbench.Core.Models;

/// <summary>
/// Lifecycle state of a project as shown on the dashboard filters.
/// </summary>
public enum ProjectStatus
{
    Active,
    Waiting,
    Broken,
    Research,
    Complete,
}
