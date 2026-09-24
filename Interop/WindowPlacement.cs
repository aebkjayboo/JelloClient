using System.Diagnostics;
using System.Runtime.InteropServices;
using JelloClient.Services;

namespace JelloClient.Interop;

/// Puts the Roblox window on the display the user picked.
///
/// Roblox has no flag for this - it opens wherever Windows last put it - so the window is
/// moved after it appears. The client is polled for a few seconds because the window does
/// not exist the moment the process does.
internal static class WindowPlacement
{
    private const uint ShowWindowFlag = 0x0040;

    private const int Restore = 9;

    private const int Maximize = 3;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    public static void MoveToDisplay(int processId, string? deviceName)
    {
        const string ident = "WindowPlacement::MoveToDisplay";

        var display = Displays.Find(deviceName);

        if (display is null)
        {
            Log.Write(ident, $"Display {deviceName} is not connected any more, leaving the window alone");
            return;
        }

        IntPtr window = WaitForWindow(processId);

        if (window == IntPtr.Zero)
        {
            Log.Write(ident, "The client never opened a window to move");
            return;
        }

        bool maximised = IsZoomed(window);

        if (maximised)
        {
            ShowWindow(window, Restore);
        }

        int width = Math.Min(1280, display.Width - 80);
        int height = Math.Min(720, display.Height - 80);

        SetWindowPos(
            window,
            IntPtr.Zero,
            display.Left + (display.Width - width) / 2,
            display.Top + (display.Height - height) / 2,
            width,
            height,
            ShowWindowFlag);

        if (maximised)
        {
            ShowWindow(window, Maximize);
        }

        Log.Write(ident, $"Moved the client onto {display.Label}");
    }

    private static IntPtr WaitForWindow(int processId)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                var process = Process.GetProcessById(processId);

                if (process.HasExited)
                {
                    return IntPtr.Zero;
                }

                process.Refresh();

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch (ArgumentException)
            {
                return IntPtr.Zero;
            }

            Thread.Sleep(500);
        }

        return IntPtr.Zero;
    }
}
