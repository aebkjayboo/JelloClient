using System.Windows;
using System.Windows.Media;
using JelloClient.Interop;
using Microsoft.Win32;

namespace JelloClient.Services;

internal enum AppTheme
{
    Dark,
    Light,
    System
}

/// Swaps the palette dictionary the rest of the app draws from.
///
/// Every style resolves its colours through the keys in Theme/Palette.xaml, so a theme is
/// one dictionary swap plus the accent colours, which are derived from the Windows accent
/// in the direction that stays readable on the surface behind them. StaticResource is
/// resolved when a window loads, so a switch after startup recreates the open windows.
internal static class Themes
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static AppTheme Effective { get; private set; } = AppTheme.Dark;

    public static bool IsLight => Effective == AppTheme.Light;

    public static AppTheme Resolve(AppTheme theme) => theme switch
    {
        AppTheme.Dark => AppTheme.Dark,
        AppTheme.Light => AppTheme.Light,
        _ => WindowsPrefersLight() ? AppTheme.Light : AppTheme.Dark
    };

    public static bool WindowsPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch (Exception ex)
        {
            Log.Write("Themes::WindowsPrefersLight", $"Could not read the Windows theme: {ex.Message}");
            return false;
        }
    }

    public static void Apply(AppTheme theme)
    {
        const string ident = "Themes::Apply";

        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        var effective = Resolve(theme);
        string source = effective == AppTheme.Light ? "Theme/PaletteLight.xaml" : "Theme/Palette.xaml";

        var dictionaries = app.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        if (dictionaries.Count == 0)
        {
            dictionaries.Add(palette);
        }
        else
        {
            dictionaries[0] = palette;
        }

        Effective = effective;

        ApplyAccent(effective);

        Log.Write(ident, $"{theme} resolved to {effective}, using {source}");
    }

    private static void ApplyAccent(AppTheme effective)
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        try
        {
            var accent = effective == AppTheme.Light
                ? SystemAccent.GetForLightTheme()
                : SystemAccent.GetForDarkTheme();

            app.Resources["Accent"] = new SolidColorBrush(accent);
            app.Resources["AccentText"] = new SolidColorBrush(
                effective == AppTheme.Light ? SystemAccent.Darken(accent, 0.2) : accent);

            app.Resources["SelectionFill"] = new SolidColorBrush(
                Color.FromArgb(effective == AppTheme.Light ? (byte)0x33 : (byte)0x4C, accent.R, accent.G, accent.B));
        }
        catch (Exception ex)
        {
            Log.WriteException("Themes::ApplyAccent", ex);
        }
    }
}
