using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public sealed partial class RunView : UserControl
{
    public RunView()
    {
        InitializeComponent();
        // Follow mode: keep the panel pinned to the newest output line.
        OutputBox.TextChanged += (_, _) =>
        {
            if (DataContext is HistoryViewModel { FollowOutput: true })
            {
                Dispatcher.UIThread.Post(
                    () => OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0,
                    DispatcherPriority.Background);
            }
        };
    }

    private async void OnCopyLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm
            && vm.SelectedLogText.Length > 0
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.SelectedLogText);
    }

    private void OnOpenFullLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel vm || vm.SelectedLogText.Length == 0)
            return;

        try
        {
            var path = Path.Combine(
                Path.GetTempPath(), $"workbench-output-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, vm.SelectedLogText);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Temp write failed or nothing handles .log files — nothing sensible to surface here.
        }
    }
}
