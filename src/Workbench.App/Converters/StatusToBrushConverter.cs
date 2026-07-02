using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Workbench.App.Converters;

/// <summary>Maps a project status name to its accent color for chips and badges.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    private static readonly Dictionary<string, IBrush> Brushes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Active"] = new SolidColorBrush(Color.Parse("#2E7D46")),
        ["Waiting"] = new SolidColorBrush(Color.Parse("#8A6D1A")),
        ["Broken"] = new SolidColorBrush(Color.Parse("#9E3A38")),
        ["Research"] = new SolidColorBrush(Color.Parse("#2C5F8A")),
        ["Complete"] = new SolidColorBrush(Color.Parse("#555B63")),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string status && Brushes.TryGetValue(status, out var brush)
            ? brush
            : new SolidColorBrush(Color.Parse("#444B53"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Green when true, red when false — build/test outcome badges.</summary>
public sealed class OutcomeToBrushConverter : IValueConverter
{
    public static readonly OutcomeToBrushConverter Instance = new();

    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#4CC38A"));
    private static readonly IBrush Fail = new SolidColorBrush(Color.Parse("#F16A50"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Ok : Fail;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
