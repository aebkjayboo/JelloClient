using System.Net.Http;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using JelloClient.Interop;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Puts the game's own icon on the Roblox window, so the taskbar shows what is being
/// played rather than the same Roblox tile every time.
///
/// This is the window's icon, not the executable's. Roblox's exe is signed and its
/// resources cannot be rewritten, which is why the earlier icon idea was a dead end - but a
/// window's icon is just two WM_SETICON messages, and any process may send them. The
/// original pair is read back with WM_GETICON first so leaving a game restores it.
internal static class WindowIcon
{
    private const int WmSetIcon = 0x0080;
    private const int WmGetIcon = 0x007F;

    private const int IconSmall = 0;
    private const int IconBig = 1;

    private const uint SendTimeoutAbortIfHung = 0x0002;
    private const uint SendTimeoutMilliseconds = 1000;

    private const int SmallSize = 32;
    private const int BigSize = 64;

    private const int MaximumDownloadBytes = 4 * 1024 * 1024;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, int count);

    private static readonly object Gate = new();

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static IntPtr _window;

    private static IntPtr _originalSmall;
    private static IntPtr _originalBig;

    private static IntPtr _ownedSmall;
    private static IntPtr _ownedBig;

    private static long _shownUniverse;

    /// The last thing this said, for the settings window.
    public static string Status { get; private set; } = "Not running.";

    /// `executablePath` is where the icon to go back to is read from.
    ///
    /// Asking the window for its current icon does not work here. Jello attaches within a
    /// second or two of launching, and at that point Roblox has a window but has not set
    /// an icon on it yet, so WM_GETICON hands back nothing - which is why leaving a game
    /// used to leave the game's icon on the taskbar: there was nothing to put back. The
    /// executable's own icon is the same picture, is there immediately, and does not
    /// change under us.
    public static void Attach(IntPtr window, string? executablePath)
    {
        lock (Gate)
        {
            if (_window == window)
            {
                return;
            }

            Forget();

            _window = window;

            (_originalSmall, _originalBig) = FromExecutable(executablePath);

            // Only if the executable had nothing to give: a copy of whatever the window is
            // showing, which is ours and so stays valid.
            if (_originalSmall == IntPtr.Zero && _originalBig == IntPtr.Zero)
            {
                _originalSmall = Duplicate(Ask(WmGetIcon, IconSmall));
                _originalBig = Duplicate(Ask(WmGetIcon, IconBig));
            }

            Log.Write("WindowIcon::Attach",
                $"Holding Roblox's own icon for window {window} "
              + $"(small={_originalSmall != IntPtr.Zero}, big={_originalBig != IntPtr.Zero})");
        }
    }

    /// The pair of icons stored in the executable's resources.
    private static (IntPtr Small, IntPtr Big) FromExecutable(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return (IntPtr.Zero, IntPtr.Zero);
        }

        try
        {
            int found = ExtractIconEx(path, 0, out IntPtr large, out IntPtr small, 1);

            if (found <= 0)
            {
                return (IntPtr.Zero, IntPtr.Zero);
            }

            return (small, large);
        }
        catch (Exception ex)
        {
            Log.Write("WindowIcon::FromExecutable", $"Could not read the icon from {path}: {ex.Message}");
            return (IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// Called when a game is joined. The universe id is what the thumbnail API keys on.
    public static async Task ShowGameAsync(long universeId)
    {
        const string ident = "WindowIcon::ShowGame";

        if (!AppState.Settings.GameIconOnWindow || universeId == 0)
        {
            return;
        }

        if (Interlocked.Read(ref _shownUniverse) == universeId)
        {
            return;
        }

        try
        {
            byte[] picture;

            string? custom = AppState.Settings.GameIconCustomPath;

            if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
            {
                picture = await File.ReadAllBytesAsync(custom);
            }
            else
            {
                string? url = await Thumbnails.GameIconUrlAsync(universeId);

                if (string.IsNullOrEmpty(url))
                {
                    Report($"Universe {universeId} has no icon, leaving Roblox's own.");
                    return;
                }

                picture = await DownloadAsync(url);
            }

            if (picture.Length == 0)
            {
                Report("The icon would not load, leaving Roblox's own.");
                return;
            }

            IntPtr small, big;

            using (var stream = new MemoryStream(picture))
            using (var bitmap = new Bitmap(stream))
            {
                small = Convert(bitmap, SmallSize);
                big = Convert(bitmap, BigSize);
            }

            if (small == IntPtr.Zero && big == IntPtr.Zero)
            {
                Report("The icon could not be converted, leaving Roblox's own.");
                return;
            }

            Apply(small, big, owned: true);

            Interlocked.Exchange(ref _shownUniverse, universeId);

            Report(string.IsNullOrEmpty(custom)
                ? $"Showing the icon for universe {universeId}."
                : "Showing your custom icon.");
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            Report($"Could not set the icon: {ex.Message}");
        }
    }

    /// Called when the game is left, or the setting is turned off.
    public static void Restore()
    {
        lock (Gate)
        {
            if (_window == IntPtr.Zero)
            {
                return;
            }

            bool had = Interlocked.Exchange(ref _shownUniverse, 0) != 0;

            Apply(_originalSmall, _originalBig, owned: false);

            Status = "Showing Roblox's own icon.";

            if (had)
            {
                Log.Write("WindowIcon::Restore", "Back to Roblox's own icon");
            }
        }
    }

    /// Roblox closed, so the handles belong to nothing. Ours are freed; Roblox's were
    /// never ours to free.
    public static void Forget()
    {
        lock (Gate)
        {
            Free(ref _ownedSmall);
            Free(ref _ownedBig);

            // The copies are ours as well, so they are freed rather than dropped.
            Free(ref _originalSmall);
            Free(ref _originalBig);

            _window = IntPtr.Zero;

            Interlocked.Exchange(ref _shownUniverse, 0);

            Status = "Not running.";
        }
    }

    private static void Apply(IntPtr small, IntPtr big, bool owned)
    {
        lock (Gate)
        {
            if (_window == IntPtr.Zero)
            {
                return;
            }

            if (small != IntPtr.Zero)
            {
                Tell(WmSetIcon, IconSmall, small);
            }

            if (big != IntPtr.Zero)
            {
                Tell(WmSetIcon, IconBig, big);
            }

            // Free the pair we replaced only once the window is no longer drawing it.
            IntPtr previousSmall = _ownedSmall;
            IntPtr previousBig = _ownedBig;

            _ownedSmall = owned ? small : IntPtr.Zero;
            _ownedBig = owned ? big : IntPtr.Zero;

            Free(ref previousSmall);
            Free(ref previousBig);
        }
    }

    /// A private copy of an icon we did not create, so it survives the owner recycling it.
    private static IntPtr Duplicate(IntPtr icon)
    {
        if (icon == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            return CopyIcon(icon);
        }
        catch (Exception ex)
        {
            Log.Write("WindowIcon::Duplicate", ex.Message);
            return IntPtr.Zero;
        }
    }

    private static IntPtr Ask(int message, int which)
    {
        SendMessageTimeout(
            _window, message, which, IntPtr.Zero, SendTimeoutAbortIfHung, SendTimeoutMilliseconds, out IntPtr result);

        return result;
    }

    private static void Tell(int message, int which, IntPtr icon) =>
        SendMessageTimeout(
            _window, message, which, icon, SendTimeoutAbortIfHung, SendTimeoutMilliseconds, out _);

    private static void Free(ref IntPtr icon)
    {
        if (icon == IntPtr.Zero)
        {
            return;
        }

        try
        {
            DestroyIcon(icon);
        }
        catch (Exception ex)
        {
            Log.Write("WindowIcon::Free", ex.Message);
        }

        icon = IntPtr.Zero;
    }

    /// Square, high quality, and on a transparent ground, because a game thumbnail is a
    /// photograph and the taskbar draws it small.
    private static IntPtr Convert(Bitmap source, int size)
    {
        try
        {
            using var square = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            using (var canvas = Graphics.FromImage(square))
            {
                canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                canvas.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                canvas.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                canvas.Clear(Color.Transparent);
                canvas.DrawImage(source, new Rectangle(0, 0, size, size));
            }

            return square.GetHicon();
        }
        catch (Exception ex)
        {
            Log.Write("WindowIcon::Convert", $"Could not build a {size}px icon: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    private static async Task<byte[]> DownloadAsync(string url)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<byte>();
        }

        if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
        {
            return Array.Empty<byte>();
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static void Report(string status)
    {
        Status = status;
        Log.Write("WindowIcon", status);
    }

    public static string Describe() =>
        !AppState.Settings.GameIconOnWindow
            ? "Off. The taskbar shows the usual Roblox icon."
            : string.IsNullOrEmpty(AppState.Settings.GameIconCustomPath)
                ? $"On, using each game's own icon. {Status}"
                : $"On, using your custom icon. {Status}";
}
