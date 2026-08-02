using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Resolves and gates <see cref="SharedStateMode"/>. The mode is persisted as a
/// string in settings.json; when the field is absent (a pre-mode install) the
/// effective mode is resolved at read time and never stamped back into the
/// file. Gating lives in the view models — <see cref="ISharedStateService"/>
/// itself is mode-agnostic.
/// </summary>
public static class SharedStateModeRules
{
    /// <summary>
    /// The effective mode: the persisted value when present; otherwise the
    /// migration default — Enabled for existing installs with an explicitly
    /// configured shared folder (preserving their current behavior), Disabled
    /// otherwise (the new-install default). An unrecognized value counts as
    /// absent. <paramref name="configuredSharedFolder"/> is the raw setting,
    /// not the resolved-with-default path.
    /// </summary>
    public static SharedStateMode Resolve(string? persistedValue, string? configuredSharedFolder)
    {
        if (Enum.TryParse<SharedStateMode>(persistedValue, ignoreCase: true, out var mode))
            return mode;

        return string.IsNullOrWhiteSpace(configuredSharedFolder)
            ? SharedStateMode.Disabled
            : SharedStateMode.Enabled;
    }

    /// <summary>Automatic folder probes (construction/refresh warnings) run only in Enabled mode.</summary>
    public static bool AutomaticChecks(SharedStateMode mode) => mode is SharedStateMode.Enabled;

    /// <summary>Sync badges (dashboard cards, top bar, workspace header) are computed only in Enabled mode.</summary>
    public static bool Badges(SharedStateMode mode) => mode is SharedStateMode.Enabled;

    /// <summary>Shared import/export UI (per-project section, Import All, Reload) shows in Manual and Enabled modes.</summary>
    public static bool ActionsVisible(SharedStateMode mode) => mode is not SharedStateMode.Disabled;

    /// <summary>
    /// Shared files for badge computation. Outside Enabled mode this returns an
    /// empty list WITHOUT touching the service — no folder access happens at all.
    /// </summary>
    public static IReadOnlyList<SharedStateFileInfo> LoadAllForBadges(
        ISharedStateService sharedState, SharedStateMode mode, string? sharedFolder) =>
        Badges(mode) && sharedFolder is not null
            ? sharedState.LoadAll(sharedFolder)
            : [];
}
