using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JelloClient.Integrations;
using JelloClient.Roblox;

namespace JelloClient.Services;

internal sealed class UniverseNameEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class UniverseNameResponse
{
    [JsonPropertyName("data")]
    public List<UniverseNameEntry> Data { get; set; } = new();
}

internal static class AppState
{
    private static ActivityWatcher? _activityWatcher;
    private static DiscordRichPresence? _richPresence;

    public static UserSettings Settings { get; } = SettingsStore.Load();

    public static HttpClient Http { get; } = CreateHttpClient();

    public static ActivityWatcher? ActivityWatcher => _activityWatcher;

    public static DiscordRichPresence? RichPresence => _richPresence;

    public static int RobloxProcessId { get; set; }

    public static event EventHandler? ActivityChanged;

    public static event EventHandler? LogOpened;

    public static void Persist() => SettingsStore.Save(Settings);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 8
        })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("JelloClient/2.0");

        return client;
    }

    public static void StartActivityTracking()
    {
        if (_activityWatcher is not null || !Settings.EnableActivityTracking)
        {
            return;
        }

        Log.Write("AppState::StartActivityTracking", "Starting the Roblox log watcher");

        var watcher = new ActivityWatcher();
        _activityWatcher = watcher;

        watcher.OnGameJoin += RememberLastPlayed;
        watcher.OnGameJoin += OnActivityChanged;
        watcher.OnGameJoin += ShowGameIcon;
        watcher.OnGameLeave += OnActivityChanged;
        watcher.OnGameLeave += RestoreGameIcon;
        watcher.OnLogOpen += (s, _) => LogOpened?.Invoke(s, EventArgs.Empty);

        _ = watcher.StartAsync();

        if (Settings.UseDiscordRichPresence)
        {
            _richPresence = new DiscordRichPresence(Http, watcher, Settings);
            _ = _richPresence.StartAsync();
        }
    }

    public static void StopActivityTracking()
    {
        _richPresence?.Dispose();
        _richPresence = null;

        if (_activityWatcher is not null)
        {
            _activityWatcher.OnGameJoin -= OnActivityChanged;
            _activityWatcher.OnGameJoin -= ShowGameIcon;
            _activityWatcher.OnGameLeave -= OnActivityChanged;
            _activityWatcher.OnGameLeave -= RestoreGameIcon;
            _activityWatcher.Dispose();
            _activityWatcher = null;
        }

        WindowIcon.Forget();
    }

    private static void ShowGameIcon(object? sender, EventArgs e)
    {
        long universe = _activityWatcher?.Data.UniverseId ?? 0;

        if (universe != 0)
        {
            _ = WindowIcon.ShowGameAsync(universe);
        }
    }

    private static void RestoreGameIcon(object? sender, EventArgs e) => WindowIcon.Restore();

    private static void OnActivityChanged(object? sender, EventArgs e) =>
        ActivityChanged?.Invoke(sender, EventArgs.Empty);

    /// The launcher shows what you were last in, so the join is worth remembering even
    /// when the experience name only resolves a moment later.
    private static void RememberLastPlayed(object? sender, EventArgs e)
    {
        var data = _activityWatcher?.Data;

        if (data is null || data.PlaceId == 0)
        {
            return;
        }

        Settings.LastPlayedPlaceId = data.PlaceId;
        Settings.LastPlayedUtc = DateTime.UtcNow;
        Settings.LastPlayedName = data.UniverseName;

        Persist();

        Log.Write("AppState::RememberLastPlayed", $"Last played {data.UniverseName ?? data.PlaceId.ToString()}");

        if (data.UniverseName is null && data.UniverseId != 0)
        {
            _ = ResolveLastPlayedNameAsync(data);
        }
    }

    /// The log gives us the universe id long before a name, so the name is filled in
    /// afterwards and the launcher told to redraw.
    private static async Task ResolveLastPlayedNameAsync(ActivityData data)
    {
        try
        {
            var response = await Http
                .GetFromJsonAsync<UniverseNameResponse>(
                    $"https://games.roblox.com/v1/games?universeIds={data.UniverseId}")
                .ConfigureAwait(false);

            string? name = response?.Data.FirstOrDefault()?.Name;

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            data.UniverseName = name;

            Settings.LastPlayedName = name;
            Persist();

            ActivityChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Write("AppState::ResolveLastPlayedName", $"Could not resolve the experience name: {ex.Message}");
        }
    }

    public static bool IsRobloxRunning()
    {
        if (RobloxProcessId == 0)
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(RobloxProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
