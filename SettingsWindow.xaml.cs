using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JelloClient.Interop;
using JelloClient.Roblox;
using JelloClient.Services;
using JelloClient.UI;

namespace JelloClient;

public partial class SettingsWindow : JelloWindow
{

    private readonly Updater _updater = new(AppState.Http);

    private CancellationTokenSource? _installCancellation;
    private bool _suppressEvents = true;

    public SettingsWindow()
    {
        InitializeComponent();

        InitialiseFeatureLists();

        Closed += (_, _) => AppState.Persist();
    }

    protected override FrameworkElement EffectsRoot => Root;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Guard(nameof(HookFastFlagEditor), HookFastFlagEditor);
        Guard(nameof(HookWheelForwarding), HookWheelForwarding);
        Guard(nameof(LoadSettingsIntoUi), LoadSettingsIntoUi);
        Guard(nameof(UpdateMaximizeIcon), UpdateMaximizeIcon);
        Guard(nameof(RefreshInstallState), RefreshInstallState);
        Guard(nameof(DescribePaths), DescribePaths);
        Guard(nameof(RefreshFontState), RefreshFontState);
        Guard(nameof(RefreshChannelHint), RefreshChannelHint);
        Guard(nameof(RefreshModState), RefreshModState);
        Guard(nameof(RefreshFastFlagViews), RefreshFastFlagViews);
        Guard("SetStatus", () => SetStatus(null));
        Guard(nameof(RestorePendingTab), RestorePendingTab);

        StateChanged += (_, _) => UpdateMaximizeIcon();
    }

    private void RestorePendingTab()
    {
        if (App.PendingSettingsTab is not { } header)
        {
            return;
        }

        App.PendingSettingsTab = null;

        foreach (var item in NavRailTabs.Items.OfType<System.Windows.Controls.TabItem>())
        {
            if (Equals(item.Header, header))
            {
                NavRailTabs.SelectedItem = item;
                return;
            }
        }
    }

    private void LoadSettingsIntoUi()
    {
        _suppressEvents = true;

        AcrylicToggle.IsChecked = Settings.AcrylicEnabled;
        SecureToggle.IsChecked = Settings.SecureUi;
        TopmostToggle.IsChecked = Settings.AlwaysOnTop;
        ConfirmLaunchesToggle.IsChecked = Settings.ConfirmLaunches;
        CloseToTrayToggle.IsChecked = Settings.CloseToTray;
        HideOnLaunchToggle.IsChecked = Settings.HideLauncherOnLaunch;
        ActivityTrackingToggle.IsChecked = Settings.EnableActivityTracking;
        ServerDetailsToggle.IsChecked = Settings.ShowServerDetails;
        NotifyJoinToggle.IsChecked = Settings.NotifyOnServerJoin;
        RichPresenceToggle.IsChecked = Settings.UseDiscordRichPresence;
        HideRpcButtonsToggle.IsChecked = Settings.HideRichPresenceButtons;
        ChannelBox.Text = Settings.Channel;
        ProtocolToggle.IsChecked = ProtocolHandler.IsPlayerRegistered();
        StartupToggle.IsChecked = Startup.IsEnabled();

        if (Settings.MultiInstance && !Launcher.HoldMultiInstanceMutex())
        {
            Log.Write("SettingsWindow::LoadSettings", "Multi-instance was on but the mutex is held elsewhere");
            Settings.MultiInstance = false;
        }

        MultiInstanceToggle.IsChecked = Settings.MultiInstance;


        RefreshFastFlagsEditor();
        LoadFeatureState();

        _suppressEvents = false;
    }

    private void HookFastFlagEditor()
    {
        FastFlagsBox.TextChanged += (_, _) =>
        {
            string error = FastFlagsBox.ErrorText;

            if (!string.IsNullOrEmpty(error))
            {
                FastFlagsStatus.Text = error;
            }
            else if (FastFlagsStatus.Text.StartsWith("Line ", StringComparison.Ordinal))
            {
                FastFlagsStatus.Text = "";
            }
        };
    }

    private void RefreshFastFlagsEditor()
    {
        FastFlagsBox.Text = Settings.FastFlags.Count == 0
            ? "{\n}"
            : JsonSerializer.Serialize(Settings.FastFlags, new JsonSerializerOptions { WriteIndented = true });
    }

    private void DescribePaths()
    {
        Paths.EnsureCreated();

        ModsPathText.Text = Paths.Modifications;
        BasePathText.Text = Paths.Base;
        LogPathText.Text = Log.FilePath ?? "This session is not being logged to disk.";
        AppVersionText.Text = $"Version {Updater.CurrentVersion.ToString(3)}";
        HeaderVersionText.Text = $"v{Updater.CurrentVersion.ToString(3)}";
    }

    private void RefreshInstallState()
    {
        var state = Installer.ReadState();

        if (state is null || Installer.FindInstalledExecutable(state.VersionGuid) is null)
        {
            InstalledVersionText.Text = "Roblox is not installed yet";
            InstalledChannelText.Text = "Launching will download the latest version first.";
            return;
        }

        InstalledVersionText.Text = $"Roblox {state.Version}";
        InstalledChannelText.Text = $"{state.Channel} channel, {state.VersionGuid}";
    }

    private void RefreshFontState()
    {
        bool installed = CustomFont.IsInstalled;

        CustomFontText.Text = installed
            ? $"Every Roblox font family is redirected to {CustomFont.ModFontPath}."
            : "Replaces every font Roblox draws with a TrueType font of your choosing.";

        ChooseFontButton.Content = installed ? "Replace" : "Choose font";
        RemoveFontButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string? extra)
    {
        string os = WindowEffects.IsWindows11 ? "Windows 11" : "Windows 10";

        StatusText.Text = string.IsNullOrEmpty(extra)
            ? $"{os}, build {WindowEffects.Build}."
            : $"{os}, build {WindowEffects.Build}. {extra}";
    }

    private void Persist() => AppState.Persist();

    private void ApplyEffectsEverywhere()
    {
        ApplyAllEffects();
        App.RefreshWindowEffects();
    }

    private void UpdateMaximizeIcon()
    {
        bool maximized = WindowState == WindowState.Maximized;

        MaximizeButton.Tag = FindResource(maximized ? "IconRestore" : "IconMaximize");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private async Task RunInstallAsync()
    {
        _installCancellation?.Cancel();
        _installCancellation = new CancellationTokenSource();

        ProgressCard.Visibility = Visibility.Visible;
        UpdateRobloxButton.IsEnabled = false;

        var progress = new Progress<InstallProgress>(report =>
        {
            ProgressStageText.Text = report.Stage;
            InstallProgressBar.Value = Math.Clamp(report.Fraction, 0, 1);
        });

        try
        {
            var installer = new Installer(AppState.Http);

            await installer.EnsureLatestAsync(
                Settings.Channel,
                Settings.FastFlagValues,
                progress,
                _installCancellation.Token);

            SetStatus("Roblox is up to date.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Install cancelled.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::RunInstall", ex);
            SetStatus(InstallErrors.Describe(ex));
        }
        finally
        {
            ProgressCard.Visibility = Visibility.Collapsed;
            UpdateRobloxButton.IsEnabled = true;
            Guard(nameof(RefreshInstallState), RefreshInstallState);
        }
    }

    private async void UpdateRoblox_Click(object sender, RoutedEventArgs e) => await RunInstallAsync();

    private void CancelInstall_Click(object sender, RoutedEventArgs e) => _installCancellation?.Cancel();

    private void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Delete every downloaded Roblox version? The next launch will download a fresh copy.",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Installer.ClearInstall();
            RefreshInstallState();
            SetStatus("Deleted the installed Roblox versions.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::Reinstall", ex);
            SetStatus($"Could not delete the installs: {ex.Message}");
        }
    }

    private void ResetChannel_Click(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;
        ChannelBox.Text = Deployment.DefaultChannel;
        _suppressEvents = false;

        Settings.Channel = Deployment.DefaultChannel;
        Persist();

        RefreshChannelHint();

        Log.Write("SettingsWindow::ResetChannel", $"Channel reset to {Deployment.DefaultChannel}");
        SetStatus($"Channel reset to {Deployment.DefaultChannel}.");
    }

    private void ChannelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        string channel = string.IsNullOrWhiteSpace(ChannelBox.Text)
            ? Deployment.DefaultChannel
            : ChannelBox.Text.Trim();

        ChannelBox.Text = channel;
        Settings.Channel = channel;
        Persist();

        RefreshChannelHint();
    }

    private void RefreshChannelHint()
    {
        bool isDefault = string.Equals(Settings.Channel, Deployment.DefaultChannel, StringComparison.OrdinalIgnoreCase);

        ResetChannelButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;

        ChannelHintText.Text = isDefault
            ? "Which Roblox deployment channel to pull from. Leave this on production unless you know you need otherwise."
            : $"Jello is set to the '{Settings.Channel}' channel. Roblox restricts non-production channels, so installs may fail with a permission error. Reset to {Deployment.DefaultChannel} if that happens.";
    }

    private void MultiInstanceToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = MultiInstanceToggle.IsChecked == true;

        if (wanted)
        {
            if (Launcher.HoldMultiInstanceMutex())
            {
                SetStatus("Holding the Roblox singleton mutex.");
            }
            else
            {
                _suppressEvents = true;
                MultiInstanceToggle.IsChecked = false;
                _suppressEvents = false;

                wanted = false;
                SetStatus("Another program already holds the Roblox singleton mutex.");
            }
        }
        else
        {
            Launcher.ReleaseMultiInstanceMutex();
            SetStatus(null);
        }

        Settings.MultiInstance = wanted;
        Persist();
    }

    private void ConfirmLaunchesToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.ConfirmLaunches = ConfirmLaunchesToggle.IsChecked == true;
        Persist();
    }

    private void HideOnLaunchToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.HideLauncherOnLaunch = HideOnLaunchToggle.IsChecked == true;
        Persist();
    }

    private void CloseToTrayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.CloseToTray = CloseToTrayToggle.IsChecked == true;
        Persist();
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = StartupToggle.IsChecked == true;

        Startup.Set(wanted);

        // The registry is the truth; show back what actually took, so a blocked write does
        // not leave the switch claiming something that is not so.
        bool actual = Startup.IsEnabled();

        if (actual != wanted)
        {
            _suppressEvents = true;
            StartupToggle.IsChecked = actual;
            _suppressEvents = false;
        }

        SetStatus(actual
            ? "Jello will start with Windows, in the notification area."
            : "Jello will no longer start with Windows.");
    }

    private void ProtocolToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = ProtocolToggle.IsChecked == true;

        try
        {
            if (wanted)
            {
                ProtocolHandler.RegisterPlayer();
                SetStatus("Roblox launch links now open through Jello.");
            }
            else
            {
                ProtocolHandler.UnregisterPlayer();
                SetStatus("Roblox launch links have been handed back to the stock bootstrapper.");
            }
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ProtocolToggle", ex);

            _suppressEvents = true;
            ProtocolToggle.IsChecked = !wanted;
            _suppressEvents = false;

            SetStatus($"Could not change the launch link handler: {ex.Message}");
        }
    }




    private void SaveFastFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FastFlagsBox.Text);

            Settings.FastFlags = parsed ?? new Dictionary<string, JsonElement>();
            Persist();

            _suppressEvents = true;
                            _suppressEvents = false;

            FastFlagsStatus.Text = $"Saved {Settings.FastFlags.Count} flag(s). They apply on the next launch.";
        }
        catch (JsonException ex)
        {
            FastFlagsStatus.Text = $"Invalid JSON: {ex.Message}";
        }
    }


    private void OpenMods_Click(object sender, RoutedEventArgs e)
    {
        Paths.EnsureCreated();
        OpenInExplorer(Paths.Modifications);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Paths.EnsureCreated();
        OpenInExplorer(Paths.Logs);
    }

    private void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::OpenInExplorer", ex);
            SetStatus($"Could not open {path}: {ex.Message}");
        }
    }

    private void ChooseFont_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a font for Roblox",
            Filter = "TrueType fonts (*.ttf)|*.ttf|OpenType fonts (*.otf)|*.otf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Paths.EnsureCreated();
            CustomFont.Install(dialog.FileName);

            RefreshFontState();
            SetStatus("Custom font installed. It applies on the next launch.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ChooseFont", ex);
            SetStatus($"Could not install the font: {ex.Message}");
        }
    }

    private void RemoveFont_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CustomFont.Remove();

            RefreshFontState();
            SetStatus("Custom font removed. Roblox goes back to its own fonts on the next launch.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::RemoveFont", ex);
            SetStatus($"Could not remove the font: {ex.Message}");
        }
    }

    private void ActivityTrackingToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.EnableActivityTracking = ActivityTrackingToggle.IsChecked == true;
        Persist();

        if (!Settings.EnableActivityTracking)
        {
            AppState.StopActivityTracking();
            SetStatus("Activity tracking is off. It stays off until Roblox is launched again.");
        }
        else
        {
            SetStatus("Activity tracking starts with the next Roblox launch.");
        }
    }

    private void ServerDetailsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.ShowServerDetails = ServerDetailsToggle.IsChecked == true;
        Persist();
    }

    private void NotifyJoinToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.NotifyOnServerJoin = NotifyJoinToggle.IsChecked == true;
        Persist();
    }

    private void RichPresenceToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.UseDiscordRichPresence = RichPresenceToggle.IsChecked == true;
        Persist();

        if (!Settings.UseDiscordRichPresence)
        {
            AppState.RichPresence?.SetVisibility(false);
            SetStatus("Rich presence cleared. It stays off until Roblox is launched again.");
        }
        else
        {
            SetStatus("Rich presence starts with the next Roblox launch.");
        }
    }

    private void HideRpcButtonsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.HideRichPresenceButtons = HideRpcButtonsToggle.IsChecked == true;
        Persist();

        _ = AppState.RichPresence?.UpdateAsync();
    }

    private void AcrylicToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.AcrylicEnabled = AcrylicToggle.IsChecked == true;
        ApplyEffectsEverywhere();
        Persist();
    }

    private void SecureToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.SecureUi = SecureToggle.IsChecked == true;

        var result = ApplySecureDisplay();
        App.RefreshWindowEffects();

        switch (result)
        {
            case SecureDisplayResult.ExcludedFromCapture:
                SetStatus("Secure UI is on: Jello's windows are excluded from capture.");
                break;

            case SecureDisplayResult.MonitorOnly:
                SetStatus("Secure UI is on using the legacy monitor-only mode, which also blocks Magnifier and other accessibility tools.");
                break;

            case SecureDisplayResult.Disabled:
                SetStatus(null);
                break;

            case SecureDisplayResult.Failed:
                Settings.SecureUi = false;
                _suppressEvents = true;
                SecureToggle.IsChecked = false;
                _suppressEvents = false;
                SetStatus($"Secure UI could not be enabled: {WindowEffects.DescribeLastError()}");
                break;
        }

        Persist();
    }

    private void TopmostToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.AlwaysOnTop = TopmostToggle.IsChecked == true;
        ApplyTopmost();
        App.RefreshWindowEffects();
        Persist();
    }

    private async void AppUpdate_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateButton.IsEnabled = false;
        AppUpdateStatusText.Text = "Checking GitHub releases...";

        try
        {
            var check = await _updater.CheckAsync(CancellationToken.None);

            AppUpdateStatusText.Text = check.Message;

            if (check.Outcome != UpdateOutcome.Available || check.Release is null)
            {
                return;
            }

            var answer = MessageBox.Show(
                $"{check.Message}\n\nDownload it and restart Jello now?",
                "Jello Client",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var progress = new Progress<double>(fraction =>
                AppUpdateStatusText.Text = $"Downloading {check.Version?.ToString(3)}... {fraction:P0}");

            string downloaded = await _updater.DownloadAsync(check.Release, progress, CancellationToken.None);

            AppUpdateStatusText.Text = "Restarting into the new build...";

            Updater.ApplyAndRestart(downloaded);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::AppUpdate", ex);
            AppUpdateStatusText.Text = $"Update failed: {ex.Message}";
        }
        finally
        {
            AppUpdateButton.IsEnabled = true;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
