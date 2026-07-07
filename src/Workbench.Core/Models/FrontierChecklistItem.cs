namespace Workbench.Core.Models;

public enum ChecklistItemStatus
{
    Pending,
    Done,
    Blocked,
}

/// <summary>
/// One concrete step under a frontier — e.g. "confirm ABI for func_8001FBE4"
/// or "capture packet trace for opcode 0x12". Purely user-tracked: nothing
/// here is inferred or estimated, only the status the user sets.
/// </summary>
public sealed class FrontierChecklistItem
{
    public long Id { get; set; }

    public long FrontierId { get; set; }

    public string Text { get; set; } = string.Empty;

    public ChecklistItemStatus Status { get; set; } = ChecklistItemStatus.Pending;

    /// <summary>Display order within the frontier.</summary>
    public int SortOrder { get; set; }
}
