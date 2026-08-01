using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Application settings page: the Shared State Folder (the user's Dropbox
/// folder — Dropbox syncs it between machines externally; Workbench never runs
/// sync commands), this machine's name, and the explicit shared-state actions.
/// Sync is always manual; nothing here runs in the background.
/// </summary>
public sealed partial class AppSettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _goBack;
    private readonly Action? _onSaved;

    [ObservableProperty]
    private string _sharedStateFolder;

    [ObservableProperty]
    private string _discoveryFolder;

    [ObservableProperty]
    private bool _discoveryEnabled;

    [ObservableProperty]
    private string _machineName;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>One-line folder status: project file count or why the folder is unavailable.</summary>
    [ObservableProperty]
    private string _sharedFolderSummary = string.Empty;

    /// <summary>Result lines from the last reload/import/export action.</summary>
    public ObservableCollection<string> ActivityLog { get; } = [];

    [ObservableProperty]
    private string _theme;

    [ObservableProperty]
    private bool _reducedMotion;

    /// <summary>Available UI themes (persisted as-is in settings.json).</summary>
    public string[] Themes { get; } = ["dark", "light"];

    public string SettingsPath => _services.AppSettings.SettingsPath;

    public AppSettingsViewModel(AppServices services, Action goBack, Action? onSaved = null)
    {
        _services = services;
        _goBack = goBack;
        _onSaved = onSaved;
        _sharedStateFolder = services.AppSettings.SharedStateFolder ?? string.Empty;
        _discoveryFolder = services.AppSettings.DiscoveryFolder;
        _discoveryEnabled = services.AppSettings.Settings.DiscoveryEnabled;
        _machineName = services.AppSettings.MachineName;
        _theme = services.AppSettings.Settings.Theme;
        _reducedMotion = services.AppSettings.Settings.ReducedMotion;
        RefreshSharedFolderStatus();
    }

    [RelayCommand]
    private void GoBack() => _goBack();

    [RelayCommand]
    private void SaveSettings()
    {
        _services.AppSettings.Settings.SharedStateFolder =
            string.IsNullOrWhiteSpace(SharedStateFolder) ? null : SharedStateFolder.Trim();
        _services.AppSettings.Settings.MachineName =
            string.IsNullOrWhiteSpace(MachineName) || MachineName.Trim() == Environment.MachineName
                ? null
                : MachineName.Trim();
        _services.AppSettings.Settings.Theme = Theme;
        _services.AppSettings.Settings.ReducedMotion = ReducedMotion;
        _services.AppSettings.Settings.DiscoveryFolder =
            string.IsNullOrWhiteSpace(DiscoveryFolder) ? null : DiscoveryFolder.Trim();
        _services.AppSettings.Settings.DiscoveryEnabled = DiscoveryEnabled;
        _services.AppSettings.Save();
        ApplyTheme();
        _onSaved?.Invoke();
        MachineName = _services.AppSettings.MachineName;
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}";
        RefreshSharedFolderStatus();
    }

    /// <summary>Applies the selected theme immediately (still requires Save to persist).</summary>
    private void ApplyTheme()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant =
                string.Equals(Theme, "light", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
        }
    }

    /// <summary>Re-reads the shared folder and reports every project file's state against local.</summary>
    [RelayCommand]
    private void ReloadSharedState()
    {
        if (RequireFolder() is not { } folder)
            return;

        ActivityLog.Clear();
        var files = _services.SharedState.LoadAll(folder);
        if (files.Count == 0)
        {
            ActivityLog.Add($"No project files found under {_services.SharedState.GetProjectsFolder(folder)}.");
        }

        var projects = _services.Projects.GetAll();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FilePath);
            if (_services.SharedState.MatchProject(projects, file.State) is not { } local)
            {
                ActivityLog.Add($"{name}: not registered here — Import All will create it.");
                continue;
            }

            var preview = _services.SharedState.PreviewImport(file.State, local, _services.AppSettings.MachineName);
            ActivityLog.Add($"{name}: '{local.Name}' — " + (preview switch
            {
                { HasConflict: true } => "CONFLICT: both local and shared changed. Review in the project's Settings tab.",
                { SharedChangedSinceSync: true } => $"shared is newer ({file.State.UpdatedAt:yyyy-MM-dd HH:mm} by {file.State.UpdatedBy ?? "unknown"}).",
                { LocalChangedSinceSync: true } => "local has unexported changes.",
                _ => "up to date.",
            }));
        }

        foreach (var warning in RefreshSharedFolderStatus().Warnings)
            ActivityLog.Add("⚠ " + warning);
    }

    /// <summary>Opens the shared folder in the OS file manager.</summary>
    [RelayCommand]
    private void OpenSharedFolder()
    {
        var folder = SharedStateFolder.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            StatusMessage = folder.Length == 0
                ? "Set the Shared State Folder first."
                : $"Folder does not exist: {folder}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open folder: {ex.Message}";
        }
    }

    /// <summary>Creates local projects for shared files that don't exist here. Never overwrites.</summary>
    [RelayCommand]
    private void ImportAllProjects()
    {
        if (RequireFolder() is not { } folder)
            return;

        ActivityLog.Clear();
        var summary = _services.SharedState.ImportAll(folder, _services.AppSettings.MachineName);
        Report(summary.Applied, summary.Skipped, summary.Warnings);
    }

    /// <summary>Exports every local project; skips (with a warning) any whose shared file is newer.</summary>
    [RelayCommand]
    private void ExportAllProjects()
    {
        if (RequireFolder() is not { } folder)
            return;

        ActivityLog.Clear();
        var summary = _services.SharedState.ExportAll(folder, _services.AppSettings.MachineName);
        Report(summary.Applied, summary.Skipped, summary.Warnings);
    }

    private void Report(IEnumerable<string> applied, IEnumerable<string> skipped, IEnumerable<string> warnings)
    {
        foreach (var line in applied)
            ActivityLog.Add("✓ " + line);
        foreach (var line in skipped)
            ActivityLog.Add("· " + line);
        foreach (var line in warnings)
            ActivityLog.Add("⚠ " + line);
        if (ActivityLog.Count == 0)
            ActivityLog.Add("Nothing to do.");
    }

    private string? RequireFolder()
    {
        var folder = SharedStateFolder.Trim();
        if (folder.Length == 0)
        {
            StatusMessage = "Set the Shared State Folder first.";
            return null;
        }

        // The check also creates projects/ when the folder is valid and writable.
        var status = _services.SharedState.CheckSharedFolder(folder);
        if (!status.Exists)
        {
            StatusMessage = $"Folder does not exist (Dropbox may not have synced it): {folder}";
            return null;
        }

        if (!status.Writable)
        {
            StatusMessage = $"Folder is not writable: {folder}";
            return null;
        }

        if (folder != _services.AppSettings.Settings.SharedStateFolder)
            SaveSettings();

        StatusMessage = string.Empty;
        return folder;
    }

    /// <summary>Probes the shared folder and updates the summary line; returns the probe result.</summary>
    private SharedFolderStatus RefreshSharedFolderStatus()
    {
        var folder = SharedStateFolder.Trim();
        if (folder.Length == 0)
        {
            SharedFolderSummary = "No shared folder set.";
            return new SharedFolderStatus { Folder = folder };
        }

        var status = _services.SharedState.CheckSharedFolder(folder);
        SharedFolderSummary = status.Exists
            ? $"{status.ProjectFileCount} shared project file(s) under {_services.SharedState.GetProjectsFolder(folder)}"
            : $"Folder not available — Dropbox may not have synced it: {folder}";
        return status;
    }
}
