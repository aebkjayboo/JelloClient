using System.Runtime.InteropServices;

namespace JelloClient.Interop;

internal sealed record DisplayInfo(
    string DeviceName,
    string Label,
    int Left,
    int Top,
    int Width,
    int Height,
    int RefreshHz,
    bool Primary);

/// The monitors Windows reports, with the one thing WinForms does not expose: the refresh
/// rate each is actually running at.
internal static class Displays
{
    private const int EnumCurrentSettings = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int mode, ref Devmode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Devmode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    public static IReadOnlyList<DisplayInfo> All()
    {
        var displays = new List<DisplayInfo>();
        int index = 1;

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            int refresh = RefreshRateOf(screen.DeviceName);

            string label = $"Display {index}: {screen.Bounds.Width} x {screen.Bounds.Height}" +
                           (refresh > 0 ? $" at {refresh} Hz" : "") +
                           (screen.Primary ? " (main)" : "");

            displays.Add(new DisplayInfo(
                screen.DeviceName,
                label,
                screen.Bounds.Left,
                screen.Bounds.Top,
                screen.Bounds.Width,
                screen.Bounds.Height,
                refresh,
                screen.Primary));

            index++;
        }

        return displays;
    }

    public static DisplayInfo? Find(string? deviceName) =>
        string.IsNullOrEmpty(deviceName)
            ? All().FirstOrDefault(display => display.Primary)
            : All().FirstOrDefault(display => display.DeviceName == deviceName);

    private static int RefreshRateOf(string deviceName)
    {
        var mode = new Devmode
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty
        };

        mode.dmSize = (short)Marshal.SizeOf<Devmode>();

        return EnumDisplaySettingsW(deviceName, EnumCurrentSettings, ref mode) ? mode.dmDisplayFrequency : 0;
    }
}
