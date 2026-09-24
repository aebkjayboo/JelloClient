using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.Integrations;

internal sealed class UniverseCreator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class UniverseDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("creator")]
    public UniverseCreator Creator { get; set; } = new();
}

internal sealed class UniverseDetailResponse
{
    [JsonPropertyName("data")]
    public List<UniverseDetail> Data { get; set; } = new();
}

internal sealed class ThumbnailEntry
{
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
}

internal sealed class ThumbnailResponse
{
    [JsonPropertyName("data")]
    public List<ThumbnailEntry> Data { get; set; } = new();
}

internal sealed class DiscordRichPresence : IDisposable
{
    public const string ApplicationId = "1516857351477526642";

    private readonly HttpClient _http;
    private readonly ActivityWatcher _watcher;
    private readonly UserSettings _settings;
    private readonly DiscordIpcClient _client;
    private readonly CancellationTokenSource _cancellation = new();

    private bool _visible = true;
    private bool _disposed;

    public DiscordRichPresence(HttpClient http, ActivityWatcher watcher, UserSettings settings)
    {
        _http = http;
        _watcher = watcher;
        _settings = settings;
        _client = new DiscordIpcClient(ApplicationId);

        _watcher.OnGameJoin += (_, _) => _ = UpdateAsync();
        _watcher.OnGameLeave += (_, _) => _ = UpdateAsync();
    }

    public bool Visible => _visible;

    public async Task StartAsync()
    {
        if (!await _client.ConnectAsync(_cancellation.Token).ConfigureAwait(false))
        {
            Log.Write("DiscordRichPresence::Start", "Discord is not running, rich presence is inactive");
            return;
        }

        await UpdateAsync().ConfigureAwait(false);
    }

    public void SetVisibility(bool visible)
    {
        Log.Write("DiscordRichPresence::SetVisibility", $"Presence visibility is now {visible}");

        _visible = visible;
        _ = UpdateAsync();
    }

    public async Task UpdateAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_client.IsConnected && !await _client.ConnectAsync(_cancellation.Token).ConfigureAwait(false))
            {
                return;
            }

            if (!_visible || !_watcher.InGame)
            {
                await _client.SetActivityAsync(null, _cancellation.Token).ConfigureAwait(false);
                return;
            }

            var activity = await BuildActivityAsync(_watcher.Data).ConfigureAwait(false);

            if (activity is null)
            {
                return;
            }

            Log.Write("DiscordRichPresence::Update", $"Activity payload: {JsonSerializer.Serialize(activity)}");

            var result = await _client.SetActivityAsync(activity, _cancellation.Token).ConfigureAwait(false);

            if (!result.Acknowledged)
            {
                Log.Write("DiscordRichPresence::Update", $"Discord did not accept the activity: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.WriteException("DiscordRichPresence::Update", ex);
        }
    }

    private async Task<object?> BuildActivityAsync(ActivityData data)
    {
        var detail = await FetchUniverseAsync(data.UniverseId).ConfigureAwait(false);

        string name = detail?.Name ?? "Roblox";
        string state = data.ServerType switch
        {
            ServerType.Private => "In a private server",
            ServerType.Reserved => "In a reserved server",
            _ => detail is null ? "In a public server" : $"by {detail.Creator.Name}"
        };

        if (name.Length < 2)
        {
            name += "⠀⠀⠀";
        }

        string largeImage = await FetchThumbnailAsync(data.UniverseId).ConfigureAwait(false) ?? "roblox";

        var buttons = new List<object>();

        if (!_settings.HideRichPresenceButtons && data.ServerType == ServerType.Public)
        {
            buttons.Add(new { label = "Join server", url = data.GetInviteDeeplink() });
        }

        buttons.Add(new { label = "See game page", url = data.GameUrl });

        DateTime started = data.TimeJoined == default ? DateTime.Now : data.TimeJoined;

        var payload = new Dictionary<string, object>
        {
            ["details"] = name,
            ["state"] = state,
            ["timestamps"] = new
            {
                start = new DateTimeOffset(started.ToUniversalTime()).ToUnixTimeMilliseconds()
            },
            ["buttons"] = buttons
        };

        // Discord's party renders as a fraction, "4 of 12", which needs the number of
        // people in this server and the size of this server. Jello only has the number
        // across the whole experience - Roblox's log does not carry the per server count -
        // so a party here would have read "54 of 8". The honest figure goes in the state
        // line instead.
        if (_settings.ShowServerFillOnDiscord && data.PlayerCount > 0)
        {
            state = $"{state} - {data.PlayerCount:N0} playing";
            payload["state"] = state;
        }

        var assets = new Dictionary<string, object>
        {
            ["large_image"] = largeImage,
            ["large_text"] = name
        };

        // The small badge says which kind of server it is, and hovering it gives the
        // location when the lookup has finished. Nothing is fetched for it here: it uses
        // whatever the activity watcher already knows.
        string badge = data.ServerType switch
        {
            ServerType.Private => "Private server",
            ServerType.Reserved => "Reserved server",
            _ => "Public server"
        };

        assets["small_image"] = "roblox";
        assets["small_text"] = string.IsNullOrEmpty(data.ServerLocation)
            ? badge
            : $"{badge} - {data.ServerLocation}";

        payload["assets"] = assets;

        return payload;
    }

    private async Task<UniverseDetail?> FetchUniverseAsync(long universeId)
    {
        if (universeId == 0)
        {
            return null;
        }

        try
        {
            var response = await _http
                .GetFromJsonAsync<UniverseDetailResponse>(
                    $"https://games.roblox.com/v1/games?universeIds={universeId}",
                    _cancellation.Token)
                .ConfigureAwait(false);

            return response?.Data.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Write("DiscordRichPresence::FetchUniverse", $"Could not fetch universe {universeId}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> FetchThumbnailAsync(long universeId)
    {
        if (universeId == 0)
        {
            return null;
        }

        try
        {
            var response = await _http
                .GetFromJsonAsync<ThumbnailResponse>(
                    $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=512x512&format=Png&isCircular=false",
                    _cancellation.Token)
                .ConfigureAwait(false);

            return response?.Data.FirstOrDefault()?.ImageUrl;
        }
        catch (Exception ex)
        {
            Log.Write("DiscordRichPresence::FetchThumbnail", $"Could not fetch a thumbnail for {universeId}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _client.SetActivityAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Write("DiscordRichPresence::Dispose", ex.Message);
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
        _client.Dispose();
    }
}
