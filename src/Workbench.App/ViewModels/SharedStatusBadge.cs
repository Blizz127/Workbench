using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// The shared-state badge rules used by the dashboard cards, the shell top
/// bar, and the workspace header. Null means in sync (or not shared).
/// </summary>
internal static class SharedStatusBadge
{
    public static string? For(
        ISharedStateService sharedState,
        Project project,
        IReadOnlyList<SharedStateFileInfo> sharedFiles,
        string machineName)
    {
        if (project.SharedId is null)
            return null;

        var file = sharedFiles.FirstOrDefault(f => f.State.Id == project.SharedId);
        if (file is null)
            return "shared file missing";

        var preview = sharedState.PreviewImport(file.State, project, machineName);
        return preview switch
        {
            { HasConflict: true } => "conflict with shared",
            { SharedChangedSinceSync: true } => "shared is newer",
            { LocalChangedSinceSync: true } => "not exported",
            _ => null,
        };
    }

    /// <summary>Phase C convention: conflict/newer badges render as warnings; the rest are neutral.</summary>
    public static bool IsWarning(string? badge) => badge is "conflict with shared" or "shared is newer";
}
