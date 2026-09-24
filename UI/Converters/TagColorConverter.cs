using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JelloClient.UI.Converters;

/// Pill colours for the flag tags, matching Voidstrap's TagColorConverter.
public sealed class TagColorConverter : IValueConverter
{
    private static Color Darken(Color color, double factor = 0.7) =>
        Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static readonly Dictionary<string, SolidColorBrush> Palette = new(StringComparer.Ordinal)
    {
        // Voidstrap leaves these two bright; white on them measures 3.2:1 and 3.4:1, so
        // they are darkened here the way the rest of their palette already is.
        ["Performance"] = Frozen(Darken(Color.FromRgb(52, 152, 219), 0.6)),
        ["LOD"] = Frozen(Darken(Color.FromRgb(41, 122, 175))),
        ["Fix"] = Frozen(Darken(Color.FromRgb(231, 76, 60))),
        ["Graphics"] = Frozen(Darken(Color.FromRgb(26, 188, 156))),
        ["Experimental"] = Frozen(Darken(Color.FromRgb(241, 196, 15), 0.55)),
        ["UI"] = Frozen(Darken(Color.FromRgb(155, 89, 182))),
        ["Unknown"] = Frozen(Darken(Color.FromRgb(149, 165, 166)))
    };

    private static readonly SolidColorBrush OverflowBrush = Frozen(Darken(Color.FromRgb(127, 140, 141)));

    private static readonly SolidColorBrush FallbackBrush = Frozen(Darken(Colors.Gray));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string tag || tag.Length == 0)
        {
            return FallbackBrush;
        }

        if (Palette.TryGetValue(tag, out var brush))
        {
            return brush;
        }

        return tag[0] == '+' ? OverflowBrush : FallbackBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
