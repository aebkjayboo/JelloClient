using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed class ClientVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("clientVersionUpload")]
    public string VersionGuid { get; set; } = "";

    [JsonPropertyName("bootstrapperVersion")]
    public string BootstrapperVersion { get; set; } = "";
}

internal sealed class InvalidChannelException : Exception
{
    public InvalidChannelException(string channel, HttpStatusCode? status)
        : base($"The '{channel}' channel is not available to your account ({(int?)status} {status}). "
             + $"Roblox restricts non-production channels. Set the channel back to '{Deployment.DefaultChannel}' "
             + "on the Deployment page to fix this.")
    {
        Channel = channel;
        StatusCode = status;
    }

    public string Channel { get; }

    public HttpStatusCode? StatusCode { get; }
}

internal static class Deployment
{
    public const string DefaultChannel = "production";

    private const string VersionStudioHash = "version-012732894899482c";

    private static readonly IReadOnlyDictionary<string, int> Mirrors = new Dictionary<string, int>
    {
        { "https://setup.rbxcdn.com", 0 },
        { "https://setup-aws.rbxcdn.com", 2 },
        { "https://setup-ak.rbxcdn.com", 2 },
        { "https://roblox-setup.cachefly.net", 2 },
        { "https://s3.amazonaws.com/setup.roblox.com", 4 }
    };

    private static readonly HttpStatusCode?[] BadChannelCodes =
    {
        HttpStatusCode.Unauthorized,
        HttpStatusCode.Forbidden,
        HttpStatusCode.NotFound
    };

    private static readonly Dictionary<string, ClientVersion> Cache = new();

    public static string Channel { get; set; } = DefaultChannel;

    public static string BinaryType { get; set; } = "WindowsPlayer";

    public static string? BaseUrl { get; private set; }

    public static async Task InitializeConnectivityAsync(HttpClient http, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            Log.Write("Deployment::InitializeConnectivity", $"Already connected to {BaseUrl}");
            return;
        }

        Log.Write("Deployment::InitializeConnectivity", $"Racing {Mirrors.Count} setup mirrors");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var tasks = Mirrors
            .Select(entry => TestMirrorAsync(http, entry.Key, entry.Value, linked.Token))
            .ToList();

        var failures = new List<Exception>();

        while (tasks.Count > 0 && string.IsNullOrEmpty(BaseUrl))
        {
            var finished = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(finished);

            if (finished.IsFaulted)
            {
                var failure = finished.Exception!.InnerException ?? finished.Exception;
                failures.Add(failure);
                Log.Write("Deployment::InitializeConnectivity", $"A mirror failed: {failure.Message}");
            }
            else if (!finished.IsCanceled)
            {
                BaseUrl = finished.Result;
                Log.Write("Deployment::InitializeConnectivity", $"Won by {BaseUrl}");
            }
        }

        linked.Cancel();

        if (string.IsNullOrEmpty(BaseUrl))
        {
            Log.Write("Deployment::InitializeConnectivity", $"Every mirror failed after {failures.Count} error(s)");

            throw failures.Count > 0
                ? new HttpRequestException("Could not reach any Roblox setup mirror.", failures[0])
                : new TimeoutException("All connection attempts to the Roblox setup mirrors timed out.");
        }
    }

    private static async Task<string> TestMirrorAsync(HttpClient http, string url, int priority, CancellationToken ct)
    {
        await Task.Delay(priority * 1000, ct).ConfigureAwait(false);

        string probeUrl = $"{url}/versionStudio";

        using var response = await RobloxHttp.GetAsync(
            http,
            probeUrl,
            "Probing the setup mirror",
            "This mirror is unreachable. Jello will fall back to another one.",
            HttpCompletionOption.ResponseContentRead,
            ct).ConfigureAwait(false);

        string content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (content.Trim() != VersionStudioHash)
        {
            Log.Write("Deployment::TestMirror", $"{url} failed validation");

            throw new HttpRequestException($"{url} returned an unexpected versionStudio response.");
        }

        return url;
    }

    public static string GetLocation(string resource)
    {
        if (string.IsNullOrEmpty(BaseUrl))
        {
            throw new InvalidOperationException("Connectivity has not been initialized.");
        }

        string location = $"{BaseUrl}/channel/common{resource}";

        Log.Write("Deployment::GetLocation", location);

        return location;
    }

    public static async Task<ClientVersion> GetInfoAsync(HttpClient http, string? channel, CancellationToken ct)
    {
        channel = string.IsNullOrWhiteSpace(channel) ? Channel : channel;

        bool isDefault = string.Equals(channel, DefaultChannel, StringComparison.OrdinalIgnoreCase);
        string cacheKey = $"{channel}-{BinaryType}";

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            Log.Write("Deployment::GetInfo", $"Reusing cached {cached.Version} ({cached.VersionGuid}) for {cacheKey}");
            return cached;
        }

        string path = isDefault
            ? $"/v2/client-version/{BinaryType}"
            : $"/v2/client-version/{BinaryType}/channel/{channel}";

        ClientVersion version;

        try
        {
            version = await GetJsonAsync(http, "https://clientsettingscdn.roblox.com" + path, channel, isDefault, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not InvalidChannelException)
        {
            Log.Write("Deployment::GetInfo", $"clientsettingscdn failed ({ex.Message}), falling back to clientsettings");

            version = await GetJsonAsync(http, "https://clientsettings.roblox.com" + path, channel, isDefault, ct)
                .ConfigureAwait(false);
        }

        Log.Write("Deployment::GetInfo", $"Channel {channel} is on {version.Version} ({version.VersionGuid})");

        Cache[cacheKey] = version;
        return version;
    }

    private static async Task<ClientVersion> GetJsonAsync(
        HttpClient http,
        string url,
        string channel,
        bool isDefault,
        CancellationToken ct)
    {
        Log.Write("Deployment::GetJson", $"Resolving the {channel} channel from {url}");

        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);

        if (!isDefault && BadChannelCodes.Contains(response.StatusCode))
        {
            Log.Write("Deployment::GetJson", $"{(int)response.StatusCode} {response.StatusCode} for the {channel} channel at {url}");
            throw new InvalidChannelException(channel, response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            await RobloxHttp.ThrowDescribedAsync(
                response,
                "Resolving the latest version",
                url,
                $"Roblox would not report a version for the {channel} channel.",
                ct).ConfigureAwait(false);
        }

        var version = await response.Content
            .ReadFromJsonAsync<ClientVersion>(cancellationToken: ct)
            .ConfigureAwait(false);

        if (version is null || string.IsNullOrEmpty(version.VersionGuid))
        {
            throw new HttpRequestException($"{url} returned an empty client version.");
        }

        return version;
    }
}
