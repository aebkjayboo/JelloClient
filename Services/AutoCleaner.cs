using System.Windows.Threading;
using JelloClient.Roblox;

namespace JelloClient.Services;

/// Runs the same cleanup the Optimizations page runs, on a schedule. Only the categories
/// that are safe to remove without asking are included: old version folders, half finished
/// downloads and logs. Roblox's own asset cache is opt in because clearing it makes the
/// next join slower.
internal static class AutoCleaner
{
    private static readonly CleanupCategory[] SafeCategories =
    {
        CleanupCategory.OrphanedVersions,
        CleanupCategory.StaleDownloads,
        CleanupCategory.RobloxLogs,
        CleanupCategory.JelloLogs
    };

    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static DispatcherTimer? _timer;

    private static bool _running;

    private static UserSettings Settings => AppState.Settings;

    public static TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(Settings.AutoCleanupMinutes, 15, 7 * 24 * 60));

    public static DateTime? NextDueUtc => Settings.AutoCleanup == AutoCleanupMode.Interval
        ? (Settings.LastAutoCleanupUtc ?? DateTime.UtcNow) + Interval
        : null;

    public static string Describe()
    {
        string last = Settings.LastAutoCleanupUtc is { } when
            ? $"Last run {when.ToLocalTime():d MMM HH:mm}."
            : "It has not run yet.";

        return Settings.AutoCleanup switch
        {
            AutoCleanupMode.EveryLaunch => $"Cleans up each time you launch Roblox. {last}",
            AutoCleanupMode.Interval => $"Cleans up every {DescribeInterval(Interval)}. {last}",
            _ => "Cleanup only happens when you press Clean below."
        };
    }

    public static string DescribeInterval(TimeSpan interval)
    {
        int hours = (int)interval.TotalHours;
        int minutes = interval.Minutes;

        if (hours == 0)
        {
            return $"{minutes} minutes";
        }

        string hourPart = hours == 1 ? "hour" : $"{hours} hours";

        return minutes == 0 ? hourPart : $"{hourPart} {minutes} min";
    }

    /// Starts, restarts or stops the timer to match the current settings.
    public static void Reschedule()
    {
        _timer?.Stop();
        _timer = null;

        if (Settings.AutoCleanup != AutoCleanupMode.Interval)
        {
            Log.Write("AutoCleaner::Reschedule", $"Timer off, mode is {Settings.AutoCleanup}");
            return;
        }

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => RunIfDue();
        _timer.Start();

        Log.Write("AutoCleaner::Reschedule", $"Checking every minute, cleaning every {DescribeInterval(Interval)}");

        RunIfDue();
    }

    private static void RunIfDue()
    {
        if (Settings.AutoCleanup != AutoCleanupMode.Interval)
        {
            return;
        }

        var last = Settings.LastAutoCleanupUtc;

        if (last is null)
        {
            Settings.LastAutoCleanupUtc = DateTime.UtcNow;
            AppState.Persist();
            return;
        }

        if (DateTime.UtcNow - last.Value < Interval)
        {
            return;
        }

        Run("the schedule");
    }

    /// Called from the launch path when the mode is set to every launch.
    public static void RunOnLaunch()
    {
        if (Settings.AutoCleanup != AutoCleanupMode.EveryLaunch)
        {
            return;
        }

        Run("a launch");
    }

    private static void Run(string trigger)
    {
        const string ident = "AutoCleaner::Run";

        if (_running)
        {
            return;
        }

        _running = true;

        try
        {
            var wanted = new HashSet<CleanupCategory>(SafeCategories);

            if (Settings.AutoCleanupIncludesCache)
            {
                wanted.Add(CleanupCategory.RobloxCache);
            }

            var groups = Cleaner.Scan()
                .Where(group => wanted.Contains(group.Category) && group.Count > 0)
                .ToList();

            Settings.LastAutoCleanupUtc = DateTime.UtcNow;
            AppState.Persist();

            if (groups.Count == 0)
            {
                Log.Write(ident, $"Triggered by {trigger}, nothing to remove");
                return;
            }

            var result = Cleaner.Clean(groups);

            Log.Write(ident,
                $"Triggered by {trigger}, removed {result.Removed} item(s) freeing {Cleaner.Describe(result.Bytes)}" +
                (result.Failures.Count == 0 ? "" : $", {result.Failures.Count} could not be deleted"));
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
        }
        finally
        {
            _running = false;
        }
    }
}
