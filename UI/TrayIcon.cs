using System.Drawing;
using System.Windows.Forms;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.UI;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly TrayMenu _menu;

    private EventHandler? _balloonHandler;
    private ServerInfoWindow? _serverInfoWindow;
    private string? _lastNotifiedJobId;
    private DateTime _lastToggle = DateTime.MinValue;
    private bool _disposed;

    public TrayIcon()
    {
        Log.Write("TrayIcon::TrayIcon", "Creating the notification area icon");

        _icon = new NotifyIcon(new System.ComponentModel.Container())
        {
            Icon = LoadIcon(),
            Text = "Jello Client",
            Visible = true
        };

        _icon.MouseClick += OnMouseClick;
        _icon.DoubleClick += (_, _) => ToggleLauncher();

        _menu = new TrayMenu();
        _menu.ServerInformationRequested += (_, _) => ShowServerInformation();
        _menu.Show();

        AppState.ActivityChanged += OnActivityChanged;
        AppState.LogOpened += OnLogOpened;
    }

    private static Icon LoadIcon()
    {
        var stream = System.Windows.Application
            .GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))!
            .Stream;

        using (stream)
        {
            var size = SystemInformation.SmallIconSize;

            Log.Write("TrayIcon::LoadIcon", $"Requesting the {size.Width}x{size.Height} frame");

            return new Icon(stream, size);
        }
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(_menu.ShowMenu);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            ToggleLauncher();
        }
    }

    private void ToggleLauncher()
    {
        var now = DateTime.UtcNow;

        if (now - _lastToggle < TimeSpan.FromMilliseconds(400))
        {
            return;
        }

        _lastToggle = now;
        App.ToggleLauncher();
    }

    private void OnLogOpened(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(_menu.Refresh);

    private void OnActivityChanged(object? sender, EventArgs e)
    {
        var watcher = AppState.ActivityWatcher;

        if (watcher is null)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _menu.Refresh();

            if (!watcher.InGame)
            {
                _serverInfoWindow?.Close();
                return;
            }

            _serverInfoWindow?.Refresh();
            NotifyServerJoin(watcher.Data);
        });
    }

    private void NotifyServerJoin(ActivityData data)
    {
        if (!AppState.Settings.NotifyOnServerJoin || data.JobId == _lastNotifiedJobId)
        {
            return;
        }

        _lastNotifiedJobId = data.JobId;

        _ = ShowJoinAlertAsync(data);
    }

    private async Task ShowJoinAlertAsync(ActivityData data)
    {
        string message = $"Job ID {data.JobId}";

        if (AppState.Settings.ShowServerDetails && data.MachineAddressValid)
        {
            string? location = await data.QueryServerLocationAsync(AppState.Http, CancellationToken.None);

            if (!string.IsNullOrEmpty(location))
            {
                message = $"Connected to a server in {location}.";
            }
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _serverInfoWindow?.Refresh();
            ShowAlert($"Joined a {data.ServerTypeLabel.ToLowerInvariant()}", message, 10, (_, _) => ShowServerInformation());
        });
    }

    public void ShowAlert(string caption, string message, int seconds, EventHandler? clickHandler)
    {
        Log.Write("TrayIcon::ShowAlert", $"{caption}: {message}");

        _icon.BalloonTipTitle = caption;
        _icon.BalloonTipText = message;

        if (_balloonHandler is not null)
        {
            _icon.BalloonTipClicked -= _balloonHandler;
        }

        _balloonHandler = clickHandler;

        if (clickHandler is not null)
        {
            _icon.BalloonTipClicked += clickHandler;
        }

        _icon.ShowBalloonTip(seconds * 1000);

        _ = ExpireAlertAsync(clickHandler, seconds);
    }

    private async Task ExpireAlertAsync(EventHandler? clickHandler, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        if (_disposed || clickHandler is null)
        {
            return;
        }

        _icon.BalloonTipClicked -= clickHandler;

        if (_balloonHandler == clickHandler)
        {
            _balloonHandler = null;
        }
    }

    public void ShowServerInformation()
    {
        if (AppState.ActivityWatcher?.InGame != true)
        {
            return;
        }

        if (_serverInfoWindow is null)
        {
            _serverInfoWindow = new ServerInfoWindow();
            _serverInfoWindow.Closed += (_, _) => _serverInfoWindow = null;
        }

        if (_serverInfoWindow.IsVisible)
        {
            _serverInfoWindow.Activate();
            return;
        }

        _serverInfoWindow.Show();
        _serverInfoWindow.Activate();
    }

    public static void SetClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(text, true);
            Log.Write("TrayIcon::SetClipboard", $"Copied {text.Length} characters");
        }
        catch (Exception ex)
        {
            Log.WriteException("TrayIcon::SetClipboard", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        AppState.ActivityChanged -= OnActivityChanged;
        AppState.LogOpened -= OnLogOpened;

        _menu.Dispatcher.Invoke(_menu.Close);

        _icon.Visible = false;
        _icon.Dispose();
    }
}
