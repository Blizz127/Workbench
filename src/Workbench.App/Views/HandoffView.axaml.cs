using Avalonia.Controls;
using Avalonia.Interactivity;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public sealed partial class HandoffView : UserControl
{
    public HandoffView() => InitializeComponent();

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HandoffViewModel vm)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(vm.HandoffMarkdown);
    }
}
