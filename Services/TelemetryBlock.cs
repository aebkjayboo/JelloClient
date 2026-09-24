using System.Diagnostics;
using System.Text;

namespace JelloClient.Services;

/// Points Roblox's telemetry hosts at nowhere, through the Windows hosts file.
///
/// Two things follow from using the hosts file, and both are told to the person before
/// they turn it on rather than discovered afterwards:
///
/// It needs administrator rights, because the file lives under System32\drivers\etc. Jello
/// does not run elevated, so applying or removing the block relaunches just this one step
/// elevated and exits.
///
/// It is machine wide. These names are blocked for every program and every account on the
/// computer, not only for Roblox under Jello. That is also why every line Jello writes is
/// marked, so removing the block takes out exactly what Jello added and nothing a person
/// put there themselves.
///
/// Roblox keeps working with these blocked - they carry counters and crash reports, not
/// anything the game needs - but this is not a privacy guarantee: it stops the hosts
/// listed here, on this machine, by name.
internal static class TelemetryBlock
{
    private const string Marker = "# JelloClient telemetry block";

    /// The hosts Roblox uses for counters, tracing and crash upload.
    public static readonly IReadOnlyList<string> Hosts = new[]
    {
        "client-telemetry.roblox.com",
        "ephemeralcounters.api.roblox.com",
        "metrics.roblox.com",
        "tracing.roblox.com",
        "lms.roblox.com",
        "ncs.roblox.com",
        "gold.roblox.com",
        "upload.crashes.roblox.com",
        "upload.crashes.rbxinfra.com"
    };

    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();

                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// How many of our hosts are currently blocked. Counted rather than a plain bool so a
    /// half applied block, from an edit by hand or a failed write, is visible.
    public static int BlockedCount()
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                return 0;
            }

            var lines = File.ReadAllLines(HostsPath);

            return Hosts.Count(host => lines.Any(line => IsOurs(line) && Mentions(line, host)));
        }
        catch (Exception ex)
        {
            Log.Write("TelemetryBlock::BlockedCount", $"Could not read the hosts file: {ex.Message}");
            return 0;
        }
    }

    public static bool IsApplied() => BlockedCount() == Hosts.Count;

    private static bool IsOurs(string line) => line.Contains(Marker, StringComparison.Ordinal);

    private static bool Mentions(string line, string host) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, host, StringComparison.OrdinalIgnoreCase));

    /// Rewrites the file with our block either present or gone. Runs only when elevated;
    /// the caller is expected to have used Request for that.
    public static void Write(bool blocked)
    {
        const string ident = "TelemetryBlock::Write";

        string path = HostsPath;

        var kept = File.Exists(path)
            ? File.ReadAllLines(path).Where(line => !IsOurs(line)).ToList()
            : new List<string>();

        // Trailing blank lines accumulate otherwise, one per toggle.
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        var builder = new StringBuilder();

        foreach (string line in kept)
        {
            builder.AppendLine(line);
        }

        if (blocked)
        {
            builder.AppendLine();

            foreach (string host in Hosts)
            {
                builder.AppendLine($"0.0.0.0 {host} {Marker}");
            }
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));

        Log.Write(ident, blocked
            ? $"Blocked {Hosts.Count} telemetry host(s)"
            : "Removed Jello's telemetry block");
    }

    /// Runs the change elevated. Returns false when the person declines the prompt, which
    /// is an ordinary answer and not an error.
    public static bool Request(bool blocked)
    {
        const string ident = "TelemetryBlock::Request";

        if (IsElevated)
        {
            Write(blocked);
            return true;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                Arguments = blocked ? "-blocktelemetry" : "-unblocktelemetry",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(start);

            process?.WaitForExit(30_000);

            bool applied = IsApplied();

            Log.Write(ident, $"Elevated pass finished, block is now {(applied ? "on" : "off")}");

            return applied == blocked;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223: the person said no to the elevation prompt.
            Log.Write(ident, "The elevation prompt was declined");
            return false;
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            return false;
        }
    }

    public static string Describe()
    {
        int blocked = BlockedCount();

        if (blocked == 0)
        {
            return $"Off. Roblox reaches all {Hosts.Count} of its telemetry hosts.";
        }

        if (blocked < Hosts.Count)
        {
            return $"Partly on: {blocked} of {Hosts.Count} hosts blocked. Turning it off and on again fixes that.";
        }

        return $"On. All {Hosts.Count} hosts point at nowhere, for every program on this computer.";
    }
}
