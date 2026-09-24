using System.Diagnostics;
using System.Threading;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal static class Launcher
{
    private const string SingletonMutexName = "ROBLOX_singletonMutex";

    private static Mutex? _singletonMutex;

    public static bool HoldMultiInstanceMutex()
    {
        if (_singletonMutex is not null)
        {
            return true;
        }

        try
        {
            _singletonMutex = new Mutex(true, SingletonMutexName, out bool created);

            if (!created)
            {
                _singletonMutex.Dispose();
                _singletonMutex = null;

                Log.Write("Launcher::HoldMultiInstanceMutex", $"{SingletonMutexName} is already held elsewhere");
                return false;
            }

            Log.Write("Launcher::HoldMultiInstanceMutex", $"Holding {SingletonMutexName}");
            return true;
        }
        catch (Exception ex)
        {
            Log.WriteException("Launcher::HoldMultiInstanceMutex", ex);

            _singletonMutex = null;
            return false;
        }
    }

    public static void ReleaseMultiInstanceMutex()
    {
        if (_singletonMutex is null)
        {
            return;
        }

        try
        {
            _singletonMutex.ReleaseMutex();
        }
        catch (Exception ex)
        {
            Log.Write("Launcher::ReleaseMultiInstanceMutex", $"Could not release cleanly: {ex.Message}");
        }

        _singletonMutex.Dispose();
        _singletonMutex = null;

        Log.Write("Launcher::ReleaseMultiInstanceMutex", $"Released {SingletonMutexName}");
    }

    public static Process Start(string executablePath, string launchArguments)
    {
        Log.Write("Launcher::Start", string.IsNullOrEmpty(launchArguments)
            ? $"Starting {executablePath} with no arguments"
            : $"Starting {executablePath} with {launchArguments}");

        var info = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = launchArguments,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false
        };

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("Roblox failed to start.");

        Log.Write("Launcher::Start", $"Roblox is running as process {process.Id}");

        return process;
    }
}
