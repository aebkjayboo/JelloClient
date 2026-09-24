using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Game icons from Roblox's public thumbnail API.
///
/// The endpoint answers without a login, and the URL it hands back is a CDN link that
/// stays good for a long while, so the answer is cached for the session: joining the same
/// game twice should not cost two round trips.
internal static class Thumbnails
{
    private const string Endpoint = "https://thumbnails.roblox.com/v1/games/icons";

    private const int Size = 512;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly Dictionary<long, string?> Cache = new();

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private sealed class Response
    {
        [JsonPropertyName("data")]
        public List<Entry> Data { get; set; } = new();
    }

    private sealed class Entry
    {
        [JsonPropertyName("imageUrl")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }

    public static async Task<string?> GameIconUrlAsync(long universeId, CancellationToken token = default)
    {
        const string ident = "Thumbnails::GameIconUrl";

        if (universeId == 0)
        {
            return null;
        }

        await Gate.WaitAsync(token);

        try
        {
            if (Cache.TryGetValue(universeId, out string? cached))
            {
                return cached;
            }
        }
        finally
        {
            Gate.Release();
        }

        string? url = null;

        try
        {
            var response = await Http.GetFromJsonAsync<Response>(
                $"{Endpoint}?universeIds={universeId}&size={Size}x{Size}&format=Png&isCircular=false",
                token);

            var entry = response?.Data.FirstOrDefault();

            // A thumbnail that is still rendering comes back with a placeholder URL, and
            // caching that would keep the placeholder for the whole session.
            if (entry is { State: "Completed" } && !string.IsNullOrEmpty(entry.ImageUrl))
            {
                url = entry.ImageUrl;
            }
            else
            {
                Log.Write(ident, $"Universe {universeId} icon is {entry?.State ?? "missing"}, not caching it");
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.Write(ident, $"Could not fetch the icon for {universeId}: {ex.Message}");
            return null;
        }

        await Gate.WaitAsync(token);

        try
        {
            Cache[universeId] = url;
        }
        finally
        {
            Gate.Release();
        }

        return url;
    }
}
