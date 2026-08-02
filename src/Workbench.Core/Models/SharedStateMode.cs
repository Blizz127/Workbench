namespace Workbench.Core.Models;

/// <summary>
/// Whether the Dropbox-folder Shared State sync is active. Shared State is
/// optional: the local SQLite database and configs stay authoritative in every
/// mode, and no mode ever deletes shared files or the configured folder.
/// </summary>
public enum SharedStateMode
{
    /// <summary>
    /// No automatic folder checks, no badges, no warnings, no shared-state UI
    /// outside App Settings (where an explicit portable-backup export remains).
    /// </summary>
    Disabled,

    /// <summary>
    /// Import/export actions are visible and work on explicit request; nothing
    /// is automatic — no startup checks, no badges, no background probing.
    /// </summary>
    Manual,

    /// <summary>Full behavior: folder probes, sync badges, and import/export actions.</summary>
    Enabled,
}
