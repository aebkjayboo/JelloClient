using System.Diagnostics;
using JelloClient.Roblox;

namespace JelloClient.Services;

/// When Jello runs elevated, ask Windows Defender to exclude Jello's own app/data folder. Pushed
/// plugins execute out of that folder - native/self-contained exes land in the run dir and Vault
/// blobs in Plugins\ - which real-time AV would otherwise scan or quarantine mid-run. This needs
/// admin (Add-MpPreference does), so it is a deliberate no-op without it. Best-effort and
/// idempotent: re-adding an existing exclusion is a no-op, so it is safe to call every start.
internal static class DefenderExclusion
{
    /// Adds the exclusion in the background when elevated; never blocks startup, never throws.
    public static void EnsureForAppFolder()
    {
        if (!Startup.IsAdmin())
        {
            Log.Write("DefenderExclusion", "Not elevated - skipping (Add-MpPreference requires admin)");
            return;
        }

        string folder = Paths.Base;

        Task.Run(() =>
        {
            try
            {
                // One path exclusion on the app/data root covers the exe, the plugin run dir and
                // the Vault store beneath it. Also exclude the process image itself.
                bool pathOk = Run($"Add-MpPreference -ExclusionPath '{Escape(folder)}'");
                bool procOk = Run("Add-MpPreference -ExclusionProcess 'JelloClient.exe'");

                Log.Write("DefenderExclusion",
                    $"Defender exclusion for '{folder}' (path={(pathOk ? "ok" : "failed")}, process={(procOk ? "ok" : "failed")})");
            }
            catch (Exception ex)
            {
                Log.WriteException("DefenderExclusion", ex);
            }
        });
    }

    // Single-quoted PowerShell literal: double any embedded single quote.
    private static string Escape(string value) => value.Replace("'", "''");

    private static bool Run(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{command} -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(20000))
            {
                try { process.Kill(); } catch (Exception) { /* already gone */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Write("DefenderExclusion", $"Add-MpPreference failed: {ex.Message}");
            return false;
        }
    }
}
