using System.Runtime.InteropServices;
using JelloClient.Services;
using Microsoft.Win32;

namespace JelloClient.Roblox;

public enum RobloxAppTheme
{
    LeaveAlone,
    Light,
    Dark
}

/// Changes the Roblox app's own light and dark theme - the real one, not a picture over it.
///
/// Everything on that home screen is drawn by the client from server data, so there is no
/// file to swap and no flag to set: replacing every LuaApp texture changed nothing,
/// replacing the sprite sheets changed only the search bar, the theme flags are refused
/// locally, the theme the person picks is not written to disk anywhere Jello can reach, and
/// the token map in the cached app policy is overwritten from the server on every launch.
///
/// What does work is the one input the app takes from outside itself. The Lua app asks
/// SystemThemeService for the theme, and that service reads Windows' own app colour mode.
/// Setting it is a real theme change: measured here, the app's background went from
/// #121215 to #FFFFFF and back, and it follows the setting live - the running client
/// changes without a relaunch.
///
/// The cost is that this is a Windows setting, so it applies to every program on the
/// computer, not only to Roblox. That is said plainly in the interface and the default is
/// to leave it alone.
///
/// This is light and dark only. The colour themes in Roblox's own Device Preferences -
/// Lava Glow and the rest - are a Roblox Plus feature whose selection lives on Roblox's
/// servers, and nothing on this machine decides them.
internal static class RobloxTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsValue = "AppsUseLightTheme";

    private static readonly IntPtr Broadcast = new(0xFFFF);

    private const int SettingChange = 0x001A;
    private const uint AbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, int message, IntPtr wparam, string lparam, uint flags, uint timeout, out IntPtr result);

    /// What Windows is set to now, which is what Roblox is showing.
    public static RobloxAppTheme Current
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Key);

                // Absent means light: that is what Windows itself assumes.
                return key?.GetValue(AppsValue) is int value && value == 0
                    ? RobloxAppTheme.Dark
                    : RobloxAppTheme.Light;
            }
            catch (Exception ex)
            {
                Log.Write("AppTheme::Current", ex.Message);
                return RobloxAppTheme.Light;
            }
        }
    }

    public static bool Apply(RobloxAppTheme wanted)
    {
        const string ident = "AppTheme::Apply";

        if (wanted == RobloxAppTheme.LeaveAlone)
        {
            return true;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);

            if (key is null)
            {
                return false;
            }

            key.SetValue(AppsValue, wanted == RobloxAppTheme.Light ? 1 : 0, RegistryValueKind.DWord);

            // Without this, programs that are already open carry on with the old colours
            // until something else nudges them.
            SendMessageTimeout(Broadcast, SettingChange, IntPtr.Zero, "ImmersiveColorSet", AbortIfHung, 2000, out _);

            Log.Write(ident, $"Windows app colour set to {wanted.ToString().ToLowerInvariant()}, Roblox follows it");

            return true;
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            return false;
        }
    }

    /// Puts the choice back on before a launch, in case something else changed it since.
    public static void Reassert()
    {
        var wanted = AppState.Settings.RobloxAppTheme;

        if (wanted != RobloxAppTheme.LeaveAlone && Current != wanted)
        {
            Apply(wanted);
        }
    }

    public static string Label(RobloxAppTheme theme) => theme switch
    {
        RobloxAppTheme.Light => "Light",
        RobloxAppTheme.Dark => "Dark",
        _ => "Leave alone"
    };

    public static string Describe()
    {
        var wanted = AppState.Settings.RobloxAppTheme;

        if (wanted == RobloxAppTheme.LeaveAlone)
        {
            return $"Not touched. Roblox is following Windows, which is set to {Label(Current).ToLowerInvariant()}.";
        }

        return Current == wanted
            ? $"Roblox is {Label(wanted).ToLowerInvariant()}. Windows is set to match, for every program."
            : $"Set to {Label(wanted).ToLowerInvariant()}, but Windows currently says {Label(Current).ToLowerInvariant()}. It is put back at the next launch.";
    }
}
