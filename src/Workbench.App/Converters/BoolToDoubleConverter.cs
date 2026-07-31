using System.Globalization;
using Avalonia.Data.Converters;

namespace Workbench.App.Converters;

/// <summary>true → 1.0, false → 0.0 — lets visibility booleans drive fade-in transitions.</summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
