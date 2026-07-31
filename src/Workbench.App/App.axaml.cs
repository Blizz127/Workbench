using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Workbench.App.ViewModels;
using Workbench.App.Views;
using Workbench.Data.SampleData;

namespace Workbench.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // CommunityToolkit.Mvvm handles validation; remove Avalonia's
            // DataAnnotations plugin so errors are not reported twice.
            var pluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
            foreach (var plugin in pluginsToRemove)
                BindingPlugins.DataValidators.Remove(plugin);

            var services = new AppServices();
            RequestedThemeVariant =
                string.Equals(services.AppSettings.Settings.Theme, "light", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
            XenogearsSeeder.SeedIfEmpty(services.Database);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(services),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
