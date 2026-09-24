using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Windows;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.UI;

internal sealed class UniversePlayCount
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("playing")]
    public int Playing { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }
}

internal sealed class UniversePlayCountResponse
{
    [JsonPropertyName("data")]
    public List<UniversePlayCount> Data { get; set; } = new();
}

public partial class ServerInfoWindow : JelloWindow
{
    public ServerInfoWindow()
    {
        InitializeComponent();
    }

    protected override FrameworkElement EffectsRoot => Root;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Guard(nameof(Refresh), Refresh);
    }

    public void Refresh()
    {
        var watcher = AppState.ActivityWatcher;

        if (watcher is null || !watcher.InGame)
        {
            return;
        }

        var data = watcher.Data;

        UniverseNameText.Text = data.UniverseName ?? "Looking up...";
        PlaceText.Text = data.PlaceId == 0 ? "Unknown" : data.PlaceId.ToString();
        UniverseIdText.Text = data.UniverseId == 0 ? "Unknown" : data.UniverseId.ToString();

        PlayersText.Text = Describe(data.PlayerCount, data.MaxPlayers);

        ServerTypeText.Text = data.ServerTypeLabel;
        JobIdText.Text = string.IsNullOrEmpty(data.JobId) ? "Unknown" : data.JobId;
        AddressText.Text = string.IsNullOrEmpty(data.MachineAddress) ? "Unknown" : data.Endpoint;
        JoinedText.Text = data.TimeJoined == default ? "Unknown" : data.TimeJoined.ToString("t");

        CopyInviteButton.IsEnabled = data.ServerType == ServerType.Public;

        // Each of these fills a different field and each has to be asked for on its own
        // condition. Gating them all on one field is what left "Looking up..." sitting
        // under Players and Provider for good whenever the name or the city happened to
        // be known already.
        if (data.UniverseId != 0 && (data.UniverseName is null || data.PlayerCount < 0))
        {
            _ = ResolveUniverseAsync(data);
        }

        bool details = AppState.Settings.ShowServerDetails;

        SetRow(PingLabel, PingText, details);
        SetRow(HostnameLabel, HostnameText, details);
        SetRow(LocationLabel, LocationText, details);
        SetRow(ProviderLabel, ProviderText, details);

        if (!details)
        {
            return;
        }

        PingText.Text = data.MachineAddressValid ? "Measuring..." : "Not available";
        LocationText.Text = data.ServerLocation ?? "Looking up...";
        HostnameText.Text = data.Hostname ?? "Looking up...";
        ProviderText.Text = data.Provider ?? "Looking up...";

        // Each of the three runs on its own. Hostname used to be started only when the
        // location was unknown, so a server whose city was already cached left the
        // hostname on "Looking up..." for good.
        if (data.ServerLocation is null || data.Provider is null)
        {
            _ = ResolveLocationAsync(data);
        }

        if (data.Hostname is null && data.MachineAddressValid)
        {
            _ = ResolveHostnameAsync(data);
        }

        if (data.MachineAddressValid)
        {
            _ = MeasurePingAsync(data);
        }
    }

    /// The count is across the whole experience and the cap is per server, so they are
    /// stated separately rather than joined into a fraction that cannot be read.
    private static string Describe(int playing, int capacity)
    {
        if (playing < 0)
        {
            return "Looking up...";
        }

        string people = playing == 1 ? "1 playing" : $"{playing:N0} playing";

        return capacity > 0 ? $"{people}, {capacity} per server" : people;
    }

    private static void SetRow(UIElement label, UIElement value, bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;

        label.Visibility = state;
        value.Visibility = state;
    }

    /// The city, from ipinfo.io.
    private async Task ResolveLocationAsync(ActivityData data)
    {
        await data.QueryServerLocationAsync(AppState.Http, CancellationToken.None);

        Dispatcher.Invoke(() =>
        {
            LocationText.Text = data.ServerLocation ?? "Not available";
            ProviderText.Text = data.Provider ?? "Not available";

            // ipinfo sometimes carries the hostname too, which beats waiting for DNS.
            if (data.Hostname is { } name)
            {
                HostnameText.Text = name;
            }
        });
    }

    /// Reverse DNS, on its own. Most Roblox machines publish no name at all, so the
    /// ordinary answer here is "none" rather than a failure.
    private async Task ResolveHostnameAsync(ActivityData data)
    {
        await data.QueryHostnameAsync(CancellationToken.None);

        Dispatcher.Invoke(() =>
            HostnameText.Text = data.Hostname ?? "None published");
    }

    /// The one latency figure Jello can get for free and for certain: the round trip to
    /// the server actually being played on. Roblox drops ICMP, so this is a TCP handshake
    /// to the same machine, which measures the path rather than guessing it from a map.
    private async Task MeasurePingAsync(ActivityData data)
    {
        int? ping = await ServerPing.MeasureAsync(data.MachineAddress, CancellationToken.None);

        Dispatcher.Invoke(() =>
            PingText.Text = ping is { } value ? $"{value} ms" : "No answer");
    }

    private async Task ResolveUniverseAsync(ActivityData data)
    {
        try
        {
            var response = await AppState.Http
                .GetFromJsonAsync<UniversePlayCountResponse>(
                    $"https://games.roblox.com/v1/games?universeIds={data.UniverseId}",
                    CancellationToken.None)
                .ConfigureAwait(false);

            var detail = response?.Data.FirstOrDefault();

            if (detail is null)
            {
                return;
            }

            data.UniverseName = detail.Name;
            data.PlayerCount = detail.Playing;
            data.MaxPlayers = detail.MaxPlayers;

            Log.Write("ServerInfoWindow::ResolveUniverse", $"{detail.Name}: {detail.Playing} playing, {detail.MaxPlayers} max");

            Dispatcher.Invoke(() =>
            {
                UniverseNameText.Text = detail.Name;
                PlayersText.Text = Describe(detail.Playing, detail.MaxPlayers);
            });
        }
        catch (Exception ex)
        {
            Log.Write("ServerInfoWindow::ResolveUniverse", $"Could not fetch universe {data.UniverseId}: {ex.Message}");

            Dispatcher.Invoke(() =>
            {
                UniverseNameText.Text = "Not available";
                PlayersText.Text = "Not available";
            });
        }
    }

    private void Copy(string text, string confirmation)
    {
        TrayIcon.SetClipboard(text);
        CopyStatusText.Text = confirmation;
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.ActivityWatcher?.Data is { } data)
        {
            Copy(data.GetClipboardSummary(), "Server details copied to the clipboard.");
        }
    }

    private void CopyJobId_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.ActivityWatcher?.Data is { } data)
        {
            Copy(data.JobId, "Job ID copied to the clipboard.");
        }
    }

    private void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.ActivityWatcher?.Data is { } data)
        {
            Copy(data.GetInviteDeeplink(), "Join link copied to the clipboard.");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
