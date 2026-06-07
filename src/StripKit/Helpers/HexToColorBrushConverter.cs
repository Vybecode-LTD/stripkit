using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace StripKit.Helpers;

/// <summary>Converts a <c>#RRGGBB</c> or <c>#AARRGGBB</c> hex string to a <see cref="SolidColorBrush"/>
/// for the colour swatch in the Generate tab. Returns a transparent brush for blank / invalid input.</summary>
public sealed class HexToColorBrushConverter : IValueConverter
{
    public static readonly HexToColorBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { /* invalid hex — fall through */ }
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
