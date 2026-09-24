using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text.Json;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed record LiveFlagResult(int Applied, int Matched, int Unknown, int Unsupported, int Failed, string Summary)
{
    public static LiveFlagResult None(string reason) => new(0, 0, 0, 0, 0, reason);
}

/// Writes flag values into the running client's memory, the way the Roblox FastFlag
/// Manager does. This is the risky path and it is off by default.
///
/// Everything here is secondary: ClientAppSettings.json is written and verified before the
/// client starts, so every flag is already applied by the supported route. This only exists
/// for flags the client re-reads after boot, and it gives up at the first sign that the
/// picture does not match what it expects:
///
///   - the dump's build must equal the build we launched, exactly
///   - the process must be the executable in our own version folder
///   - every address must land inside that module's image
///   - only booleans and integers are written; strings are left alone
///   - each value is read back after writing, and three failures stop the pass
///
/// Writes go through ntdll's NtWriteVirtualMemory first and fall back to
/// VirtualProtectEx + WriteProcessMemory, the same order the reference uses. What it does
/// not have is their watchdog: instead of re-enforcing every 50 ms forever, a launch runs
/// a short bounded schedule of passes so the client's own start up cannot silently undo
/// the write, and then stops touching the process.
internal static class LiveFlags
{
    private const int MaximumFailures = 3;

    /// The last pass this session, so the settings card can show what happened at launch
    /// rather than only flashing it in the status bar.
    public static LiveFlagResult? Last { get; private set; }

    public static DateTime? LastRunUtc { get; private set; }

    private const int ProcessVmOperation = 0x0008;
    private const int ProcessVmRead = 0x0010;
    private const int ProcessVmWrite = 0x0020;
    private const int ProcessQueryInformation = 0x0400;

    private const uint PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtectEx(
        IntPtr process, IntPtr address, IntPtr size, uint protect, out uint previous);

    // The native calls the Win32 wrappers above sit on top of. Tried first, exactly the
    // way the reference implementation orders them.
    [DllImport("ntdll.dll")]
    private static extern int NtReadVirtualMemory(
        IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("ntdll.dll")]
    private static extern int NtWriteVirtualMemory(
        IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

    [DllImport("ntdll.dll")]
    private static extern int NtProtectVirtualMemory(
        IntPtr process, ref IntPtr address, ref IntPtr size, uint protect, out uint previous);

    private static int _ntWrites;

    private static int _win32Writes;

    /// Delays between the passes a launch runs. The client sets its own flag variables
    /// while it starts, so a single early write can be overwritten a second later; these
    /// re-check and re-apply until two passes in a row find everything already correct.
    private static readonly TimeSpan[] Schedule =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30)
    };

    public static async Task<LiveFlagResult> ApplyOnLaunchAsync(
        int processId,
        string versionGuid,
        Func<IReadOnlyDictionary<string, JsonElement>> flags,
        HttpClient http,
        Action<LiveFlagResult> report,
        CancellationToken ct)
    {
        const string ident = "LiveFlags::ApplyOnLaunch";

        var last = LiveFlagResult.None("Live flags did not run.");
        int settled = 0;

        for (int pass = 0; pass < Schedule.Length; pass++)
        {
            try
            {
                await Task.Delay(pass == 0 ? Schedule[0] : Schedule[pass] - Schedule[pass - 1], ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return last;
            }

            if (!IsAlive(processId))
            {
                Log.Write(ident, $"Roblox exited before pass {pass + 1}");
                return last;
            }

            last = await ApplyAsync(processId, versionGuid, flags(), http, ct).ConfigureAwait(false);


            Log.Write(ident, $"Pass {pass + 1} of {Schedule.Length}: {last.Summary}");

            report(last);

            if (last.Failed > 0)
            {
                return last;
            }

            settled = last.Applied == 0 ? settled + 1 : 0;

            if (settled >= 2)
            {
                Log.Write(ident, $"Settled after pass {pass + 1}, no further passes needed");
                return last;
            }
        }

        return last;
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            return !Process.GetProcessById(processId).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static async Task<LiveFlagResult> ApplyAsync(
        int processId,
        string versionGuid,
        IReadOnlyDictionary<string, JsonElement> flags,
        HttpClient http,
        CancellationToken ct)
    {
        const string ident = "LiveFlags::Apply";

        if (!AppState.Settings.LiveFlagInjection)
        {
            return LiveFlagResult.None("Live flags are off.");
        }

        if (flags.Count == 0)
        {
            return LiveFlagResult.None("No flags to apply.");
        }

        Process process;

        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return LiveFlagResult.None("Roblox is no longer running.");
        }

        string expected = Path.Combine(Paths.VersionDirectory(versionGuid), Installer.ExecutableName);

        string? actual = SafeModulePath(process);

        if (actual is null || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            Log.Write(ident, $"Process {processId} runs {actual ?? "an unreadable path"}, not {expected}, skipping");

            return LiveFlagResult.None("That Roblox is not the copy Jello launched, so nothing was written.");
        }

        var dump = await OffsetDump.ForVersionAsync(http, versionGuid, ct).ConfigureAwait(false);

        if (dump is null)
        {
            return LiveFlagResult.None($"No offsets published for {versionGuid} yet, so nothing was written.");
        }

        return Write(process, dump, flags);
    }

    /// The running client that came out of our own version folder, or 0. Used when the
    /// settings window was opened separately from the launch that started Roblox.
    public static int FindRunningClient(string versionGuid)
    {
        string expected = Path.Combine(Paths.VersionDirectory(versionGuid), Installer.ExecutableName);

        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            if (string.Equals(SafeModulePath(process), expected, StringComparison.OrdinalIgnoreCase))
            {
                return process.Id;
            }
        }

        return 0;
    }

    /// Read only: how many of the current flags this build's dump even knows about. Touches
    /// no process memory, so it is safe to run with the toggle off.
    public static async Task<string> PreviewAsync(
        string versionGuid,
        IReadOnlyDictionary<string, JsonElement> flags,
        HttpClient http,
        CancellationToken ct)
    {
        var dump = await OffsetDump.ForVersionAsync(http, versionGuid, ct).ConfigureAwait(false);

        if (dump is null)
        {
            return $"No offsets are published for {versionGuid}, so live flags can do nothing on this build.";
        }

        int known = 0;
        int unsupported = 0;
        var missing = new List<string>();

        foreach (var pair in flags)
        {
            string? prefix = FastFlagTypes.PrefixOf(pair.Key);

            if (prefix is null || FastFlagTypes.KindOf(pair.Key) == FlagValueKind.Text)
            {
                unsupported++;
                continue;
            }

            if (dump.Offsets.ContainsKey(pair.Key[prefix.Length..]))
            {
                known++;
            }
            else
            {
                missing.Add(pair.Key);
            }
        }

        Log.Write("LiveFlags::Preview",
            $"{known} of {flags.Count} flag(s) exist in the {versionGuid} dump, {unsupported} unsupported, {missing.Count} missing");

        if (missing.Count > 0)
        {
            Log.Write("LiveFlags::Preview", "Missing: " + string.Join(", ", missing.Take(8)));
        }

        return $"{known} of {flags.Count} flag(s) exist in this build" +
               (unsupported > 0 ? $", {unsupported} are strings or have no Roblox prefix" : "") +
               (missing.Count > 0 ? $", {missing.Count} are not in the client at all" : "") + ".";
    }

    private static string? SafeModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            Log.Write("LiveFlags::ModulePath", $"Could not read the module path: {ex.Message}");
            return null;
        }
    }

    private static LiveFlagResult Write(
        Process process,
        OffsetDump dump,
        IReadOnlyDictionary<string, JsonElement> flags)
    {
        const string ident = "LiveFlags::Write";

        IntPtr baseAddress;
        long imageSize;

        try
        {
            baseAddress = process.MainModule!.BaseAddress;
            imageSize = process.MainModule.ModuleMemorySize;
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            return LiveFlagResult.None("Could not read the client's module layout.");
        }

        IntPtr handle = OpenProcess(
            ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation,
            false,
            process.Id);

        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();

            Log.Write(ident, $"OpenProcess failed with {error}");

            return LiveFlagResult.None("Windows would not let Jello open the Roblox process.");
        }

        _ntWrites = 0;
        _win32Writes = 0;

        int applied = 0;
        int matched = 0;
        int unknown = 0;
        int unsupported = 0;
        int failed = 0;

        var missing = new List<string>();

        try
        {
            foreach (var pair in flags)
            {
                var kind = FastFlagTypes.KindOf(pair.Key);

                if (kind == FlagValueKind.Text)
                {
                    unsupported++;
                    continue;
                }

                string? prefix = FastFlagTypes.PrefixOf(pair.Key);

                if (prefix is null)
                {
                    unsupported++;
                    continue;
                }

                string name = pair.Key[prefix.Length..];

                if (!dump.Offsets.TryGetValue(name, out uint rva) || rva >= imageSize)
                {
                    unknown++;
                    missing.Add(pair.Key);
                    continue;
                }

                byte[]? wanted = Encode(kind, pair.Value);

                if (wanted is null)
                {
                    unsupported++;
                    continue;
                }

                IntPtr address = baseAddress + (nint)rva;

                byte[] current = new byte[wanted.Length];

                if (!Read(handle, address, current))
                {
                    failed++;
                }
                else if (current.SequenceEqual(wanted))
                {
                    matched++;
                    continue;
                }
                else if (WriteVerified(handle, address, wanted))
                {
                    applied++;
                    continue;
                }
                else
                {
                    failed++;
                }

                if (failed >= MaximumFailures)
                {
                    Log.Write(ident, $"Stopping after {failed} failed writes, the dump does not match this client");

                    return new LiveFlagResult(applied, matched, unknown, unsupported, failed,
                        $"Stopped after {failed} refused writes. The offsets do not line up with this client, so live flags were abandoned.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);

            return new LiveFlagResult(applied, matched, unknown, unsupported, failed, $"Live flags stopped: {ex.Message}");
        }
        finally
        {
            CloseHandle(handle);
        }

        Log.Write(ident,
            $"{applied} written ({_ntWrites} via NtWriteVirtualMemory, {_win32Writes} after unprotecting), " +
            $"{matched} already correct, {unknown} not in this build, " +
            $"{unsupported} unsupported, {failed} refused, dump from {dump.Source}");

        if (missing.Count > 0)
        {
            Log.Write(ident, "Not in this build: " + string.Join(", ", missing.Take(8)) +
                             (missing.Count > 8 ? $" and {missing.Count - 8} more" : ""));
        }

        var parts = new List<string> { $"{applied} written" };

        if (matched > 0)
        {
            parts.Add($"{matched} already correct");
        }

        if (unknown > 0)
        {
            parts.Add($"{unknown} not in this build");
        }

        if (unsupported > 0)
        {
            parts.Add($"{unsupported} unsupported");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} refused");
        }

        var result = new LiveFlagResult(applied, matched, unknown, unsupported, failed,
            "Live flags: " + string.Join(", ", parts) + ".");

        Last = result;
        LastRunUtc = DateTime.UtcNow;

        return result;
    }

    private static byte[]? Encode(FlagValueKind kind, JsonElement value)
    {
        string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();

        if (kind == FlagValueKind.Boolean)
        {
            return text.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => new byte[] { 1 },
                "false" or "0" or "no" or "off" => new byte[] { 0 },
                _ => null
            };
        }

        return int.TryParse(text.Trim(), out int number) ? BitConverter.GetBytes(number) : null;
    }

    /// NtReadVirtualMemory, falling back to the Win32 wrapper.
    private static bool Read(IntPtr handle, IntPtr address, byte[] buffer)
    {
        if (NtReadVirtualMemory(handle, address, buffer, buffer.Length, out IntPtr read) == 0
            && read == buffer.Length)
        {
            return true;
        }

        return ReadProcessMemory(handle, address, buffer, buffer.Length, out IntPtr fallback)
               && fallback == buffer.Length;
    }

    /// Writes, then reads the same address back. A write that reports success but does not
    /// stick means the page is not what the dump thinks it is, and the caller stops.
    ///
    /// Order matches the reference: the native call first, then unprotecting the page and
    /// going through the Win32 wrapper. Hyperion keeps parts of the image read only, and
    /// those pages fail both ways rather than being forced.
    private static bool WriteVerified(IntPtr handle, IntPtr address, byte[] wanted)
    {
        if (NtWriteVirtualMemory(handle, address, wanted, wanted.Length, out IntPtr written) == 0
            && written == wanted.Length)
        {
            _ntWrites++;
        }
        else if (Unprotected(handle, address, wanted))
        {
            _win32Writes++;
        }
        else
        {
            return false;
        }

        byte[] check = new byte[wanted.Length];

        return Read(handle, address, check) && check.SequenceEqual(wanted);
    }

    private static bool Unprotected(IntPtr handle, IntPtr address, byte[] wanted)
    {
        IntPtr target = address;
        IntPtr size = wanted.Length;

        if (NtProtectVirtualMemory(handle, ref target, ref size, PageReadWrite, out uint previous) != 0
            && !VirtualProtectEx(handle, address, wanted.Length, PageReadWrite, out previous))
        {
            return false;
        }

        bool written = (NtWriteVirtualMemory(handle, address, wanted, wanted.Length, out IntPtr count) == 0
                        && count == wanted.Length)
                       || (WriteProcessMemory(handle, address, wanted, wanted.Length, out count)
                           && count == wanted.Length);

        target = address;
        size = wanted.Length;

        if (NtProtectVirtualMemory(handle, ref target, ref size, previous, out _) != 0)
        {
            VirtualProtectEx(handle, address, wanted.Length, previous, out _);
        }

        return written;
    }
}
