using Avalonia;
using Avalonia.Controls;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is not MainWindowViewModel vm)
            return;

        var settings = vm.AppSettings.Settings;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        if (settings.WindowX is { } x && settings.WindowY is { } y && IsOnScreen(x, y))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var settings = vm.AppSettings.Settings;
            // Maximized/minimized bounds are transient; keep the last normal-state bounds.
            if (WindowState == WindowState.Normal)
            {
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
                settings.WindowX = Position.X;
                settings.WindowY = Position.Y;
            }
            vm.AppSettings.Save();
        }
        base.OnClosing(e);
    }

    // A saved position counts as sane if a corner of the window would land on a visible working area.
    private bool IsOnScreen(int x, int y) =>
        Screens.All.Any(s => s.WorkingArea.Intersects(new PixelRect(x, y, 50, 50)));
}
