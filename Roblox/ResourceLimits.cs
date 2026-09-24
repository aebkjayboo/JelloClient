using System.Diagnostics;
using System.Runtime.InteropServices;
using JelloClient.Services;

namespace JelloClient.Roblox;

public enum ProcessPriority
{
    Leave,
    BelowNormal,
    Normal,
    AboveNormal,
    High
}

/// How much of the machine Roblox is allowed to take.
///
/// Two levers, both applied to the running client and both reversible by restarting it:
/// which cores it may run on, and where it sits in the scheduler. Neither is a speed up on
/// its own - the point is deciding what happens to everything else while a game is open.
///
/// Realtime is deliberately not offered. It outranks input and audio handling, and a game
/// that stops responding at realtime can take the desktop with it.
internal static class ResourceLimits
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessAffinityMask(IntPtr process, UIntPtr mask);

    public static int CoreCount => Environment.ProcessorCount;

    /// The last thing this said, for the settings window.
    public static string Status { get; private set; } = "Nothing applied yet.";

    /// Applies whatever the settings ask for to the running client. Called after launch;
    /// safe to call when Roblox is not running, in which case it does nothing.
    public static void Apply(int processId)
    {
        const string ident = "Resources::Apply";

        var settings = AppState.Settings;

        if (settings.CoreLimit <= 0 && settings.RobloxPriority == ProcessPriority.Leave)
        {
            Status = "Nothing set, so Roblox is left alone.";
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);

            var said = new List<string>();

            if (settings.RobloxPriority != ProcessPriority.Leave)
            {
                said.Add(SetPriority(process, settings.RobloxPriority));
            }

            if (settings.CoreLimit > 0)
            {
                said.Add(SetCores(process, settings.CoreLimit));
            }

            Status = string.Join(" ", said);

            Log.Write(ident, Status);
        }
        catch (ArgumentException)
        {
            Status = "Roblox is not running.";
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            Status = $"Could not apply: {ex.Message}";
        }
    }

    private static string SetPriority(Process process, ProcessPriority priority)
    {
        var wanted = priority switch
        {
            ProcessPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
            ProcessPriority.Normal => ProcessPriorityClass.Normal,
            ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
            ProcessPriority.High => ProcessPriorityClass.High,
            _ => ProcessPriorityClass.Normal
        };

        try
        {
            process.PriorityClass = wanted;
            return $"Priority set to {Describe(priority).ToLowerInvariant()}.";
        }
        catch (Exception ex)
        {
            return $"Priority refused: {ex.Message}";
        }
    }

    /// The lowest `count` cores. Which cores in particular matters less than how many,
    /// and picking the low ones keeps the choice predictable across restarts.
    private static string SetCores(Process process, int count)
    {
        count = Math.Clamp(count, 1, CoreCount);

        ulong mask = count >= 64 ? ulong.MaxValue : (1UL << count) - 1;

        try
        {
            if (!SetProcessAffinityMask(process.Handle, (UIntPtr)mask))
            {
                int error = Marshal.GetLastWin32Error();
                return $"Core limit refused by Windows (error {error}).";
            }

            return $"Running on {count} of {CoreCount} core(s).";
        }
        catch (Exception ex)
        {
            return $"Core limit refused: {ex.Message}";
        }
    }

    public static string Describe(ProcessPriority priority) => priority switch
    {
        ProcessPriority.Leave => "Leave alone",
        ProcessPriority.BelowNormal => "Below normal",
        ProcessPriority.Normal => "Normal",
        ProcessPriority.AboveNormal => "Above normal",
        ProcessPriority.High => "High",
        _ => "Leave alone"
    };

    public static string Describe()
    {
        var settings = AppState.Settings;

        var parts = new List<string>();

        if (settings.RobloxPriority != ProcessPriority.Leave)
        {
            parts.Add($"priority {Describe(settings.RobloxPriority).ToLowerInvariant()}");
        }

        if (settings.CoreLimit > 0)
        {
            parts.Add($"{settings.CoreLimit} of {CoreCount} cores");
        }

        return parts.Count == 0
            ? $"Roblox gets the whole machine: all {CoreCount} cores, normal priority."
            : $"Roblox gets {string.Join(", ", parts)}. {Status}";
    }
}
