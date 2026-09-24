using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient;

internal sealed class CleanupRow : INotifyPropertyChanged
{
    private bool? _selected;

    public required CleanupGroup Group { get; init; }

    public string Title => Group.Title;

    public string Description => Group.Description;

    public bool HasItems => Group.Count > 0;

    public string Size => Group.Count == 0 ? "nothing" : Cleaner.Describe(Group.Bytes);

    public string Detail => Group.Count == 0
        ? "Nothing to remove."
        : $"{Group.Count} item(s): " + string.Join(", ",
            Group.Items.Take(3).Select(item => Path.GetFileName(item.Path.TrimEnd(Path.DirectorySeparatorChar)))) +
          (Group.Count > 3 ? $" and {Group.Count - 3} more" : "");

    public bool Selected
    {
        get => _selected ?? HasItems;
        set
        {
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class SettingsWindow
{
    private List<CleanupRow> _cleanupRows = new();

    private static readonly AutoCleanupMode[] AutoCleanupOrder =
        { AutoCleanupMode.Off, AutoCleanupMode.EveryLaunch, AutoCleanupMode.Interval };

    private static readonly string[] AutoCleanupLabels = { "Off", "Every launch", "Every few hours" };

    private void InitialiseCleanupLists()
    {
        foreach (string label in AutoCleanupLabels)
        {
            AutoCleanupCombo.Items.Add(new ComboBoxItem { Content = label });
        }

        AutoCleanupPicker.ValueChanged += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            Settings.AutoCleanupMinutes = AutoCleanupPicker.Minutes;
            Persist();

            AutoCleaner.Reschedule();
            RefreshAutoCleanupState();

            SetStatus($"Cleaning up every {AutoCleaner.DescribeInterval(AutoCleaner.Interval)}.");
        };
    }

    private void LoadCleanupState()
    {
        AutoCleanupCombo.SelectedIndex = Math.Max(0, Array.IndexOf(AutoCleanupOrder, Settings.AutoCleanup));
        AutoCleanupPicker.Minutes = Settings.AutoCleanupMinutes;
        AutoCleanupCacheToggle.IsChecked = Settings.AutoCleanupIncludesCache;

        RefreshAutoCleanupState();
        RefreshCleanupAsync();
    }

    private void RefreshAutoCleanupState()
    {
        AutoCleanupText.Text = AutoCleaner.Describe();

        AutoCleanupIntervalRow.Visibility = Settings.AutoCleanup == AutoCleanupMode.Interval
            ? Visibility.Visible
            : Visibility.Collapsed;

        AutoCleanupNextText.Text = AutoCleaner.NextDueUtc is { } due
            ? $"Next run around {due.ToLocalTime():d MMM HH:mm}."
            : "";
    }

    private void AutoCleanupMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.AutoCleanup = AutoCleanupOrder[AutoCleanupCombo.SelectedIndex];
        Persist();

        AutoCleaner.Reschedule();
        RefreshAutoCleanupState();

        SetStatus($"Automatic cleanup set to {AutoCleanupLabels[AutoCleanupCombo.SelectedIndex].ToLowerInvariant()}.");
    }

    private void AutoCleanupCache_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.AutoCleanupIncludesCache = AutoCleanupCacheToggle.IsChecked == true;
        Persist();

        SetStatus(Settings.AutoCleanupIncludesCache
            ? "Automatic cleanup will clear the Roblox asset cache too."
            : "Automatic cleanup will leave the Roblox asset cache alone.");
    }

    /// Scans as soon as the tab is built, and again after a clean. Asking someone to press
    /// Scan and then press Clean was two steps to do one thing: opening the page is already
    /// the request to know what is there.
    private async void RefreshCleanupAsync()
    {
        CleanupSummaryText.Text = "Looking...";

        try
        {
            // Walking the version folders touches a lot of files, so it is done off the
            // interface thread and the page stays responsive while it runs.
            var groups = await Task.Run(Cleaner.Scan);

            _cleanupRows = groups.Select(group => new CleanupRow { Group = group }).ToList();

            CleanupGroups.ItemsSource = null;
            CleanupGroups.ItemsSource = _cleanupRows;

            long total = _cleanupRows.Sum(row => row.Group.Bytes);
            int items = _cleanupRows.Sum(row => row.Group.Count);

            CleanupSummaryText.Text = items == 0
                ? "Nothing to clean. Everything here is either in use or already tidy."
                : $"{items} item(s) using {Cleaner.Describe(total)} can go. Untick anything you want to keep.";

            CleanButton.IsEnabled = items > 0;
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::RefreshCleanup", ex);
            CleanupSummaryText.Text = $"Could not look: {ex.Message}";
        }
    }

    private void RunCleanup_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _cleanupRows.Where(row => row.Selected && row.HasItems).ToList();

        Log.Write("SettingsWindow::RunCleanup",
            $"{chosen.Count} of {_cleanupRows.Count} categories selected: " +
            string.Join(", ", _cleanupRows.Select(r => $"{r.Title}={r.Selected}/{r.HasItems}")));

        if (chosen.Count == 0)
        {
            SetStatus("Nothing selected to clean.");
            return;
        }

        long bytes = chosen.Sum(row => row.Group.Bytes);
        int items = chosen.Sum(row => row.Group.Count);

        string summary = string.Join("\n", chosen.Select(row => $"  - {row.Title}: {row.Group.Count} item(s), {Cleaner.Describe(row.Group.Bytes)}"));

        var answer = MessageBox.Show(
            $"Delete {items} item(s), freeing {Cleaner.Describe(bytes)}?\n\n{summary}\n\nThis cannot be undone.",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        CleanButton.IsEnabled = false;

        try
        {
            var result = Cleaner.Clean(chosen.Select(row => row.Group));

            CleanupSummaryText.Text = result.Failures.Count == 0
                ? $"Removed {result.Removed} item(s) and freed {Cleaner.Describe(result.Bytes)}."
                : $"Removed {result.Removed} item(s) and freed {Cleaner.Describe(result.Bytes)}. {result.Failures.Count} item(s) could not be deleted.";

            SetStatus(CleanupSummaryText.Text);

            RefreshCleanupAsync();
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::RunCleanup", ex);
            CleanupSummaryText.Text = $"Cleanup failed: {ex.Message}";
        }
    }
}
