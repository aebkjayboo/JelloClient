using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JelloClient.Interop;

public enum SecureDisplayResult
{
    Disabled,
    ExcludedFromCapture,
    MonitorOnly,
    Failed
}

internal static class WindowEffects
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private const uint WdaNone = 0x00000000;
    private const uint WdaMonitor = 0x00000001;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private const int Windows11Build = 22000;
    private const int ExcludeFromCaptureBuild = 19041;

    public static int Build => Environment.OSVersion.Version.Build;

    public static bool IsWindows11 => Build >= Windows11Build;

    public static bool SupportsExcludeFromCapture => Build >= ExcludeFromCaptureBuild;

    public static bool IsForeground(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && Native.GetForegroundWindow() == hwnd;

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Native.ShowWindow(hwnd, Native.IsIconic(hwnd) ? Native.SwRestore : Native.SwShow);

        if (Native.SetForegroundWindow(hwnd))
        {
            return;
        }

        uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero);
        uint current = Native.GetCurrentThreadId();

        if (foreground == 0 || foreground == current)
        {
            return;
        }

        Native.AttachThreadInput(current, foreground, true);

        try
        {
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            Native.AttachThreadInput(current, foreground, false);
        }
    }

    public static void ApplyBlur(IntPtr hwnd, byte a, byte r, byte g, byte b, bool preferAcrylic)
    {
        uint abgr = (uint)((a << 24) | (b << 16) | (g << 8) | r);

        SetAccent(hwnd, preferAcrylic ? AccentEnableAcrylicBlurBehind : AccentEnableBlurBehind, abgr);
    }

    public static void DisableBlur(IntPtr hwnd)
    {
        SetAccent(hwnd, AccentDisabled, 0);
    }

    private static void SetAccent(IntPtr hwnd, int state, uint gradientColor)
    {
        var policy = new Native.AccentPolicy
        {
            AccentState = state,
            AccentFlags = 2,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        int size = Marshal.SizeOf<Native.AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, ptr, false);

            var data = new Native.WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size
            };

            Native.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static SecureDisplayResult SetSecureDisplay(IntPtr hwnd, bool secure)
    {
        if (!secure)
        {
            return Native.SetWindowDisplayAffinity(hwnd, WdaNone)
                ? SecureDisplayResult.Disabled
                : SecureDisplayResult.Failed;
        }

        if (SupportsExcludeFromCapture && Native.SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture))
        {
            return SecureDisplayResult.ExcludedFromCapture;
        }

        if (Native.SetWindowDisplayAffinity(hwnd, WdaMonitor))
        {
            return SecureDisplayResult.MonitorOnly;
        }

        return SecureDisplayResult.Failed;
    }

    public static string DescribeLastError()
    {
        return new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    public static void ApplyWindows11Corners(IntPtr hwnd)
    {
        int preference = DwmwcpRound;
        Native.DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    public static void ApplyWindows10Corners(IntPtr hwnd, double radiusDip, double dpiScale, bool maximized)
    {
        if (maximized)
        {
            Native.SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (!Native.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        int diameter = (int)Math.Round(radiusDip * dpiScale * 2.0);

        IntPtr region = Native.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        Native.SetWindowRgn(hwnd, region, true);
    }

    public static void ClampMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        IntPtr monitor = Native.MonitorFromWindow(hwnd, Native.MonitorDefaultToNearest);

        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };

        if (!Native.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var mmi = Marshal.PtrToStructure<Native.MinMaxInfo>(lParam);

        mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        mmi.MaxSize.X = info.Work.Right - info.Work.Left;
        mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        mmi.MaxTrackSize.X = mmi.MaxSize.X;
        mmi.MaxTrackSize.Y = mmi.MaxSize.Y;

        Marshal.StructureToPtr(mmi, lParam, true);
    }
}
