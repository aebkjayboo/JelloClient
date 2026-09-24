using System.Windows.Media;
using Microsoft.Win32;

namespace JelloClient.Interop;

internal static class SystemAccent
{
    private const string AccentKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    private static readonly Color Fallback = Color.FromRgb(0x4C, 0xC2, 0xFF);

    private static readonly Color LightFallback = Color.FromRgb(0x14, 0x90, 0xE0);

    public static Color GetForDarkTheme()
    {
        Color? accent = ReadAccentColorMenu();

        return accent is null ? Fallback : Lighten(accent.Value, 0.4);
    }

    private static Color? ReadAccentColorMenu()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AccentKeyPath);

            if (key?.GetValue("AccentColorMenu") is not int raw)
            {
                return null;
            }

            uint abgr = unchecked((uint)raw);

            return Color.FromRgb(
                (byte)(abgr & 0xFF),
                (byte)((abgr >> 8) & 0xFF),
                (byte)((abgr >> 16) & 0xFF));
        }
        catch
        {
            return null;
        }
    }

    /// The accent as drawn on a light surface: dark enough that white text on top of it,
    /// and the colour itself as text, both stay readable.
    public static Color GetForLightTheme()
    {
        Color? accent = ReadAccentColorMenu();

        return Darken(accent ?? LightFallback, 0.25);
    }

    public static Color Darken(Color color, double amount) =>
        Color.FromRgb(
            (byte)Math.Round(color.R * (1 - amount)),
            (byte)Math.Round(color.G * (1 - amount)),
            (byte)Math.Round(color.B * (1 - amount)));

    private static Color Lighten(Color color, double amount)
    {
        return Color.FromRgb(
            (byte)Math.Round(color.R + (255 - color.R) * amount),
            (byte)Math.Round(color.G + (255 - color.G) * amount),
            (byte)Math.Round(color.B + (255 - color.B) * amount));
    }
}
