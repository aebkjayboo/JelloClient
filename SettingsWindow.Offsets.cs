using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JelloClient.Roblox;
using JelloClient.Roblox.Memory;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private void LoadOverrideState()
    {
        _suppressEvents = true;

        OverrideToggle.IsChecked = Settings.OffsetsOverrideEnabled;
        MemoryOffsetsBox.Text = Settings.MemoryOffsetsOverrideJson ?? "";
        FlagOffsetsBox.Text = Settings.FlagOffsetsOverrideJson ?? "";

        _suppressEvents = false;

        PlaceOverridePanel();
        RefreshOverrideState();
    }

    /// The one override panel lives in the Lab while the Lab is on, and in its own Offsets
    /// tab while the Lab is off, so it is reachable either way without existing twice.
    private void PlaceOverridePanel()
    {
        bool inLab = Settings.LabEnabled;

        ContentControl target = inLab ? OverrideHostLab : OverrideHostTab;

        if (!ReferenceEquals(OverridePanel.Parent, target))
        {
            if (OverridePanel.Parent is ContentControl current)
            {
                current.Content = null;
            }

            target.Content = OverridePanel;
        }

        OffsetsTab.Visibility = inLab ? Visibility.Collapsed : Visibility.Visible;

        // If the Offsets tab was the selected one and has just been hidden, step off it so
        // the content pane is not left blank.
        if (inLab && ReferenceEquals(NavRailTabs.SelectedItem, OffsetsTab))
        {
            NavRailTabs.SelectedIndex = 0;
        }
    }

    private void RefreshOverrideState()
    {
        bool on = Settings.OffsetsOverrideEnabled;

        OverrideText.Text = on
            ? "On. A box holding valid JSON is used in place of a fetch; an empty box still fetches for your build."
            : "Off. Both tables are fetched from the published sources for your build, the usual way.";

        OverrideBoxes.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        RefreshOverrideStatus();
    }

    private void RefreshOverrideStatus()
    {
        string? installed = Installer.ReadState()?.VersionGuid;

        var memory = RobloxOffsets.Inspect(Settings.MemoryOffsetsOverrideJson, installed);
        MemoryOffsetsStatus.Text = memory.Message;
        MemoryOffsetsStatus.Foreground = StatusBrush(memory, installed);

        var flags = OffsetDump.Inspect(Settings.FlagOffsetsOverrideJson, installed);
        FlagOffsetsStatus.Text = flags.Message;
        FlagOffsetsStatus.Foreground = StatusBrush(flags, installed);
    }

    /// A usable table that is stamped for a different build than the one installed reads as
    /// a warning; anything else is a quiet hint in the ordinary text colour.
    private Brush StatusBrush(OffsetOverride.Check check, string? installed)
    {
        bool outdated = check.Valid
            && check.Version is not null
            && installed is not null
            && !string.Equals(check.Version, installed, StringComparison.OrdinalIgnoreCase);

        string key = outdated ? "WarningText" : "TextTertiary";

        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private void Override_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.OffsetsOverrideEnabled = OverrideToggle.IsChecked == true;
        Persist();

        RefreshOverrideState();

        SetStatus(Settings.OffsetsOverrideEnabled
            ? "Your pasted offsets will be used where a box holds valid JSON."
            : "Offsets are fetched for your build again.");
    }

    private void MemoryOffsets_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        string text = MemoryOffsetsBox.Text.Trim();
        Settings.MemoryOffsetsOverrideJson = text.Length == 0 ? null : text;
        Persist();

        RefreshOverrideStatus();
    }

    private void FlagOffsets_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        string text = FlagOffsetsBox.Text.Trim();
        Settings.FlagOffsetsOverrideJson = text.Length == 0 ? null : text;
        Persist();

        RefreshOverrideStatus();
    }
}
