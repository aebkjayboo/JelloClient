using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace JelloClient.Services;

/// Starts Jello when Windows starts: a per-user Run key without admin, or a privileged
/// (highest) logon scheduled task with admin - whichever fits, and only one. It starts with
/// -agent, so at login ONLY the Private background agent runs (no window, no tray); the full
/// UI appears only when the user opens Jello themselves.
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "JelloClient";

    /// The registry is the truth here, not a setting: the person can remove the entry from
    /// Task Manager, and the toggle should reflect that when it is next read.
    /// True if autostart is configured by EITHER mechanism - the Run key or the scheduled task -
    /// so an admin (task) install reads as enabled, not just a Run-key one.
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is string value && value.Length > 0)
            {
                return true;
            }
        }
        catch (Exception)
        {
            // fall through to the task check
        }

        return TaskExists();
    }

    public static void Set(bool enabled, string? executablePath = null)
    {
        if (enabled)
        {
            Enable(executablePath);
        }
        else
        {
            Disable();
        }
    }

    public static void Enable(string? executablePath = null)
    {
        try
        {
            string executable = Resolve(executablePath);

            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, $"\"{executable}\" -agent");

            Log.Write("Startup::Enable", $"Jello will start with Windows: {executable}");
        }
        catch (Exception ex)
        {
            Log.WriteException("Startup::Enable", ex);
        }
    }

    /// Turn autostart off completely - removes BOTH the Run key and the scheduled task, so a
    /// user toggling "Start with Windows" off actually stops it on an admin (task) install too.
    public static void Disable()
    {
        DisableRunKey();
        DeleteTask();
    }

    private static void DisableRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Write("Startup::Disable", "Removed the Run-key autostart");
            }
        }
        catch (Exception ex)
        {
            Log.WriteException("Startup::Disable", ex);
        }
    }

    /// The installed copy is what should run at login, not whichever exe happens to be
    /// running now - an installer that copied itself elsewhere, say. The install location
    /// wins when it holds a real executable; otherwise the running one stands in.
    private static string Resolve(string? executablePath)
    {
        if (!string.IsNullOrEmpty(executablePath))
        {
            return executablePath;
        }

        string? installed = FirstRun.RecordedLocation();

        if (!string.IsNullOrEmpty(installed))
        {
            string candidate = Path.Combine(installed, "JelloClient.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return ProtocolHandler.ApplicationPath;
    }

    // ----- Agent autostart: one mechanism, chosen by privilege -----
    //
    // Without admin we use the per-user Run key (visible in Task Manager's Startup tab).
    // With admin we use a logon scheduled task instead - more robust, and it can start
    // elevated - and we drop the Run key so there is only ever one entry, never a stack of
    // them. Uninstall (RemoveAgentAutostart) takes whichever exists back out.

    private const string TaskName = "JelloClient";

    public static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Ensures Jello starts with Windows, self-healing: it installs BOTH a logon scheduled task
    /// and the Run key. Either one alone starts the agent at logon, so if one is deleted the other
    /// still fires - and the agent, on run, calls back here and re-creates whichever is missing.
    /// The agent's single-instance mutex means two triggers never leave two agents running. The
    /// task is elevated (/rl highest) with admin, plain otherwise; if the task can't be created
    /// (e.g. a standard user without task rights) the Run key still carries autostart on its own.
    public static void EnsureAgentAutostart(string? executablePath = null)
    {
        string executable = Resolve(executablePath);

        Enable(executable);   // Run-key trigger (always)

        bool elevated = IsAdmin();
        bool task = CreateTask(executable, elevated);

        Log.Write("Startup::Agent", task
            ? $"Autostart: Run key + scheduled task{(elevated ? " (elevated)" : "")}"
            : "Autostart: Run key (scheduled task unavailable)");
    }

    /// Removes every autostart entry Jello may have created (both mechanisms).
    public static void RemoveAgentAutostart() => Disable();

    private static bool CreateTask(string executable, bool elevated)
    {
        // onlogon so it comes up at sign-in; highest (admin only) so an elevated install stays elevated.
        string runLevel = elevated ? " /rl highest" : "";
        return Schtasks($"/create /tn \"{TaskName}\" /tr \"\\\"{executable}\\\" -agent\" /sc onlogon{runLevel} /f");
    }

    private static void DeleteTask()
    {
        Schtasks($"/delete /tn \"{TaskName}\" /f");
    }

    private static bool TaskExists() => Schtasks($"/query /tn \"{TaskName}\"");

    private static bool Schtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            // Drain nothing (no redirect), just wait; if it hangs, treat as failure.
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(); } catch (Exception) { /* already gone */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Write("Startup::Schtasks", ex.Message);
            return false;
        }
    }
}
