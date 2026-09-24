using System.ComponentModel;
using System.Windows;
using JelloClient.Interop;
using JelloClient.Roblox;
using JelloClient.Services;
using JelloClient.UI;
using JelloClient.UI.Bootstrapper;

namespace JelloClient;

public partial class LauncherWindow : JelloWindow
{
    private CancellationTokenSource? _installCancellation;
    private bool _installing;

    public LauncherWindow()
    {
        InitializeComponent();

        AppState.ActivityChanged += OnActivityChanged;
        Announcements.Changed += OnAnnouncementChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            AppState.ActivityChanged -= OnActivityChanged;
            Announcements.Changed -= OnAnnouncementChanged;
        };
    }

    private void OnAnnouncementChanged() => Dispatcher.Invoke(RenderAnnouncement);

    // A pushed announcement, shown as a coloured banner. Called on load (in case one arrived
    // while the window was in the tray) and whenever the live link changes it.
    private void RenderAnnouncement()
    {
        var notice = Announcements.Current;

        if (notice is null)
        {
            AnnouncementBanner.Visibility = Visibility.Collapsed;
            return;
        }

        string hex = notice.Level switch
        {
            "critical" => "#C2410C",
            "warn" => "#B45309",
            _ => "#1D4ED8",
        };

        AnnouncementBanner.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        AnnouncementText.Text = notice.Text;
        AnnouncementBanner.Visibility = Visibility.Visible;
    }

    private void AnnouncementBanner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        AnnouncementBanner.Visibility = Visibility.Collapsed;

    protected override FrameworkElement EffectsRoot => Root;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        AppVersionText.Text = $"v{Updater.CurrentVersion.ToString(3)}";

        Guard(nameof(ApplyLayout), () => ApplyLayout());

        Guard(nameof(RefreshInstallState), RefreshInstallState);
        Guard(nameof(RefreshActivityUi), RefreshActivityUi);
        Guard(nameof(RefreshButtonRow), RefreshButtonRow);
        Guard(nameof(RenderAnnouncement), RenderAnnouncement);
    }

    public void RefreshEffects()
    {
        ApplyAllEffects();
        Guard(nameof(RefreshInstallState), RefreshInstallState);
    }

    private bool _forceClose;

    /// Closes for real, ignoring close-to-tray. Used when the window has to be rebuilt.
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose || !Settings.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        Hide();

        Log.Write("LauncherWindow::OnClosing", "Hidden to the notification area");
    }

    private void RefreshInstallState()
    {
        var state = Installer.ReadState();
        bool installed = state is not null && Installer.FindInstalledExecutable(state.VersionGuid) is not null;

        InstalledVersionText.Text = installed
            ? $"Roblox {state!.Version} · {state.Channel}"
            : "Roblox is not installed yet";

        RefreshLastPlayed();
    }

    private void RefreshLastPlayed()
    {
        if (Settings.LastPlayedUtc is not { } when)
        {
            LastPlayedText.Visibility = Visibility.Collapsed;
            return;
        }

        string name = string.IsNullOrEmpty(Settings.LastPlayedName)
            ? $"place {Settings.LastPlayedPlaceId}"
            : Settings.LastPlayedName!;

        LastPlayedText.Text = $"Last played {name} · {Ago(DateTime.UtcNow - when)}";
        LastPlayedText.Visibility = Visibility.Visible;
    }

    private static string Ago(TimeSpan span) => span switch
    {
        { TotalMinutes: < 2 } => "just now",
        { TotalMinutes: < 60 } => $"{(int)span.TotalMinutes} minutes ago",
        { TotalHours: < 2 } => "an hour ago",
        { TotalHours: < 24 } => $"{(int)span.TotalHours} hours ago",
        { TotalDays: < 2 } => "yesterday",
        { TotalDays: < 30 } => $"{(int)span.TotalDays} days ago",
        _ => "a while ago"
    };

    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        string url = Sun.DiscordInvite;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            Log.Write("LauncherWindow::SupportButton", $"Opened {url}");
        }
        catch (Exception ex)
        {
            Log.WriteException("LauncherWindow::SupportButton", ex);
            SetStatus("Could not open your browser.");
        }
    }

    private void OnActivityChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(RefreshActivityUi);

    private void RefreshActivityUi()
    {
        var watcher = AppState.ActivityWatcher;
        bool inGame = watcher?.InGame == true;

        ServerInfoButton.Visibility = inGame && !_installing ? Visibility.Visible : Visibility.Collapsed;
        RefreshButtonRow();

        if (inGame && watcher is not null)
        {
            SetStatus(string.IsNullOrEmpty(watcher.Data.UniverseName)
                ? $"In a {watcher.Data.ServerTypeLabel.ToLowerInvariant()}"
                : $"{watcher.Data.UniverseName} · {watcher.Data.ServerTypeLabel.ToLowerInvariant()}");
        }
        else if (!_installing)
        {
            SetStatus(null);
            Guard(nameof(RefreshInstallState), RefreshInstallState);
        }

        RefreshLastPlayed();
    }

    /// Deliberately not tied to the install cancellation token: that one is cancelled and
    /// disposed once the launch is done, which would kill the passes before they run.
    private async Task ApplyLiveFlagsAsync(int processId)
    {
        try
        {
            var state = Installer.ReadState();

            if (state is null)
            {
                return;
            }

            await LiveFlags.ApplyOnLaunchAsync(
                processId,
                state.VersionGuid,
                () => Settings.FastFlags,
                AppState.Http,
                result => Dispatcher.Invoke(() => SetStatus(result.Summary)),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteException("LauncherWindow::ApplyLiveFlags", ex);
        }
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetInstalling(bool installing)
    {
        _installing = installing;

        LaunchButton.IsEnabled = !installing;
        SettingsButton.IsEnabled = !installing;
        InstallProgressBar.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;

        if (installing)
        {
            ServerInfoButton.Visibility = Visibility.Collapsed;
            RefreshButtonRow();
        }
        else
        {
            RefreshActivityUi();
        }
    }

    private void RefreshButtonRow()
    {
        bool secondary = CancelButton.Visibility == Visibility.Visible
            || ServerInfoButton.Visibility == Visibility.Visible;

        System.Windows.Controls.Grid.SetColumnSpan(SettingsButton, secondary ? 1 : 3);
    }

    public Task<string?> RunInstallAsync() => RunInstallAsync(null);

    internal async Task<string?> RunInstallAsync(IBootstrapperDialog? dialog)
    {
        _installCancellation?.Cancel();
        _installCancellation = new CancellationTokenSource();

        SetInstalling(true);

        if (dialog is not null)
        {
            dialog.Cancelled += (_, _) => _installCancellation?.Cancel();
            dialog.ShowBootstrapper();
        }

        var progress = new Progress<InstallProgress>(report =>
        {
            SetStatus(report.Stage);
            InstallProgressBar.Value = Math.Clamp(report.Fraction, 0, 1);

            if (dialog is null)
            {
                return;
            }

            dialog.Message = report.Stage;
            dialog.ProgressIndeterminate = report.Indeterminate;
            dialog.ProgressValue = (int)Math.Round(Math.Clamp(report.Fraction, 0, 1) * 100);
        });

        try
        {
            var installer = new Installer(AppState.Http);

            return await installer.EnsureLatestAsync(
                App.Launch.ChannelOverride ?? Settings.Channel,
                Settings.FastFlagValues,
                progress,
                _installCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Write("LauncherWindow::RunInstall", "Cancelled by the user");
            SetStatus("Install cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            Log.WriteException("LauncherWindow::RunInstall", ex);
            SetStatus(InstallErrors.Describe(ex));
            return null;
        }
        finally
        {
            SetInstalling(false);
            Guard(nameof(RefreshInstallState), RefreshInstallState);
        }
    }

    private void WatchRobloxExit(System.Diagnostics.Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                Log.Write("LauncherWindow::WatchRobloxExit", "Roblox exited, closing auto-close integrations");

                IntegrationRunner.CloseAll();

                // The window is gone, so the icons we were holding for it are meaningless
                // and the handle must not be reused against whatever takes its place.
                WindowIcon.Forget();

                if (AppState.RobloxProcessId == process.Id)
                {
                    AppState.RobloxProcessId = 0;
                }
            };
        }
        catch (Exception ex)
        {
            Log.WriteException("LauncherWindow::WatchRobloxExit", ex);
        }
    }

    /// Two launches at once would stage the same install folder twice over, so the second
    /// waits. It only holds until the client is up, not until it closes, so launching two
    /// games in a row still works.
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);

    public async Task LaunchAsync(string launchArguments)
    {
        await LaunchGate.WaitAsync();

        try
        {
            await RunLaunchAsync(launchArguments);
        }
        finally
        {
            LaunchGate.Release();
        }
    }

    private async Task RunLaunchAsync(string launchArguments)
    {
        if (Settings.ConfirmLaunches)
        {
            var answer = MessageBox.Show(
                "Launch Roblox now?",
                "Jello Client",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (Settings.MultiInstance)
        {
            Launcher.HoldMultiInstanceMutex();
        }

        // Something else may have changed the Windows colour since last time.
        RobloxTheme.Reassert();

        var dialog = await BootstrapperStyles.CreateAsync();

        try
        {
            string? executable = await RunInstallAsync(dialog);

            if (executable is null)
            {
                return;
            }

            if (Settings.MultiInstance)
            {
                try
                {
                    executable = await Task.Run(() => MultiInstance.Resolve(executable, Settings.FastFlagValues));
                }
                catch (Exception ex)
                {
                    Log.WriteException("LauncherWindow::MultiInstance", ex);
                    SetStatus(ex.Message);
                    return;
                }
            }

            launchArguments = await ApplyMatchmakerAsync(dialog, launchArguments);

            await StartAndWaitAsync(dialog, executable, launchArguments);
        }
        finally
        {
            dialog?.CloseBootstrapper();
        }

        if (Settings.HideLauncherOnLaunch && AppState.RobloxProcessId != 0)
        {
            Hide();
        }
    }

    private async Task StartAndWaitAsync(IBootstrapperDialog? dialog, string executable, string launchArguments)
    {
        const string ident = "LauncherWindow::StartAndWait";

        var cancellation = _installCancellation ?? new CancellationTokenSource();

        if (dialog is not null)
        {
            dialog.Message = "Starting Roblox...";
            dialog.ProgressIndeterminate = true;
        }

        SetStatus("Starting Roblox...");

        using var watcher = LaunchWatcher.Arm();

        System.Diagnostics.Process process;

        try
        {
            process = Launcher.Start(executable, launchArguments);
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            SetStatus($"Could not start Roblox: {ex.Message}");
            return;
        }

        AppState.RobloxProcessId = process.Id;
        AppState.StartActivityTracking();

        WatchRobloxExit(process);
        IntegrationRunner.LaunchAll();

        if (dialog is not null)
        {
            dialog.Message = "Waiting for Roblox to open...";
        }

        SetStatus("Waiting for Roblox to open...");

        string? log = await watcher.WaitAsync(cancellation.Token);

        if (cancellation.IsCancellationRequested)
        {
            Log.Write(ident, "Cancelled while waiting, closing Roblox");

            KillRoblox();
            SetStatus("Launch cancelled.");
            return;
        }

        if (log is null)
        {
            Log.Write(ident, "Roblox never opened its log, giving up on the wait");
            SetStatus("Roblox was started but did not open within 15 seconds. It may still be loading.");
            return;
        }

        // Roblox refuses most locally configured flags on this build and says so only in
        // its own log, so read that back and record which of ours actually landed.
        _ = FlagAudit.InspectAsync(log, Settings.FastFlagValues, Installer.ReadState()?.Version ?? "unknown");

        // Flags are already in ClientAppSettings.json and were verified before the client
        // started. This is the opt in second pass into the running process, and it is
        // allowed to fail without affecting anything.
        if (Settings.LiveFlagInjection)
        {
            _ = ApplyLiveFlagsAsync(process.Id);
        }

        if (!string.IsNullOrEmpty(Settings.PreferredDisplay))
        {
            _ = Task.Run(() => WindowPlacement.MoveToDisplay(process.Id, Settings.PreferredDisplay));
        }

        await Task.Delay(LaunchWatcher.WindowSettleDelay, CancellationToken.None);

        // The window only exists once Roblox has drawn something, which is what the settle
        // delay above was waiting for.
        if (Settings.GameIconOnWindow)
        {
            AttachWindowIcon(process, executable);
        }

        ResourceLimits.Apply(process.Id);

        Log.Write(ident, "Roblox is up");
        SetStatus("Roblox launched.");
    }

    /// Rewrites the launch arguments to point at the fastest server the matchmaker could
    /// measure. When it finds nothing the arguments come back untouched and Roblox picks,
    /// which is the ordinary outcome once its lookup allowance is spent.
    private async Task<string> ApplyMatchmakerAsync(IBootstrapperDialog? dialog, string launchArguments)
    {
        if (!Settings.MatchmakerEnabled)
        {
            return launchArguments;
        }

        long placeId = LaunchArguments.PlaceIdOf(launchArguments);

        if (placeId <= 0)
        {
            placeId = Settings.LastPlayedPlaceId;
        }

        if (placeId <= 0)
        {
            Log.Write("LauncherWindow::Matchmaker", "No place to search, launching normally");
            return launchArguments;
        }

        var progress = new Progress<string>(message =>
        {
            if (dialog is not null)
            {
                dialog.Message = message;
            }

            SetStatus(message);
        });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var outcome = await Matchmaker.FindAsync(
            placeId,
            Math.Clamp(Settings.MatchmakerBudget, 1, 20),
            Settings.MatchmakerPreferEmptier,
            progress,
            cancellation.Token);

        SetStatus(outcome.Summary);

        if (!outcome.Found)
        {
            Log.Write("LauncherWindow::Matchmaker", outcome.Summary);
            return launchArguments;
        }

        Log.Write("LauncherWindow::Matchmaker",
            $"Joining {outcome.Winner!.JobId} at {outcome.Winner.Ping} ms");

        return LaunchArguments.WithJob(launchArguments, placeId, outcome.Winner.JobId);
    }

    /// Roblox does not always have its main window by the time the log opens, so this
    /// gives it a few seconds rather than one try.
    private static void AttachWindowIcon(System.Diagnostics.Process process, string executable)
    {
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    process.Refresh();

                    if (process.HasExited)
                    {
                        return;
                    }

                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        WindowIcon.Attach(process.MainWindowHandle, executable);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("LauncherWindow::AttachWindowIcon", ex.Message);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Log.Write("LauncherWindow::AttachWindowIcon", "Roblox never showed a window to put an icon on");
        });
    }

    private static void KillRoblox()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(AppState.RobloxProcessId);
            process.Kill();
        }
        catch (Exception ex)
        {
            Log.Write("LauncherWindow::KillRoblox", ex.Message);
        }

        AppState.RobloxProcessId = 0;
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) => await LaunchAsync(string.Empty);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _installCancellation?.Cancel();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

    private void ServerInfoButton_Click(object sender, RoutedEventArgs e) => App.Tray?.ShowServerInformation();

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
