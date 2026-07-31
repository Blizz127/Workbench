namespace Workbench.Core.Models;

/// <summary>
/// Result of probing the shared state folder (see
/// <see cref="Services.ISharedStateService.CheckSharedFolder"/>). The folder is
/// synced externally by Dropbox — Workbench only reads/writes
/// <c>projects/*.workbench.json</c> inside it.
/// </summary>
public sealed class SharedFolderStatus
{
    public required string Folder { get; init; }

    /// <summary>The folder exists (Dropbox has synced/mounted it).</summary>
    public bool Exists { get; set; }

    /// <summary>A temp probe file could be written and deleted inside the folder.</summary>
    public bool Writable { get; set; }

    /// <summary>The <c>projects/</c> subfolder exists (created by the check when missing).</summary>
    public bool ProjectsFolderExists { get; set; }

    /// <summary>Number of <c>*.workbench.json</c> files under <c>projects/</c>.</summary>
    public int ProjectFileCount { get; set; }

    /// <summary>Problems found: missing/unwritable folder, Dropbox conflicted copies, unreadable files.</summary>
    public List<string> Warnings { get; } = [];
}
