using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Workbench.App.Converters;

/// <summary>
/// Resolves brushes from the theme dictionaries at convert time, so a theme
/// switch applies without re-binding. Falls back to the dark palette when no
/// application resources are available (e.g. design time).
/// </summary>
internal static class ThemeBrush
{
    private static readonly Dictionary<string, IBrush> Fallbacks = new(StringComparer.Ordinal)
    {
        ["StatusActiveBrush"] = new SolidColorBrush(Color.Parse("#3FA060")),
        ["StatusWaitingBrush"] = new SolidColorBrush(Color.Parse("#C29A3C")),
        ["StatusBrokenBrush"] = new SolidColorBrush(Color.Parse("#D96A5F")),
        ["StatusResearchBrush"] = new SolidColorBrush(Color.Parse("#5D97D1")),
        ["StatusCompleteBrush"] = new SolidColorBrush(Color.Parse("#78808A")),
        ["NeutralBrush"] = new SolidColorBrush(Color.Parse("#8B929C")),
        ["TextMutedBrush"] = new SolidColorBrush(Color.Parse("#8B929C")),
        ["SuccessBrush"] = new SolidColorBrush(Color.Parse("#4CC38A")),
        ["DangerBrush"] = new SolidColorBrush(Color.Parse("#F16A50")),
    };

    public static IBrush Lookup(string key)
    {
        var app = Application.Current;
        if (app is not null
            && app.TryGetResource(key, app.ActualThemeVariant ?? ThemeVariant.Dark, out var value)
            && value is IBrush brush)
            return brush;

        return Fallbacks[key];
    }
}

/// <summary>Maps a project status name to its accent color for chips and badges.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    private static readonly Dictionary<string, string> ResourceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Active"] = "StatusActiveBrush",
        ["Waiting"] = "StatusWaitingBrush",
        ["Broken"] = "StatusBrokenBrush",
        ["Research"] = "StatusResearchBrush",
        ["Complete"] = "StatusCompleteBrush",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeBrush.Lookup(
            value is string status && ResourceKeys.TryGetValue(status, out var key) ? key : "NeutralBrush");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Green when true, red when false, muted when null (no run yet) — build/test outcome badges.</summary>
public sealed class OutcomeToBrushConverter : IValueConverter
{
    public static readonly OutcomeToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeBrush.Lookup(value is true ? "SuccessBrush" : value is false ? "DangerBrush" : "TextMutedBrush");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
