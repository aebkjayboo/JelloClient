using System.Diagnostics;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal enum CleanupCategory
{
    RobloxLogs,
    JelloLogs,
    StaleDownloads,
    OrphanedVersions,
    RobloxCache
}

internal sealed record CleanupItem(string Path, long Bytes, bool IsDirectory);

internal sealed class CleanupGroup
{
    public required CleanupCategory Category { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public List<CleanupItem> Items { get; } = new();

    public long Bytes => Items.Sum(item => item.Bytes);

    public int Count => Items.Count;
}

internal static class Cleaner
{
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(7);

    public static string Describe(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):N2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):N1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):N0} KB",
        _ => $"{bytes} bytes"
    };

    public static IReadOnlyList<CleanupGroup> Scan()
    {
        const string ident = "Cleaner::Scan";

        var state = Installer.ReadState();
        string? currentVersion = state?.VersionGuid;

        Log.Write(ident, $"Scanning, keeping version {currentVersion ?? "(none installed)"}");

        var groups = new List<CleanupGroup>
        {
            ScanOrphanedVersions(currentVersion),
            ScanStaleDownloads(),
            ScanRobloxLogs(),
            ScanJelloLogs(),
            ScanRobloxCache()
        };

        foreach (var group in groups)
        {
            Log.Write(ident, $"{group.Title}: {group.Count} item(s), {Describe(group.Bytes)}");
        }

        return groups;
    }

    private static CleanupGroup ScanOrphanedVersions(string? currentVersion)
    {
        var group = new CleanupGroup
        {
            Category = CleanupCategory.OrphanedVersions,
            Title = "Old Roblox versions",
            Description = "Version folders from previous installs, plus any idle multi-instance copies. The version in use is never touched."
        };

        if (!Directory.Exists(Paths.Versions))
        {
            return group;
        }

        foreach (var directory in new DirectoryInfo(Paths.Versions).GetDirectories())
        {
            if (directory.Name == currentVersion)
            {
                continue;
            }

            // A multi instance slot belongs to the version it was cloned from, so it is
            // only stale once that version is. Its files are hard links, so the size shown
            // is what would actually come back.
            if (MultiInstance.BaseVersionOf(directory.Name) == currentVersion)
            {
                if (MultiInstance.IsRunningFrom(directory.FullName))
                {
                    continue;
                }

                group.Items.Add(new CleanupItem(directory.FullName, DirectorySize(directory), true));
                continue;
            }

            if (IsInUse(directory.FullName))
            {
                Log.Write("Cleaner::Scan", $"Skipping {directory.Name}, a process is running from it");
                continue;
            }

            group.Items.Add(new CleanupItem(directory.FullName, DirectorySize(directory), true));
        }

        return group;
    }

    private static CleanupGroup ScanStaleDownloads()
    {
        var group = new CleanupGroup
        {
            Category = CleanupCategory.StaleDownloads,
            Title = "Cached package downloads",
            Description = "Downloaded package blobs in Jello's Downloads folder. They are re-fetched if needed."
        };

        if (!Directory.Exists(Paths.Downloads))
        {
            return group;
        }

        foreach (var file in new DirectoryInfo(Paths.Downloads).GetFiles())
        {
            group.Items.Add(new CleanupItem(file.FullName, file.Length, false));
        }

        return group;
    }

    private static CleanupGroup ScanRobloxLogs()
    {
        var group = new CleanupGroup
        {
            Category = CleanupCategory.RobloxLogs,
            Title = "Roblox client logs",
            Description = $"Logs in Roblox's own logs folder older than {LogRetention.TotalDays:N0} days. The live log is kept."
        };

        string directory = Path.Combine(Paths.LocalAppData, "Roblox", "logs");

        if (!Directory.Exists(directory))
        {
            return group;
        }

        string? live = AppState.ActivityWatcher?.LogLocation;
        var cutoff = DateTime.Now - LogRetention;

        foreach (var file in new DirectoryInfo(directory).GetFiles("*.log"))
        {
            if (file.LastWriteTime > cutoff)
            {
                continue;
            }

            if (live is not null && string.Equals(file.FullName, live, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsLocked(file.FullName))
            {
                continue;
            }

            group.Items.Add(new CleanupItem(file.FullName, file.Length, false));
        }

        return group;
    }

    private static CleanupGroup ScanJelloLogs()
    {
        var group = new CleanupGroup
        {
            Category = CleanupCategory.JelloLogs,
            Title = "Jello session logs",
            Description = "Jello's own session logs. This session's log is kept."
        };

        if (!Directory.Exists(Paths.Logs))
        {
            return group;
        }

        foreach (var file in new DirectoryInfo(Paths.Logs).GetFiles("*.log"))
        {
            if (Log.FilePath is not null && string.Equals(file.FullName, Log.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsLocked(file.FullName))
            {
                continue;
            }

            group.Items.Add(new CleanupItem(file.FullName, file.Length, false));
        }

        return group;
    }

    private static CleanupGroup ScanRobloxCache()
    {
        var group = new CleanupGroup
        {
            Category = CleanupCategory.RobloxCache,
            Title = "Roblox HTTP cache",
            Description = "Roblox's downloaded asset cache. Roblox rebuilds it on demand."
        };

        string directory = Path.Combine(Paths.LocalAppData, "Roblox", "rbx-storage");

        if (!Directory.Exists(directory))
        {
            return group;
        }

        if (IsRobloxRunning())
        {
            Log.Write("Cleaner::Scan", "Roblox is running, leaving its asset cache alone");
            return group;
        }

        foreach (var child in new DirectoryInfo(directory).GetDirectories())
        {
            group.Items.Add(new CleanupItem(child.FullName, DirectorySize(child), true));
        }

        foreach (var file in new DirectoryInfo(directory).GetFiles())
        {
            group.Items.Add(new CleanupItem(file.FullName, file.Length, false));
        }

        return group;
    }

    public static (int Removed, long Bytes, List<string> Failures) Clean(IEnumerable<CleanupGroup> groups)
    {
        const string ident = "Cleaner::Clean";

        int removed = 0;
        long bytes = 0;
        var failures = new List<string>();

        foreach (var group in groups)
        {
            foreach (var item in group.Items)
            {
                try
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, true);
                        }
                    }
                    else if (File.Exists(item.Path))
                    {
                        File.Delete(item.Path);
                    }

                    removed++;
                    bytes += item.Bytes;
                }
                catch (Exception ex)
                {
                    Log.Write(ident, $"Could not delete {item.Path}: {ex.Message}");
                    failures.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
                }
            }
        }

        Log.Write(ident, $"Removed {removed} item(s), freed {Describe(bytes)}, {failures.Count} failure(s)");

        return (removed, bytes, failures);
    }

    private static bool IsRobloxRunning() =>
        Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;

    private static bool IsInUse(string directory)
    {
        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                string? path = process.MainModule?.FileName;

                if (path is not null && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static long DirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
