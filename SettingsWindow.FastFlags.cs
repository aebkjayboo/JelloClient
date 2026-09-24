using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JelloClient.Roblox;
using JelloClient.Services;
using JelloClient.UI;

namespace JelloClient;

internal sealed class PresetRow : INotifyPropertyChanged
{
    public required FlagPreset Preset { get; init; }

    public required Func<FlagPreset, bool> IsOn { get; init; }

    public required Action<FlagPreset, bool> Toggle { get; init; }

    public string Name => Preset.Name;

    public string Description => Preset.Description;

    public string FlagSummary => Preset.Flags.Count == 1
        ? Preset.Flags[0].Flag
        : $"{Preset.Flags.Count} flags, including {Preset.Flags[0].Flag}";

    public bool Enabled
    {
        get => IsOn(Preset);
        set
        {
            Toggle(Preset, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class PresetGroup
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<PresetRow> Items { get; init; }
}

internal sealed class FlagItem : INotifyPropertyChanged
{
    private static Brush PresetYes =>
        Application.Current?.TryFindResource("PresetYes") as Brush ?? Brushes.LimeGreen;

    private static Brush PresetNo =>
        Application.Current?.TryFindResource("TextTertiary") as Brush ?? Brushes.Gray;

    private string _name = "";
    private string _value = "";
    private IReadOnlyList<string>? _visibleTags;

    public string OriginalName { get; set; } = "";

    public bool IsPreset { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            _visibleTags = null;

            Raise(nameof(Name));
            Raise(nameof(VisibleTags));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            Raise(nameof(Value));
        }
    }

    public IReadOnlyList<string> VisibleTags => _visibleTags ??= FastFlagTags.VisibleTags(_name);

    /// Roblox refused this exact flag on the last launch, so it is saved here but does
    /// nothing. Shown in the row rather than only in the banner, because the banner names
    /// at most six.
    public bool WasRefused => FlagAudit.WasRefused(_name);

    public Visibility RefusedVisibility => WasRefused ? Visibility.Visible : Visibility.Collapsed;

    public string PresetGlyph => IsPreset ? "✓" : "✕";

    public Brush PresetBrush => IsPreset ? PresetYes : PresetNo;

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class HistoryRow
{
    public required FastFlagSnapshot Snapshot { get; init; }

    public required string Line { get; init; }

    public required string Detail { get; init; }

    public string Count => $"{Snapshot.Flags.Count} flag(s)";

    public string Taken => Snapshot.Taken;
}

public partial class SettingsWindow
{
    private static readonly HashSet<string> PresetFlagNames = new(
        FastFlagCatalogue.Presets.SelectMany(preset => preset.Flags).Select(flag => flag.Flag),
        StringComparer.Ordinal);

    private readonly List<FlagItem> _flagItems = new();

    private string _flagSearch = "";

    private bool _showPresetFlags = true;

    private bool _editingFlagCell;

    private List<HistoryRow> _historyRows = new();

    private void RefreshFastFlagViews()
    {
        Guard(nameof(LoadLiveFlagState), LoadLiveFlagState);
        Guard(nameof(RefreshPresets), RefreshPresets);
        Guard(nameof(RefreshFlagGrid), RefreshFlagGrid);
        Guard(nameof(RefreshHistory), RefreshHistory);
    }

    private void LoadLiveFlagState()
    {
        LiveFlagToggle.IsChecked = Settings.LiveFlagInjection;
        RefreshLiveFlagStatus();
    }

    private async void CheckLiveFlags_Click(object sender, RoutedEventArgs e)
    {
        var state = Installer.ReadState();

        if (state is null)
        {
            SetStatus("Roblox is not installed yet.");
            return;
        }

        CheckLiveFlagsButton.IsEnabled = false;

        try
        {
            string summary = await LiveFlags.PreviewAsync(
                state.VersionGuid, Settings.FastFlags, AppState.Http, CancellationToken.None);

            LiveFlagStatus.Text = summary;
            SetStatus(summary);
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::CheckLiveFlags", ex);
            SetStatus($"Could not check the flags: {ex.Message}");
        }
        finally
        {
            CheckLiveFlagsButton.IsEnabled = true;
        }
    }

    private async void ApplyLiveFlags_Click(object sender, RoutedEventArgs e)
    {
        var state = Installer.ReadState();

        if (state is null)
        {
            SetStatus("Roblox is not installed yet.");
            return;
        }

        int processId = AppState.RobloxProcessId != 0
            ? AppState.RobloxProcessId
            : LiveFlags.FindRunningClient(state.VersionGuid);

        if (processId == 0)
        {
            SetStatus("Roblox is not running, so there is nothing to write into.");
            return;
        }

        ApplyLiveFlagsButton.IsEnabled = false;

        try
        {
            var result = await LiveFlags.ApplyAsync(
                processId,
                state.VersionGuid,
                Settings.FastFlags,
                AppState.Http,
                CancellationToken.None);

            LiveFlagStatus.Text = result.Summary;
            SetStatus(result.Summary);
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ApplyLiveFlags", ex);
            SetStatus($"Live flags failed: {ex.Message}");
        }
        finally
        {
            ApplyLiveFlagsButton.IsEnabled = true;
        }
    }

    private void RefreshLiveFlagStatus()
    {
        ApplyLiveFlagsButton.Visibility = Settings.LiveFlagInjection ? Visibility.Visible : Visibility.Collapsed;
        CheckLiveFlagsButton.Visibility = ApplyLiveFlagsButton.Visibility;

        if (LiveFlags.Last is { } last && LiveFlags.LastRunUtc is { } when)
        {
            LiveFlagStatus.Text = $"{last.Summary} ({when.ToLocalTime():HH:mm:ss})";
            return;
        }

        if (!Settings.LiveFlagInjection)
        {
            LiveFlagStatus.Text = "Off. Flags are applied through ClientAppSettings.json only, which is the supported route.";
            return;
        }

        var state = Installer.ReadState();

        LiveFlagStatus.Text = state is null
            ? "On. Offsets are fetched for whichever build you launch, and the passes are skipped if none match it."
            : $"On. Each launch runs up to six passes over the first 30 seconds, because the client sets its own flags while it starts. Offsets must match {state.VersionGuid} exactly or nothing is written.";
    }

    private void LiveFlags_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = LiveFlagToggle.IsChecked == true;

        if (wanted)
        {
            string newline = Environment.NewLine;

            var answer = MessageBox.Show(
                "Turn on live flag writing?" + newline + newline +
                "This writes values into Roblox while it is running, using addresses from a community offset dump." + newline + newline +
                "It can crash Roblox instantly, mid game." + newline +
                "Roblox's anti-cheat watches its own memory, and writes into it may be treated as tampering. That risk is yours." + newline +
                "It is skipped unless the dump matches your exact build, only booleans and integers are written, and it stops at the first three refused writes." + newline + newline +
                "Your flags already apply through the normal file route without this.",
                "Jello Client",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                LiveFlagToggle.IsChecked = false;
                return;
            }
        }

        Settings.LiveFlagInjection = wanted;
        Persist();

        Log.Write("SettingsWindow::LiveFlags", $"Live flag writing {(wanted ? "enabled" : "disabled")}");

        RefreshLiveFlagStatus();

        SetStatus(wanted
            ? "Live flags on. They are applied after the client starts, and only when the offsets match your build."
            : "Live flags off.");
    }

    private void FlagView_Click(object sender, RoutedEventArgs e)
    {
        bool presets = ReferenceEquals(sender, PresetsTabButton);

        EditorTabButton.IsChecked = !presets;
        PresetsTabButton.IsChecked = presets;

        EditorView.Visibility = presets ? Visibility.Collapsed : Visibility.Visible;
        PresetsView.Visibility = presets ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- presets ----------

    private bool PresetApplied(FlagPreset preset) =>
        preset.Flags.All(value =>
            FastFlagPresets.GetValue(Settings.FastFlags, value.Flag) == value.Value);

    private void TogglePreset(FlagPreset preset, bool on)
    {
        FastFlagHistory.Record(
            on ? $"Enabled the preset '{preset.Name}'" : $"Disabled the preset '{preset.Name}'",
            Settings.FastFlags);

        foreach (var value in preset.Flags)
        {
            FastFlagPresets.SetValue(Settings.FastFlags, value.Flag, on ? value.Value : null);
        }

        Persist();

        Log.Write("SettingsWindow::TogglePreset",
            $"{(on ? "Enabled" : "Disabled")} '{preset.Name}' ({preset.Flags.Count} flag(s))");

        RefreshFastFlagsEditor();
        Guard(nameof(RefreshFlagGrid), RefreshFlagGrid);
        Guard(nameof(RefreshHistory), RefreshHistory);

        SetStatus($"{(on ? "Enabled" : "Disabled")} {preset.Name}. Applies on the next launch.");
    }

    private void RefreshPresets()
    {
        var groups = FastFlagCatalogue.Presets
            .GroupBy(preset => preset.Group)
            .Select(group => new PresetGroup
            {
                Name = group.Key,
                Description = FastFlagCatalogue.GroupDescriptions.TryGetValue(group.Key, out string? d) ? d : "",
                Items = group.Select(preset => new PresetRow
                {
                    Preset = preset,
                    IsOn = PresetApplied,
                    Toggle = TogglePreset
                }).ToList()
            })
            .ToList();

        PresetGroups.ItemsSource = null;
        PresetGroups.ItemsSource = groups;
    }

    // ---------- the editor grid ----------

    private void RefreshFlagGrid()
    {
        if (_editingFlagCell)
        {
            return;
        }

        _flagItems.Clear();

        foreach (var pair in Settings.FastFlags.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            string value = pair.Value.ValueKind == JsonValueKind.String
                ? pair.Value.GetString() ?? ""
                : pair.Value.ToString();

            bool preset = PresetFlagNames.Contains(pair.Key);

            if (!_showPresetFlags && preset)
            {
                continue;
            }

            if (_flagSearch.Length > 0
                && pair.Key.IndexOf(_flagSearch, StringComparison.OrdinalIgnoreCase) < 0
                && value.IndexOf(_flagSearch, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            _flagItems.Add(new FlagItem
            {
                Name = pair.Key,
                OriginalName = pair.Key,
                Value = value,
                IsPreset = preset
            });
        }

        FlagGrid.ItemsSource = null;
        FlagGrid.ItemsSource = _flagItems;

        UpdateFlagCounters();
        UpdateEmptyState();
        UpdateFlagProblems();
    }

    private List<FlagProblem> _flagProblems = new();

    /// Malformed entries are worth catching before a launch: Roblox drops a flag whose
    /// prefix it does not know, and a boolean holding "1" is not the same as "True".
    private void UpdateFlagProblems()
    {
        _flagProblems = FastFlagWriter.Inspect(Settings.FastFlags);

        int fixable = _flagProblems.Count(problem => problem.Suggestion is not null);

        FixFlagsButton.Visibility = fixable == 0 ? Visibility.Collapsed : Visibility.Visible;
        FixFlagsButton.Content = fixable == 1 ? "Fix 1 problem" : $"Fix {fixable} problems";

        if (_flagProblems.Count == 0)
        {
            FlagStatusText.Text = "";
            return;
        }

        var first = _flagProblems[0];

        FlagStatusText.Text = _flagProblems.Count == 1
            ? $"{first.Kind}: {first.Flag} - {first.Detail}"
            : $"{_flagProblems.Count} flags look wrong. {first.Kind}: {first.Flag} - {first.Detail}";
    }

    private void FixFlags_Click(object sender, RoutedEventArgs e)
    {
        var fixable = _flagProblems.Where(problem => problem.Suggestion is not null).ToList();

        if (fixable.Count == 0)
        {
            return;
        }

        string newline = Environment.NewLine;

        string summary = string.Join(newline, fixable.Take(8).Select(problem =>
            problem.Kind is "Bogus prefix" or "Whitespace"
                ? $"  {problem.Flag}  ->  {problem.Suggestion}"
                : $"  {problem.Flag} = {problem.Suggestion}"));

        var answer = MessageBox.Show(
            $"Fix {fixable.Count} flag(s)?{newline}{newline}{summary}" +
            (fixable.Count > 8 ? $"{newline}  and {fixable.Count - 8} more" : "") +
            $"{newline}{newline}Your current flags are saved to history first.",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        FastFlagHistory.Record($"Fixed {fixable.Count} malformed flag(s)", Settings.FastFlags);

        foreach (var problem in fixable)
        {
            string? value = FastFlagPresets.GetValue(Settings.FastFlags, problem.Flag);

            if (problem.Kind is "Bogus prefix" or "Whitespace")
            {
                FastFlagPresets.SetValue(Settings.FastFlags, problem.Flag, null);
                FastFlagPresets.SetValue(Settings.FastFlags, problem.Suggestion!, value);
            }
            else
            {
                FastFlagPresets.SetValue(Settings.FastFlags, problem.Flag, problem.Suggestion);
            }
        }

        Persist();

        Log.Write("SettingsWindow::FixFlags", $"Repaired {fixable.Count} malformed flag(s)");

        AfterFlagChange($"Fixed {fixable.Count} flag(s).");
    }

    private void UpdateFlagCounters()
    {
        int total = Settings.FastFlags.Count;

        TotalFlagsText.Text = $"Flags added: {total}";

        double bloat = Math.Min(total * 0.2, 100.0);

        BloatText.Text = $"Bloat: {(bloat % 1.0 == 0.0 ? bloat.ToString("0") : bloat.ToString("0.##"))}%";
    }

    private void UpdateEmptyState()
    {
        if (_flagItems.Count > 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyStateText.Text = Settings.FastFlags.Count == 0
            ? "No flags yet. Turn on a preset, or use Add new."
            : _flagSearch.Length > 0
                ? "No flags match your search."
                : !_showPresetFlags
                    ? "Every flag you have came from a preset. Turn on Show preset flags to see them."
                    : "No flags to show.";

        EmptyStatePanel.Visibility = Visibility.Visible;
    }

    private void FlagSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _flagSearch = FlagSearchBox.Text.Trim();
        Guard(nameof(RefreshFlagGrid), RefreshFlagGrid);
    }

    private void ShowPresetFlags_Click(object sender, RoutedEventArgs e)
    {
        _showPresetFlags = ShowPresetFlagsButton.IsChecked == true;
        Guard(nameof(RefreshFlagGrid), RefreshFlagGrid);
    }


    private void FlagGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || e.Row.Item is not FlagItem item
            || e.EditingElement is not TextBox box)
        {
            return;
        }

        string edited = box.Text.Trim();
        bool isName = (string)e.Column.Header == "Name";

        _editingFlagCell = true;

        Dispatcher.BeginInvoke(() =>
        {
            _editingFlagCell = false;

            if (isName)
            {
                RenameFlag(item, edited);
            }
            else
            {
                ChangeFlagValue(item, edited);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RenameFlag(FlagItem item, string name)
    {
        if (name.Length == 0 || name == item.OriginalName)
        {
            item.Name = item.OriginalName;
            return;
        }

        FastFlagHistory.Record($"Renamed '{item.OriginalName}' to '{name}'", Settings.FastFlags);

        FastFlagPresets.SetValue(Settings.FastFlags, item.OriginalName, null);
        FastFlagPresets.SetValue(Settings.FastFlags, name, item.Value);
        Persist();

        Log.Write("SettingsWindow::RenameFlag", $"{item.OriginalName} renamed to {name}");

        AfterFlagChange($"Renamed to {name}.");
    }

    private void ChangeFlagValue(FlagItem item, string value)
    {
        string? before = FastFlagPresets.GetValue(Settings.FastFlags, item.OriginalName);

        if (before == value)
        {
            return;
        }

        FastFlagHistory.Record($"Changed '{item.OriginalName}' from '{before}' to '{value}'", Settings.FastFlags);

        FastFlagPresets.SetValue(Settings.FastFlags, item.OriginalName, value);
        Persist();

        Log.Write("SettingsWindow::ChangeFlagValue", $"{item.OriginalName} set to {value}");

        AfterFlagChange($"{item.OriginalName} set to {value}.");
    }

    private void AfterFlagChange(string status)
    {
        RefreshFastFlagsEditor();
        Guard(nameof(RefreshFlagGrid), RefreshFlagGrid);
        Guard(nameof(RefreshHistory), RefreshHistory);

        FlagStatusText.Text = status;
        UpdateFlagProblems();

        // With live flags on, a change to a running client is the whole point of the
        // feature, so it is pushed straight away instead of waiting for a relaunch.
        if (Settings.LiveFlagInjection && PushLiveFlags())
        {
            SetStatus($"{status} Pushed to the running client.");
            return;
        }

        SetStatus($"{status} Applies on the next launch.");
    }

    private bool PushLiveFlags()
    {
        var state = Installer.ReadState();

        if (state is null)
        {
            return false;
        }

        int processId = AppState.RobloxProcessId != 0
            ? AppState.RobloxProcessId
            : LiveFlags.FindRunningClient(state.VersionGuid);

        if (processId == 0)
        {
            return false;
        }

        _ = PushLiveFlagsAsync(processId, state.VersionGuid);

        return true;
    }

    private async Task PushLiveFlagsAsync(int processId, string versionGuid)
    {
        try
        {
            var result = await LiveFlags.ApplyAsync(
                processId, versionGuid, Settings.FastFlags, AppState.Http, CancellationToken.None);

            Dispatcher.Invoke(() =>
            {
                LiveFlagStatus.Text = result.Summary;
                SetStatus(result.Summary);
            });
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::PushLiveFlags", ex);
        }
    }

    private void AddFlag_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddFlagDialog { Owner = this };

        if (dialog.ShowDialog() != true || dialog.Flags.Count == 0)
        {
            return;
        }

        FastFlagHistory.Record(
            dialog.Flags.Count == 1
                ? $"Added '{dialog.Flags.First().Key}'"
                : $"Added {dialog.Flags.Count} flags",
            Settings.FastFlags);

        foreach (var pair in dialog.Flags)
        {
            FastFlagPresets.SetValue(Settings.FastFlags, pair.Key, pair.Value);
        }

        Persist();

        Log.Write("SettingsWindow::AddFlag", $"Added {dialog.Flags.Count} flag(s)");

        AfterFlagChange($"Added {dialog.Flags.Count} flag(s).");
    }

    private void DeleteSelectedFlags_Click(object sender, RoutedEventArgs e)
    {
        var selected = FlagGrid.SelectedItems.OfType<FlagItem>().ToList();

        if (selected.Count == 0)
        {
            return;
        }

        FastFlagHistory.Record(
            selected.Count == 1 ? $"Deleted '{selected[0].OriginalName}'" : $"Deleted {selected.Count} flags",
            Settings.FastFlags);

        foreach (var item in selected)
        {
            FastFlagPresets.SetValue(Settings.FastFlags, item.OriginalName, null);
        }

        Persist();

        Log.Write("SettingsWindow::DeleteSelectedFlags", $"Deleted {selected.Count} flag(s)");

        AfterFlagChange($"Deleted {selected.Count} flag(s).");
    }

    private void DeleteAllFlags_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.FastFlags.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Remove all {Settings.FastFlags.Count} flag(s), including the ones the presets set?",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        FastFlagHistory.Record($"Deleted all flags ({Settings.FastFlags.Count})", Settings.FastFlags);

        Settings.FastFlags.Clear();
        Persist();

        Log.Write("SettingsWindow::DeleteAllFlags", "Cleared every flag");

        RefreshPresets();
        AfterFlagChange("Cleared every flag.");
    }

    private void ExportFlags_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export your FastFlags",
            Filter = "JSON files (*.json)|*.json",
            FileName = "ClientAppSettings.json"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(picker.FileName, FlagJson());

            FlagStatusText.Text = $"Exported {Settings.FastFlags.Count} flag(s).";
            SetStatus($"Exported {Settings.FastFlags.Count} flag(s) to {Path.GetFileName(picker.FileName)}.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ExportFlags", ex);
            SetStatus($"Could not export the flags: {ex.Message}");
        }
    }

    private void CopyFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(FlagJson());

            FlagStatusText.Text = $"Copied {Settings.FastFlags.Count} flag(s) to the clipboard.";
            SetStatus($"Copied {Settings.FastFlags.Count} flag(s) to the clipboard.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::CopyFlags", ex);
            SetStatus($"Could not copy the flags: {ex.Message}");
        }
    }

    private void ImportFlags_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a flag file",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(picker.FileName));

            if (parsed is null || parsed.Count == 0)
            {
                SetStatus("That file has no flags in it.");
                return;
            }

            FastFlagHistory.Record($"Imported {Path.GetFileName(picker.FileName)}", Settings.FastFlags);

            foreach (var pair in parsed)
            {
                string value = pair.Value.ValueKind == JsonValueKind.String
                    ? pair.Value.GetString() ?? ""
                    : pair.Value.ToString();

                FastFlagPresets.SetValue(Settings.FastFlags, pair.Key, value);
            }

            Persist();

            Log.Write("SettingsWindow::ImportFlags", $"Imported {parsed.Count} flag(s) from {picker.FileName}");

            RefreshPresets();
            AfterFlagChange($"Imported {parsed.Count} flag(s).");
        }
        catch (JsonException ex)
        {
            SetStatus($"That file is not valid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ImportFlags", ex);
            SetStatus($"Could not import that file: {ex.Message}");
        }
    }

    private string FlagJson() =>
        JsonSerializer.Serialize(Settings.FastFlags, new JsonSerializerOptions { WriteIndented = true });

    // ---------- history ----------

    private void RefreshHistory()
    {
        var snapshots = FastFlagHistory.Load();
        var rows = new List<HistoryRow>();
        var current = FastFlagHistory.Flatten(Settings.FastFlags);

        for (int index = 0; index < snapshots.Count; index++)
        {
            var after = index == 0 ? current : snapshots[index - 1].Flags;
            var changes = FastFlagHistory.Diff(snapshots[index].Flags, after);

            string time = snapshots[index].Taken;

            string line = changes.Count switch
            {
                0 => $"{time}   {snapshots[index].Reason}",
                1 => $"{time}   {Describe(changes[0])}",
                _ => $"{time}   {snapshots[index].Reason} ({changes.Count} changes)"
            };

            string detail = changes.Count == 0
                ? $"Restores {snapshots[index].Flags.Count} flag(s)"
                : string.Join("   ", changes.Take(3).Select(change => change.Summary))
                  + (changes.Count > 3 ? $"   and {changes.Count - 3} more" : "");

            rows.Add(new HistoryRow { Snapshot = snapshots[index], Line = line, Detail = detail });
        }

        _historyRows = rows;

        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = rows;

        HistoryCountText.Text = rows.Count switch
        {
            0 => "No changes recorded",
            1 => "1 entry recorded",
            _ => $"{rows.Count} entries recorded"
        };
    }

    private static string Describe(FlagChange change) => change.Kind switch
    {
        "added" => $"Added '{change.Flag}' with value '{change.After}'",
        "removed" => $"Deleted '{change.Flag}', was '{change.Before}'",
        _ => $"Changed '{change.Flag}' from '{change.Before}' to '{change.After}'"
    };

    private void UndoHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow row)
        {
            SetStatus("Select an entry in the history first.");
            return;
        }

        RestoreSnapshot(row);
    }

    private void HistoryList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow row)
        {
            RestoreSnapshot(row);
        }
    }

    private void RestoreSnapshot(HistoryRow row)
    {
        var answer = MessageBox.Show(
            $"Go back to the {row.Count} from {row.Taken}?\n\n{row.Line}\n\nYour current flags are saved to history first, so this can be undone.",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        FastFlagHistory.Record($"Reverted to {row.Taken}", Settings.FastFlags);

        Settings.FastFlags.Clear();

        foreach (var pair in row.Snapshot.Flags)
        {
            FastFlagPresets.SetValue(Settings.FastFlags, pair.Key, pair.Value);
        }

        Persist();

        Log.Write("SettingsWindow::RestoreSnapshot", $"Reverted to {row.Taken} ({row.Snapshot.Flags.Count} flag(s))");

        RefreshPresets();
        AfterFlagChange($"Reverted to the flag set from {row.Taken}.");
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Delete the whole FastFlag history? Your current flags are not affected.",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        FastFlagHistory.Clear();
        RefreshHistory();

        SetStatus("FastFlag history cleared.");
    }

    private void ReloadFastFlags_Click(object sender, RoutedEventArgs e)
    {
        RefreshFastFlagsEditor();
        FastFlagsStatus.Text = $"Reloaded {Settings.FastFlags.Count} flag(s) from your settings.";
    }
}
