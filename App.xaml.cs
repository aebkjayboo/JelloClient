using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JelloClient.Interop;
using JelloClient.Roblox;
using JelloClient.Services;
using JelloClient.UI;

namespace JelloClient;

public partial class App : Application
{
    private static int _reporting;

    private static LauncherWindow? _launcher;
    private static SettingsWindow? _settings;

    internal static LaunchSettings Launch { get; private set; } = new(Array.Empty<string>());

    internal static TrayIcon? Tray { get; private set; }

    partial void StartIdentity();

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        Paths.Initialize(FirstRun.RecordedLocation());

        Log.Start(e.Args);

        Launch = new LaunchSettings(e.Args);
        Log.Write("App::OnStartup", $"Launch settings: {Launch.Describe()}");

        // Harden the process before anything sensitive runs: block external memory reads/writes,
        // injection and debuggers. Applies to both the UI and the -agent process.
        ProcessHardening.Apply();

        // If we're elevated, exclude Jello's app/data folder from Defender so pushed plugins
        // (native exes in the run dir, Vault blobs) aren't scanned/quarantined mid-run. No-op
        // without admin. Runs in the background. Applies to both the UI and the -agent process.
        DefenderExclusion.EnsureForAppFolder();

        // The headless background agent (same executable, -agent). It only keeps the identity
        // link alive - no window, no tray - so the link survives the user exiting the UI. It
        // bypasses the single-instance/UI path entirely.
        if (Launch.IsAgent)
        {
            RunAsAgent();
            return;
        }

        // Uninstall (from Add/Remove Programs -> "<exe>" -uninstall): remove Jello's own
        // footprint and exit. Nothing else runs.
        if (e.Args.Any(a => a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            FirstRun.Uninstall();
            Shutdown();
            return;
        }

        Updater.CleanUpPreviousUpdate();
        ProtocolHandler.ReassertIfRegistered();

        try
        {
            Themes.Apply(AppState.Settings.Theme);
            Locales.Apply(AppState.Settings.Locale);
        }
        catch (Exception ex)
        {
            Log.WriteException("App::OnStartup", ex);
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The elevated pass Jello starts for itself: do the one thing and go. It must come
        // before the single instance check, because the copy that asked for it is still
        // running and holds the mutex.
        if (Launch.TelemetryBlockChange is { } change)
        {
            try
            {
                TelemetryBlock.Write(change);
            }
            catch (Exception ex)
            {
                Log.WriteException("App::TelemetryBlock", ex);
            }

            Shutdown();
            return;
        }

        // One Jello, whatever started it. Launches used to be exempt here so that a game
        // started from the website could bootstrap itself, but that left a Jello behind
        // after every launch. A second copy now hands its command line over and exits, and
        // the copy already running acts on it. Multi instance is untouched, because one
        // Jello can start as many clients as it likes.
        if (!SingleInstance.Claim())
        {
            if (SingleInstance.HandOff(e.Args))
            {
                Log.Write("App::OnStartup", "Jello is already running, handed this start to it");

                Shutdown();
                return;
            }

            Log.Write("App::OnStartup", "The running copy did not answer, carrying on as a second one");
        }
        else
        {
            SingleInstance.Listen(args => Current.Dispatcher.Invoke(() => HandleSecondCopy(args)));
        }

        if (!FirstRun.IsInstalled() && !RunFirstRun())
        {
            Shutdown();
            return;
        }

        // If this build is newer than (or different code from) the installed copy, refresh the
        // install so what's on disk is always the latest code. A no-op when we ARE the install.
        FirstRun.SyncInstallIfStale();

        Tray = new TrayIcon();

        AutoCleaner.Reschedule();

        Roblox.Memory.GuiTintRunner.Refresh();
        Roblox.AudioDuck.Refresh();

        // If a background agent is holding the link, tell it to stand down - this UI process
        // takes the link over now, so there is only ever one.
        SignalAgentStop();

        // The identity step, implemented privately. This declaration compiles to nothing if
        // the private implementation is not present, so the app always builds.
        StartIdentity();

        var launcher = GetLauncher();

        if (Launch.ShouldBootstrap)
        {
            if (!Launch.Quiet)
            {
                launcher.Show();
            }

            _ = launcher.LaunchAsync(Launch.RobloxLaunchArgs);
            return;
        }

        if (Launch.ForceSettings)
        {
            ShowSettings();
            return;
        }

        if (Launch.NoLaunch)
        {
            Log.Write("App::OnStartup", "Started with -nolaunch, staying in the notification area");
            return;
        }

        if (_openLauncherAfterInstall)
        {
            launcher.Show();
        }
        else
        {
            ShowSettings();
        }
    }

    // ── Background agent: same executable, -agent, no UI, keeps the identity link alive ──────
    private const string AgentMutexName = @"Global\JelloClient.Agent";
    private const string AgentStopEvent = @"Global\JelloClient.AgentStop";
    private static Mutex? _agentMutex;

    private void RunAsAgent()
    {
        try
        {
            _agentMutex = new Mutex(true, AgentMutexName, out bool first);
            if (!first)
            {
                Log.Write("App::Agent", "An agent is already running; exiting");
                Shutdown();
                return;
            }
        }
        catch (Exception ex) { Log.WriteException("App::Agent", ex); }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Log.Write("App::Agent", "Headless identity agent started");

        StartIdentity();

        // Wait off the UI thread for a stand-down signal (the UI reclaiming the link, or a
        // full quit), then exit.
        Task.Run(() =>
        {
            try
            {
                using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, AgentStopEvent);
                stop.WaitOne();
            }
            catch (Exception ex) { Log.WriteException("App::Agent", ex); }

            Log.Write("App::Agent", "Stand-down signalled; exiting");
            Dispatcher.Invoke(Shutdown);
        });
    }

    /// Hand the link off to a background agent (same executable, -agent) so it keeps running
    /// after the UI closes. Called by the tray "Exit".
    internal static void SpawnAgent()
    {
        try
        {
            string exe = Environment.ProcessPath ?? Paths.ApplicationFile;
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "-agent", UseShellExecute = false, CreateNoWindow = true });
            Log.Write("App::SpawnAgent", "Handed the link to a background agent");
        }
        catch (Exception ex) { Log.WriteException("App::SpawnAgent", ex); }
    }

    /// Tell any running background agent to stand down - used when the UI takes the link back,
    /// or on a full quit.
    internal static void SignalAgentStop()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(AgentStopEvent, out var stop))
            {
                stop.Set();
                stop.Dispose();
            }
        }
        catch (Exception ex) { Log.WriteException("App::SignalAgentStop", ex); }
    }

    private static LauncherWindow GetLauncher()
    {
        if (_launcher is null)
        {
            _launcher = new LauncherWindow();
            _launcher.Closed += (_, _) => _launcher = null;
        }

        return _launcher;
    }

    private static SettingsWindow GetSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
        }

        return _settings;
    }

    /// No install marker means this is either a first run or a portable copy someone
    /// double clicked. A Roblox link still has to launch, so that case installs quietly
    /// to the default location the way Bloxstrap does; anything else gets the wizard.
    private bool RunFirstRun()
    {
        if (Launch.ShouldBootstrap || Launch.Quiet)
        {
            Log.Write("App::RunFirstRun", "Launched for Roblox, installing quietly to the default location");

            FirstRun.Install(new FirstRun.Options(
                Paths.Default,
                DesktopShortcut: false,
                StartMenuShortcut: false,
                RegisterProtocol: true,
                RunAtStartup: false,
                AppState.Settings.Theme,
                AppState.Settings.Locale), implicitInstall: true);

            return true;
        }

        var installer = new InstallerWindow();

        installer.ShowDialog();

        if (!installer.Completed)
        {
            Log.Write("App::RunFirstRun", "Installer closed without finishing");
            return false;
        }

        _openLauncherAfterInstall = installer.OpenLauncherOnClose;

        return true;
    }

    private static bool _openLauncherAfterInstall = true;

    /// A second copy handed us its command line. A Roblox link launches from here, so the
    /// game still starts, and nothing is left running afterwards but this one window.
    private static void HandleSecondCopy(string[] args)
    {
        var started = new LaunchSettings(args);

        Log.Write("App::HandleSecondCopy", started.Describe());

        if (started.ShouldBootstrap)
        {
            var launcher = GetLauncher();

            if (!started.Quiet)
            {
                Surface(launcher);
            }

            _ = launcher.LaunchAsync(started.RobloxLaunchArgs);
            return;
        }

        if (started.ForceSettings)
        {
            ShowSettings();
            return;
        }

        if (started.NoLaunch)
        {
            // Nothing to show: it asked for the notification area.
            return;
        }

        SurfaceForSecondCopy();
    }

    /// Whatever the person was last looking at is what comes back up.
    private static void SurfaceForSecondCopy()
    {
        if (_settings is not null)
        {
            ShowSettings();
            return;
        }

        ShowLauncher();
    }

    internal static string? PendingSettingsTab { get; set; }

    /// Swaps the palette and rebuilds whatever is open, because a loaded window keeps the
    /// brushes it resolved when it was created.
    internal static void ApplyTheme(string? returnToTab = null)
    {
        Current.Dispatcher.Invoke(() =>
        {
            Themes.Apply(AppState.Settings.Theme);

            bool settingsOpen = _settings is not null;
            bool launcherVisible = _launcher is { IsVisible: true };

            _settings?.Close();
            _settings = null;

            _launcher?.ForceClose();
            _launcher = null;

            if (launcherVisible)
            {
                Surface(GetLauncher());
            }

            if (settingsOpen)
            {
                PendingSettingsTab = returnToTab;
                Surface(GetSettings());
            }
        });
    }

    internal static void ApplyLauncherLayout() =>
        Current.Dispatcher.Invoke(() => _launcher?.ApplyLayout());

    internal static void ShowLauncher()
    {
        Current.Dispatcher.Invoke(() =>
        {
            try
            {
                Surface(GetLauncher());
            }
            catch (InvalidOperationException ex)
            {
                Log.Write("App::ShowLauncher", $"Existing launcher was unusable, recreating it: {ex.Message}");

                _launcher = null;
                Surface(GetLauncher());
            }
        });
    }

    internal static void LaunchFromTray()
    {
        Current.Dispatcher.Invoke(() =>
        {
            var launcher = GetLauncher();

            Surface(launcher);

            _ = launcher.LaunchAsync(string.Empty);
        });
    }

    internal static void ToggleLauncher()
    {
        Current.Dispatcher.Invoke(() =>
        {
            var launcher = _launcher;

            if (launcher is not null
                && launcher.IsVisible
                && launcher.WindowState != WindowState.Minimized
                && WindowEffects.IsForeground(new WindowInteropHelper(launcher).Handle))
            {
                launcher.Hide();
                Log.Write("App::ToggleLauncher", "Launcher hidden to the notification area");
                return;
            }

            ShowLauncher();
        });
    }

    internal static void ShowSettings()
    {
        Current.Dispatcher.Invoke(() =>
        {
            try
            {
                Surface(GetSettings());
            }
            catch (InvalidOperationException ex)
            {
                Log.Write("App::ShowSettings", $"Existing settings window was unusable, recreating it: {ex.Message}");

                _settings = null;
                Surface(GetSettings());
            }
        });
    }

    private static void Surface(Window window)
    {
        bool wasHidden = !window.IsVisible || window.WindowState == WindowState.Minimized;

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();

        WindowEffects.ForceForeground(new WindowInteropHelper(window).Handle);

        if (wasHidden)
        {
            Log.Write("App::Surface", $"Surfaced {window.GetType().Name} (visible={window.IsVisible}, state={window.WindowState})");
        }
    }

    internal static void RefreshWindowEffects() => _launcher?.RefreshEffects();

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Write("App::OnExit", $"Exiting with code {e.ApplicationExitCode}");

        IntegrationRunner.CloseAll();
        AppState.StopActivityTracking();
        Roblox.Launcher.ReleaseMultiInstanceMutex();
        Roblox.Memory.GuiTintRunner.Stop();
        Roblox.AudioDuck.Stop();
        SingleInstance.Release();
        AppState.Persist();

        Tray?.Dispose();
        Tray = null;

        Log.Stop();

        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report("App::DispatcherUnhandledException", e.Exception, true);
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Report("App::UnhandledException", exception, false);
        }
        else
        {
            Log.Write("App::UnhandledException", $"Non-exception fault: {e.ExceptionObject}");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Log.WriteException("App::UnobservedTaskException", e.Exception);
    }

    private static void Report(string ident, Exception exception, bool shutdown)
    {
        Log.WriteException(ident, exception);

        if (Interlocked.Exchange(ref _reporting, 1) == 1)
        {
            return;
        }

        Show(Describe(exception));

        Log.Stop();

        if (!shutdown)
        {
            return;
        }

        try
        {
            Current?.Dispatcher.Invoke(() => Current.Shutdown(1));
        }
        catch
        {
            Environment.Exit(1);
        }
    }

    private static void Show(string message)
    {
        try
        {
            var current = Current;

            if (current is null)
            {
                MessageBox.Show(message, "Jello Client crashed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            current.Dispatcher.Invoke(() =>
                MessageBox.Show(message, "Jello Client crashed", MessageBoxButton.OK, MessageBoxImage.Error));
        }
        catch (Exception ex)
        {
            Log.WriteException("App::Report", ex);
        }
    }

    private static string Describe(Exception exception)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Jello Client hit an unrecoverable error and has to close.");
        builder.AppendLine();

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            builder.AppendLine($"{current.GetType().FullName}: {current.Message}");
        }

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            builder.AppendLine();
            builder.AppendLine(exception.StackTrace);
        }

        if (Log.FilePath is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"Full details: {Log.FilePath}");
        }

        return builder.ToString();
    }
}
