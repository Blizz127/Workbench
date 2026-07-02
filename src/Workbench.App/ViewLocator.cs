using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Workbench.App.ViewModels;

namespace Workbench.App;

/// <summary>Maps ViewModels to Views by convention: ViewModels.FooViewModel → Views.FooView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);
        if (type is null)
            return new TextBlock { Text = "View not found: " + name };

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
