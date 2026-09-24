using System.Diagnostics;
using System.Runtime.InteropServices;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Makes a second copy of the same Roblox build run alongside the first.
///
/// Two separate guards stop that from working, and holding the singleton only clears one
/// of them:
///
///   1. ROBLOX_singletonMutex - the "one Roblox at a time" check. Holding it ourselves is
///      enough to get past this, which is why two *different* version folders can already
///      run at once.
///   2. The NoReload detector - keyed on the install the client was started from. A second
///      launch out of the same folder logs "NoReload Detector: warm start adjustment" then
///      "App NoReload!" and destroys itself before it opens a window. No amount of mutex
///      holding changes that, because it is not the mutex talking.
///
/// So a second instance is given its own folder. The clone is built out of hard links, so
/// it shares the bytes on disk with the original - a full 3,000 file client costs a few
/// seconds and effectively no space - and only the settings file is a real copy, since
/// that one gets rewritten per launch.
internal static class MultiInstance
{
    private const int MaximumSlots = 5;

    private const string SlotMarker = "__jello";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string link, string target, IntPtr attributes);

    public static bool IsSlot(string directoryName) =>
        directoryName.Contains(SlotMarker, StringComparison.Ordinal);

    /// The version folder a slot was cloned from, or null when it is not a slot.
    public static string? BaseVersionOf(string directoryName)
    {
        int marker = directoryName.IndexOf(SlotMarker, StringComparison.Ordinal);

        return marker <= 0 ? null : directoryName[..marker];
    }

    /// The executable the next launch should use: the original when nothing is running out
    /// of it, otherwise a free slot beside it.
    public static string Resolve(string executable, IReadOnlyDictionary<string, object> fastFlags)
    {
        const string ident = "MultiInstance::Resolve";

        string primary = Path.GetDirectoryName(executable)!;

        if (!IsRunningFrom(primary))
        {
            return executable;
        }

        Log.Write(ident, $"A client is already running from {Path.GetFileName(primary)}, looking for a free slot");

        for (int slot = 2; slot <= MaximumSlots; slot++)
        {
            string candidate = $"{primary}{SlotMarker}{slot}";

            if (IsRunningFrom(candidate))
            {
                continue;
            }

            try
            {
                Clone(primary, candidate);
                Stage(candidate, fastFlags);

                string cloned = Path.Combine(candidate, Installer.ExecutableName);

                Log.Write(ident, $"Second instance will run from {Path.GetFileName(candidate)}");

                return cloned;
            }
            catch (Exception ex)
            {
                Log.WriteException(ident, ex);

                throw new IOException(
                    $"Jello could not prepare a second copy of Roblox in {candidate}: {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException(
            $"All {MaximumSlots} instance slots are in use. Close one of the running clients first.");
    }

    public static bool IsRunningFrom(string versionDirectory)
    {
        string executable = Path.Combine(versionDirectory, Installer.ExecutableName);

        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // A process we cannot query is not one of ours.
            }
        }

        return false;
    }

    /// Rebuilds the slot when it is missing or its client no longer matches the original.
    private static void Clone(string primary, string slot)
    {
        const string ident = "MultiInstance::Clone";

        if (Directory.Exists(slot) && !NeedsRebuild(primary, slot))
        {
            Log.Write(ident, $"{Path.GetFileName(slot)} is already in place");
            return;
        }

        if (Directory.Exists(slot))
        {
            Log.Write(ident, $"{Path.GetFileName(slot)} is out of date, rebuilding it");
            Directory.Delete(slot, true);
        }

        var started = Stopwatch.StartNew();
        int linked = 0;
        int copied = 0;

        foreach (string directory in Directory.EnumerateDirectories(primary, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(primary, slot, StringComparison.OrdinalIgnoreCase));
        }

        Directory.CreateDirectory(slot);

        foreach (string file in Directory.EnumerateFiles(primary, "*", SearchOption.AllDirectories))
        {
            string destination = file.Replace(primary, slot, StringComparison.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (CreateHardLinkW(destination, file, IntPtr.Zero))
            {
                linked++;
                continue;
            }

            // Different volume, or a filesystem without hard links: fall back to a copy.
            File.Copy(file, destination, true);
            copied++;
        }

        Log.Write(ident,
            $"Built {Path.GetFileName(slot)} from {linked} hard link(s) and {copied} copy(s) in {started.ElapsedMilliseconds} ms");
    }

    private static bool NeedsRebuild(string primary, string slot)
    {
        var original = new FileInfo(Path.Combine(primary, Installer.ExecutableName));
        var clone = new FileInfo(Path.Combine(slot, Installer.ExecutableName));

        return !clone.Exists
               || !original.Exists
               || clone.Length != original.Length
               || clone.LastWriteTimeUtc != original.LastWriteTimeUtc;
    }

    /// The settings file is hard linked like everything else, so it is deleted and written
    /// fresh - otherwise writing it would change the original's copy too.
    private static void Stage(string slot, IReadOnlyDictionary<string, object> fastFlags)
    {
        string settings = FastFlagWriter.PathFor(slot);

        if (File.Exists(settings))
        {
            File.Delete(settings);
        }

        if (!FastFlagWriter.WriteVerified(slot, fastFlags))
        {
            throw new IOException($"The flags could not be written into {slot}.");
        }
    }

    /// Slots left behind by closed clients, so cleanup can offer them.
    public static IReadOnlyList<string> IdleSlots(string versionsRoot)
    {
        if (!Directory.Exists(versionsRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(versionsRoot)
            .Where(directory => IsSlot(Path.GetFileName(directory)) && !IsRunningFrom(directory))
            .ToList();
    }
}
