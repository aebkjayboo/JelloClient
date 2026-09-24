using System.Diagnostics;
using System.Runtime.InteropServices;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal static class AudioDuck
{
    private const int IntervalMilliseconds = 700;

    private static readonly object Gate = new();

    private static Timer? _timer;
    private static bool _ducked;
    private static float _fullVolume = 1f;

    public static string Status { get; private set; } = "Off.";

    public static void Refresh()
    {
        if (AppState.Settings.DuckAudioUnfocused)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private static void Start()
    {
        lock (Gate)
        {
            if (_timer is not null)
            {
                return;
            }

            _timer = new Timer(_ => Tick(), null, 0, IntervalMilliseconds);
            Status = "Watching focus.";
        }
    }

    public static void Stop()
    {
        Timer? timer;

        lock (Gate)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();

        if (_ducked)
        {
            SetRobloxVolume(_fullVolume);
            _ducked = false;
        }

        Status = "Off.";
    }

    private static void Tick()
    {
        try
        {
            int robloxPid = AppState.RobloxProcessId;

            if (robloxPid == 0)
            {
                if (_ducked)
                {
                    _ducked = false;
                }

                return;
            }

            bool robloxForeground = ForegroundProcessId() == (uint)robloxPid;

            if (!robloxForeground && !_ducked)
            {
                _fullVolume = GetRobloxVolume() ?? 1f;
                SetRobloxVolume(_fullVolume * (float)Math.Clamp(AppState.Settings.DuckAudioLevel, 0, 1));
                _ducked = true;
                Status = "Ducked while Roblox is in the background.";
            }
            else if (robloxForeground && _ducked)
            {
                SetRobloxVolume(_fullVolume);
                _ducked = false;
                Status = "Full volume while Roblox is in front.";
            }
        }
        catch (Exception ex)
        {
            Log.Write("AudioDuck::Tick", ex.Message);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private static uint ForegroundProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid;
    }

    private static float? GetRobloxVolume()
    {
        var volume = SessionVolume();
        return volume is null ? null : volume.GetMasterVolume(out float level) == 0 ? level : null;
    }

    private static void SetRobloxVolume(float level)
    {
        var volume = SessionVolume();
        volume?.SetMasterVolume(Math.Clamp(level, 0f, 1f), Guid.Empty);
    }

    private static ISimpleAudioVolume? SessionVolume()
    {
        var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));

        if (enumeratorType is null || Activator.CreateInstance(enumeratorType) is not IMMDeviceEnumerator enumerator)
        {
            return null;
        }

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device) != 0)
            {
                return null;
            }

            var managerGuid = typeof(IAudioSessionManager2).GUID;

            if (device.Activate(ref managerGuid, 0, IntPtr.Zero, out object managerObject) != 0
                || managerObject is not IAudioSessionManager2 manager)
            {
                return null;
            }

            if (manager.GetSessionEnumerator(out IAudioSessionEnumerator sessions) != 0)
            {
                return null;
            }

            sessions.GetCount(out int count);

            uint want = (uint)AppState.RobloxProcessId;

            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out IAudioSessionControl control) != 0)
                {
                    continue;
                }

                if (control is IAudioSessionControl2 control2
                    && control2.GetProcessId(out uint pid) == 0
                    && pid == want
                    && control is ISimpleAudioVolume volume)
                {
                    return volume;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("AudioDuck::SessionVolume", ex.Message);
        }

        return null;
    }

    public static string Describe()
    {
        if (!AppState.Settings.DuckAudioUnfocused)
        {
            return "Off. Roblox stays at full volume when you switch away.";
        }

        return $"On. Roblox drops to {AppState.Settings.DuckAudioLevel * 100:0}% while it is not the window in front. {Status}";
    }
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int NotImpl1();
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    int NotImpl1();
    int NotImpl2();
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount(out int count);
    int GetSession(int index, out IAudioSessionControl session);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    int NotImpl0();
    int NotImpl1();
    int NotImpl2();
    int NotImpl3();
    int NotImpl4();
    int NotImpl5();
    int NotImpl6();
    int NotImpl7();
    int NotImpl8();
    int NotImpl9();
    int NotImpl10();
    int GetProcessId(out uint pid);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    int SetMasterVolume(float level, Guid eventContext);
    int GetMasterVolume(out float level);
}
