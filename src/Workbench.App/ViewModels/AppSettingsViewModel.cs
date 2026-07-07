using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Workbench.App.ViewModels;

/// <summary>
/// Application settings page: the Shared State Folder (a directory synced by
/// Google Drive, Syncthing, Dropbox, OneDrive, a NAS — or any plain folder),
/// this machine's name, and the explicit shared-state actions. Sync is always
/// manual; nothing here runs in the background.
/// </summary>
public sealed partial class AppSettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _goBack;

    [ObservableProperty]
    private string _sharedStateFolder;

    [ObservableProperty]
    private string _machineName;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Result lines from the last reload/import/export action.</summary>
    public ObservableCollection<string> ActivityLog { get; } = [];

    public string SettingsPath => _services.AppSettings.SettingsPath;

    public AppSettingsViewModel(AppServices services, Action goBack)
    {
        _services = services;
        _goBack = goBack;
        _sharedStateFolder = services.AppSettings.Settings.SharedStateFolder ?? string.Empty;
        _machineName = services.AppSettings.MachineName;
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
        _services.AppSettings.Save();
        MachineName = _services.AppSettings.MachineName;
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}";
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
            return;
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

        if (!Directory.Exists(folder))
        {
            StatusMessage = $"Folder does not exist: {folder}";
            return null;
        }

        if (folder != _services.AppSettings.Settings.SharedStateFolder)
            SaveSettings();

        StatusMessage = string.Empty;
        return folder;
    }
}
