namespace Workbench.Core.Models;

/// <summary>
/// A user-defined command beyond the fixed build/run/test/matching-build
/// slots on <see cref="Project"/> — e.g. a symbol-diff tool, a specific test
/// scenario, or a packet-capture replay. Executed the same way as any other
/// configured command: explicitly, on demand, never automatically.
/// </summary>
public sealed class ProjectCommand
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CommandText { get; set; } = string.Empty;

    /// <summary>Display order within the project.</summary>
    public int SortOrder { get; set; }
}
