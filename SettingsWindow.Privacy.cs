using System.Windows;
using System.Windows.Controls;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private void LoadPrivacyState()
    {
        TelemetryToggle.IsChecked = TelemetryBlock.IsApplied();

        // The hosts file can change without Jello, so what is on disk wins over what was
        // saved. A stale setting would otherwise claim a block that is not there.
        Settings.BlockTelemetry = TelemetryToggle.IsChecked == true;

        RefreshPrivacyState();
    }

    private void RefreshPrivacyState() => TelemetryText.Text = TelemetryBlock.Describe();

    private void Telemetry_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = TelemetryToggle.IsChecked == true;

        SetStatus(wanted
            ? "Asking Windows for permission to edit the hosts file..."
            : "Asking Windows for permission to undo the hosts block...");

        bool done = TelemetryBlock.Request(wanted);

        _suppressEvents = true;
        TelemetryToggle.IsChecked = TelemetryBlock.IsApplied();
        _suppressEvents = false;

        Settings.BlockTelemetry = TelemetryToggle.IsChecked == true;
        Persist();

        RefreshPrivacyState();

        if (!done)
        {
            SetStatus("Nothing changed. The hosts file needs administrator rights.");
            return;
        }

        SetStatus(wanted
            ? $"{TelemetryBlock.Hosts.Count} telemetry host(s) blocked for this computer."
            : "Jello's telemetry block removed.");
    }

    private void ShowTelemetryHosts_Click(object sender, RoutedEventArgs e)
    {
        int blocked = TelemetryBlock.BlockedCount();

        string body = string.Join(Environment.NewLine, TelemetryBlock.Hosts);

        MessageBox.Show(
            $"These names are pointed at 0.0.0.0 in {TelemetryBlock.HostsPath}:"
            + Environment.NewLine + Environment.NewLine
            + body
            + Environment.NewLine + Environment.NewLine
            + $"{blocked} of {TelemetryBlock.Hosts.Count} are blocked now. "
            + "Every line Jello writes is marked, so turning this off removes exactly those "
            + "and leaves anything else in the file alone.",
            "Telemetry hosts",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ---- resource limits, on the Optimizations tab -------------------------------------

    private void BuildResourceLists()
    {
        CoreLimitCombo.Items.Add(new ComboBoxItem { Content = "All cores" });

        for (int cores = 1; cores <= ResourceLimits.CoreCount; cores++)
        {
            CoreLimitCombo.Items.Add(new ComboBoxItem
            {
                Content = cores == 1 ? "1 core" : $"{cores} cores"
            });
        }

        foreach (var priority in Enum.GetValues<ProcessPriority>())
        {
            PriorityCombo.Items.Add(new ComboBoxItem { Content = ResourceLimits.Describe(priority) });
        }

        foreach (int level in DuckLevels)
        {
            DuckLevelCombo.Items.Add(new ComboBoxItem { Content = level == 0 ? "Mute" : $"{level}%" });
        }
    }

    private static readonly int[] DuckLevels = { 0, 10, 30, 50 };

    private void LoadResourceState()
    {
        CoreLimitCombo.SelectedIndex = Math.Clamp(Settings.CoreLimit, 0, ResourceLimits.CoreCount);
        PriorityCombo.SelectedIndex = (int)Settings.RobloxPriority;

        RefreshResourceState();
    }

    private void RefreshResourceState() => ResourceText.Text = ResourceLimits.Describe();

    private void LoadDuckAudioState()
    {
        DuckAudioToggle.IsChecked = Settings.DuckAudioUnfocused;

        int level = Array.IndexOf(DuckLevels, (int)Math.Round(Settings.DuckAudioLevel * 100));
        DuckLevelCombo.SelectedIndex = level < 0 ? 2 : level;

        DuckAudioText.Text = AudioDuck.Describe();
    }

    private void DuckAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.DuckAudioUnfocused = DuckAudioToggle.IsChecked == true;
        Persist();

        AudioDuck.Refresh();
        DuckAudioText.Text = AudioDuck.Describe();

        SetStatus(Settings.DuckAudioUnfocused
            ? "Roblox will quieten while it is not the window in front."
            : "Roblox stays at full volume.");
    }

    private void DuckLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || DuckLevelCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.DuckAudioLevel = DuckLevels[DuckLevelCombo.SelectedIndex] / 100.0;
        Persist();

        DuckAudioText.Text = AudioDuck.Describe();
    }

    private void CoreLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || CoreLimitCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.CoreLimit = CoreLimitCombo.SelectedIndex;
        Persist();

        ApplyResourcesNow();
    }

    private void Priority_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || PriorityCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.RobloxPriority = (ProcessPriority)PriorityCombo.SelectedIndex;
        Persist();

        ApplyResourcesNow();
    }

    /// Takes effect straight away when Roblox is open, rather than only on the next launch.
    private void ApplyResourcesNow()
    {
        if (AppState.RobloxProcessId != 0)
        {
            ResourceLimits.Apply(AppState.RobloxProcessId);
        }

        RefreshResourceState();

        SetStatus(AppState.RobloxProcessId != 0
            ? ResourceLimits.Status
            : "Saved. It applies the next time Roblox starts.");
    }

    // ---- the Integrations sub-tabs -----------------------------------------------------

    /// The tab held three unrelated things in one scroll: what Jello watches, what it tells
    /// Discord, and what it launches beside Roblox. They are the same three chips the
    /// FastFlags tab uses.
    private void IntegrationView_Click(object sender, RoutedEventArgs e)
    {
        ShowIntegrationView(
            ReferenceEquals(sender, DiscordTabButton) ? 1
            : ReferenceEquals(sender, ProgramsTabButton) ? 2
            : 0);
    }

    private void ShowIntegrationView(int which)
    {
        ActivityTabButton.IsChecked = which == 0;
        DiscordTabButton.IsChecked = which == 1;
        ProgramsTabButton.IsChecked = which == 2;

        ActivityView.Visibility = which == 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscordView.Visibility = which == 1 ? Visibility.Visible : Visibility.Collapsed;
        ProgramsView.Visibility = which == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ServerFill_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.ShowServerFillOnDiscord = ServerFillToggle.IsChecked == true;
        Persist();

        SetStatus(Settings.ShowServerFillOnDiscord
            ? "Discord will show how full the server is. It updates on the next server you join."
            : "Discord will not show the player count.");
    }

    // ---- the Roblox app's own light/dark theme -----------------------------------------

    private void BuildRobloxThemeList()
    {
        foreach (var theme in Enum.GetValues<RobloxAppTheme>())
        {
            RobloxThemeCombo.Items.Add(new ComboBoxItem { Content = RobloxTheme.Label(theme) });
        }
    }

    private void LoadRobloxThemeState()
    {
        RobloxThemeCombo.SelectedIndex = (int)Settings.RobloxAppTheme;
        RobloxThemeText.Text = RobloxTheme.Describe();
    }

    private void RobloxTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || RobloxThemeCombo.SelectedIndex < 0)
        {
            return;
        }

        var wanted = (RobloxAppTheme)RobloxThemeCombo.SelectedIndex;

        Settings.RobloxAppTheme = wanted;
        Persist();

        if (wanted == RobloxAppTheme.LeaveAlone)
        {
            RobloxThemeText.Text = RobloxTheme.Describe();
            SetStatus("Windows is left as it is.");
            return;
        }

        bool done = RobloxTheme.Apply(wanted);

        RobloxThemeText.Text = RobloxTheme.Describe();

        SetStatus(done
            ? $"Roblox is now {RobloxTheme.Label(wanted).ToLowerInvariant()}. So is the rest of Windows."
            : "Windows would not let that setting be changed.");
    }

    // ---- the game icon, on the Integrations tab ----------------------------------------

    private void LoadGameIconState()
    {
        ServerFillToggle.IsChecked = Settings.ShowServerFillOnDiscord;
        GameIconToggle.IsChecked = Settings.GameIconOnWindow;
        ClearCustomIconButton.Visibility = string.IsNullOrEmpty(Settings.GameIconCustomPath)
            ? Visibility.Collapsed
            : Visibility.Visible;
        GameIconText.Text = WindowIcon.Describe();
    }

    private void GameIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.GameIconOnWindow = GameIconToggle.IsChecked == true;
        Persist();

        if (!Settings.GameIconOnWindow)
        {
            WindowIcon.Restore();
        }
        else if (AppState.ActivityWatcher is { InGame: true, Data.UniverseId: var universe and not 0 })
        {
            _ = WindowIcon.ShowGameAsync(universe);
        }

        GameIconText.Text = WindowIcon.Describe();

        SetStatus(Settings.GameIconOnWindow
            ? "The taskbar will show the icon of whatever you are playing."
            : "The taskbar will show the usual Roblox icon.");
    }

    private void ChooseCustomIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a custom icon for the Roblox window",
            Filter = "Images (*.png;*.ico;*.jpg;*.jpeg;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.bmp"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        Settings.GameIconCustomPath = picker.FileName;

        if (!Settings.GameIconOnWindow)
        {
            Settings.GameIconOnWindow = true;
            GameIconToggle.IsChecked = true;
        }

        Persist();

        WindowIcon.Forget();

        if (AppState.ActivityWatcher is { InGame: true, Data.UniverseId: var universe and not 0 })
        {
            _ = WindowIcon.ShowGameAsync(universe);
        }

        LoadGameIconState();
        SetStatus($"{Path.GetFileName(picker.FileName)} will be the Roblox window icon in game.");
    }

    private void ClearCustomIcon_Click(object sender, RoutedEventArgs e)
    {
        Settings.GameIconCustomPath = null;
        Persist();

        WindowIcon.Forget();

        if (Settings.GameIconOnWindow
            && AppState.ActivityWatcher is { InGame: true, Data.UniverseId: var universe and not 0 })
        {
            _ = WindowIcon.ShowGameAsync(universe);
        }

        LoadGameIconState();
        SetStatus("Back to each game's own icon.");
    }
}
