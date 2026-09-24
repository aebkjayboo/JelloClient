using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using JelloClient.Interop;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.UI;

public partial class TrayMenu : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    private bool _suppressPresenceEvent;

    public TrayMenu()
    {
        InitializeComponent();
    }

    public event EventHandler? ServerInformationRequested;

    public void ShowMenu()
    {
        Activate();

        Menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        Menu.IsOpen = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;

        int style = Native.GetWindowLong(handle, GwlExStyle);
        Native.SetWindowLong(handle, GwlExStyle, style | WsExToolWindow);
    }

    private void Menu_Opened(object sender, RoutedEventArgs e) => Refresh();

    public void Refresh()
    {
        var watcher = AppState.ActivityWatcher;
        bool inGame = watcher?.InGame == true;

        HeaderItem.Header = $"Jello Client v{Updater.CurrentVersion.ToString(3)}";

        ServerInfoItem.Visibility = Collapse(inGame);
        CopyDetailsItem.Visibility = Collapse(inGame);
        CopyInviteItem.Visibility = Collapse(inGame && watcher!.Data.ServerType == ServerType.Public);
        OpenLogItem.Visibility = Collapse(watcher?.LogLocation is not null && File.Exists(watcher.LogLocation));
        CloseRobloxItem.Visibility = Collapse(AppState.IsRobloxRunning());

        var presence = AppState.RichPresence;

        RichPresenceItem.Visibility = Collapse(presence is not null);

        if (presence is not null && RichPresenceItem.IsChecked != presence.Visible)
        {
            _suppressPresenceEvent = true;
            RichPresenceItem.IsChecked = presence.Visible;
            _suppressPresenceEvent = false;
        }

        ActivitySeparator.Visibility = Collapse(
            ServerInfoItem.Visibility == Visibility.Visible
            || OpenLogItem.Visibility == Visibility.Visible
            || CloseRobloxItem.Visibility == Visibility.Visible
            || RichPresenceItem.Visibility == Visibility.Visible);
    }

    private static Visibility Collapse(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static ActivityData? Data => AppState.ActivityWatcher?.Data;

    private void Header_Click(object sender, RoutedEventArgs e) => App.ShowLauncher();

    private void ServerInfo_Click(object sender, RoutedEventArgs e) =>
        ServerInformationRequested?.Invoke(this, EventArgs.Empty);

    private void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        if (Data is { } data)
        {
            TrayIcon.SetClipboard(data.GetInviteDeeplink());
        }
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (Data is { } data)
        {
            TrayIcon.SetClipboard(data.GetClipboardSummary());
        }
    }

    private void RichPresence_Click(object sender, RoutedEventArgs e)
    {
        if (!_suppressPresenceEvent)
        {
            AppState.RichPresence?.SetVisibility(RichPresenceItem.IsChecked);
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        string? location = AppState.ActivityWatcher?.LogLocation;

        if (location is not null && File.Exists(location))
        {
            Process.Start(new ProcessStartInfo { FileName = location, UseShellExecute = true });
        }
    }

    private void CloseRoblox_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.IsRobloxRunning())
        {
            return;
        }

        var answer = MessageBox.Show(
            "Close Roblox now?",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(AppState.RobloxProcessId);
            process.Kill();

            Log.Write("TrayMenu::CloseRoblox", $"Killed process {AppState.RobloxProcessId}");
        }
        catch (Exception ex)
        {
            Log.WriteException("TrayMenu::CloseRoblox", ex);
        }
    }

    private void Launch_Click(object sender, RoutedEventArgs e) => App.LaunchFromTray();

    private void Settings_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

    // Close the UI but hand the identity link to a background agent so it keeps running.
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.SpawnAgent();
        Application.Current.Shutdown();
    }

    // Fully quit: stop any background agent too, so nothing stays connected.
    private void QuitCompletely_Click(object sender, RoutedEventArgs e)
    {
        App.SignalAgentStop();
        Application.Current.Shutdown();
    }
}
