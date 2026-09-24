using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using JelloClient.Interop;
using JelloClient.Services;

namespace JelloClient.UI;

public abstract class JelloWindow : Window
{
    // Jello's windows are WindowStyle.None (custom chrome), which is exactly the case where WPF
    // can leave the taskbar button with no icon. Pushing the icon straight onto the HWND with
    // WM_SETICON is deterministic and survives the tray app's hide/show cycles.
    private const int WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int SmCxIcon = 11;
    private const int SmCxSmIcon = 49;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private System.Drawing.Icon? _smallIcon;
    private System.Drawing.Icon? _bigIcon;

    private const byte TintAlpha = 0x99;
    private const byte TintRed = 0x20;
    private const byte TintGreen = 0x20;
    private const byte TintBlue = 0x20;

    private const double CornerRadiusDip = 8.0;

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _ready;
    private (int Width, int Height, double Scale, bool Maximized)? _lastCornerShape;

    protected JelloWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = Brushes.Transparent;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        StateChanged += (_, _) => RefreshCornersOnLegacyWindows();
        SizeChanged += (_, _) => RefreshCornersOnLegacyWindows();
        DpiChanged += (_, _) => RefreshCornersOnLegacyWindows();
    }

    internal UserSettings Settings => AppState.Settings;

    protected abstract FrameworkElement EffectsRoot { get; }

    protected string LogIdent => GetType().Name;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Log.Write($"{LogIdent}::OnSourceInitialized", "Creating window handle");

        try
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source.AddHook(HandleMessage);

            _hwnd = source.Handle;
            _ready = true;
        }
        catch (Exception ex)
        {
            Log.WriteException($"{LogIdent}::OnSourceInitialized", ex);
        }

        Guard(nameof(ApplyTaskbarIcon), ApplyTaskbarIcon);

        ApplyAllEffects();
    }

    /// Force Jello's icon onto this window's HWND (taskbar + Alt-Tab). Loads real small/large
    /// HICONs from the embedded icon.ico at the current system metric sizes and sends WM_SETICON,
    /// so the taskbar always has an icon even when WPF wouldn't set one for a None-style window.
    private void ApplyTaskbarIcon()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        int smallSize = GetSystemMetrics(SmCxSmIcon);
        int bigSize = GetSystemMetrics(SmCxIcon);
        if (smallSize <= 0) smallSize = 16;
        if (bigSize <= 0) bigSize = 32;

        // Reload from the embedded resource each time; the HICON handles must stay alive for the
        // life of the window (WM_SETICON only stores the handle), so keep the Icon objects in
        // fields and dispose them when the window closes.
        var uri = new Uri("pack://application:,,,/Assets/icon.ico");

        using (var stream = Application.GetResourceStream(uri)?.Stream)
        {
            if (stream is null)
            {
                Log.Write($"{LogIdent}::ApplyTaskbarIcon", "icon.ico resource not found");
                return;
            }

            _smallIcon = new System.Drawing.Icon(stream, new System.Drawing.Size(smallSize, smallSize));
            stream.Position = 0;
            _bigIcon = new System.Drawing.Icon(stream, new System.Drawing.Size(bigSize, bigSize));
        }

        SendMessage(_hwnd, WmSetIcon, IconSmall, _smallIcon.Handle);
        SendMessage(_hwnd, WmSetIcon, IconBig, _bigIcon.Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _smallIcon?.Dispose();
        _bigIcon?.Dispose();
        _smallIcon = null;
        _bigIcon = null;

        base.OnClosed(e);
    }

    protected void ApplyAllEffects()
    {
        Guard(nameof(ApplyBackground), ApplyBackground);
        Guard(nameof(ApplyCorners), ApplyCorners);
        Guard(nameof(ApplySecureDisplay), () => ApplySecureDisplay());
        Guard(nameof(ApplyTopmost), ApplyTopmost);
    }

    protected void Guard(string ident, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.WriteException($"{LogIdent}::{ident}", ex);
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WmGetMinMaxInfo)
        {
            WindowEffects.ClampMaximizedBounds(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    protected void ApplyBackground()
    {
        if (!_ready)
        {
            return;
        }

        if (Settings.AcrylicEnabled)
        {
            var tint = Application.Current?.TryFindResource("WindowTint") is Color themed
                ? themed
                : Color.FromRgb(TintRed, TintGreen, TintBlue);

            byte alpha = Application.Current?.TryFindResource("WindowTintAlpha") is byte themedAlpha
                ? themedAlpha
                : TintAlpha;

            WindowEffects.ApplyBlur(_hwnd, alpha, tint.R, tint.G, tint.B, WindowEffects.IsWindows11);
            EffectsRoot.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        }
        else
        {
            WindowEffects.DisableBlur(_hwnd);
            EffectsRoot.SetValue(Panel.BackgroundProperty, (Brush)FindResource("ApplicationBackground"));
        }
    }

    protected void ApplyCorners()
    {
        if (!_ready)
        {
            return;
        }

        if (WindowEffects.IsWindows11)
        {
            WindowEffects.ApplyWindows11Corners(_hwnd);
            return;
        }

        if (!Native.GetWindowRect(_hwnd, out var rect))
        {
            return;
        }

        var shape = (
            Width: rect.Right - rect.Left,
            Height: rect.Bottom - rect.Top,
            Scale: VisualTreeHelper.GetDpi(this).DpiScaleX,
            Maximized: WindowState == WindowState.Maximized);

        if (_lastCornerShape == shape)
        {
            return;
        }

        _lastCornerShape = shape;

        WindowEffects.ApplyWindows10Corners(_hwnd, CornerRadiusDip, shape.Scale, shape.Maximized);
    }

    private void RefreshCornersOnLegacyWindows()
    {
        if (!WindowEffects.IsWindows11)
        {
            Guard(nameof(ApplyCorners), ApplyCorners);
        }
    }

    protected SecureDisplayResult ApplySecureDisplay()
    {
        if (!_ready)
        {
            return SecureDisplayResult.Disabled;
        }

        var result = WindowEffects.SetSecureDisplay(_hwnd, Settings.SecureUi);

        if (result == SecureDisplayResult.Failed)
        {
            Log.Write($"{LogIdent}::ApplySecureDisplay", $"Failed: {WindowEffects.DescribeLastError()}");
        }

        return result;
    }

    protected void ApplyTopmost() => Topmost = Settings.AlwaysOnTop;
}
