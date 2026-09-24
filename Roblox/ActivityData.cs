using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal enum ServerType
{
    Public,
    Private,
    Reserved
}

internal sealed class IpInfoResponse
{
    [JsonPropertyName("city")]
    public string City { get; set; } = "";

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";

    [JsonPropertyName("country")]
    public string Country { get; set; } = "";

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("org")]
    public string? Org { get; set; }
}

internal sealed record ServerDetails(string? Location, string? Hostname, string? Provider);

internal sealed class ActivityData
{
    private static readonly Dictionary<string, string?> LocationCache = new();

    public long PlaceId { get; set; }

    public string JobId { get; set; } = "";

    public long UniverseId { get; set; }

    public string MachineAddress { get; set; } = "";

    public int Port { get; set; }

    public string? Hostname { get; private set; }

    public string? Provider { get; private set; }

    /// Everyone in the experience right now, across every one of its servers. This is not
    /// the number of people in the server being played on: Roblox's log does not carry
    /// that, and the games API only reports the experience-wide figure.
    public int PlayerCount { get; set; } = -1;

    /// The most one server of this experience can hold. Not comparable with PlayerCount
    /// above - showing them together as "54 of 8" is how that mistake looks.
    public int MaxPlayers { get; set; } = -1;

    public string? UniverseName { get; set; }

    public ServerType ServerType { get; set; } = ServerType.Public;

    public DateTime TimeJoined { get; set; }

    public string LaunchData { get; set; } = "";

    public string? ServerLocation { get; private set; }

    public bool MachineAddressValid =>
        !string.IsNullOrEmpty(MachineAddress) && !MachineAddress.StartsWith("10.", StringComparison.Ordinal);

    public string ServerTypeLabel => ServerType switch
    {
        ServerType.Private => "Private server",
        ServerType.Reserved => "Reserved server",
        _ => "Public server"
    };

    public string GameUrl => $"https://www.roblox.com/games/{PlaceId}";

    public string GetInviteDeeplink(bool includeLaunchData = true)
    {
        string deeplink = $"roblox://experiences/start?placeId={PlaceId}";

        deeplink += "&gameInstanceId=" + JobId;

        if (includeLaunchData && !string.IsNullOrEmpty(LaunchData))
        {
            deeplink += "&launchData=" + Uri.EscapeDataString(LaunchData);
        }

        return deeplink;
    }

    public string Endpoint => Port > 0 ? $"{MachineAddress}:{Port}" : MachineAddress;

    public string GetClipboardSummary()
    {
        var lines = new List<string>();

        if (!string.IsNullOrEmpty(UniverseName))
        {
            lines.Add($"Experience: {UniverseName}");
        }

        lines.Add($"Place ID: {PlaceId}");

        if (UniverseId != 0)
        {
            lines.Add($"Universe ID: {UniverseId}");
        }

        lines.Add($"Job ID: {JobId}");
        lines.Add($"Server: {ServerTypeLabel}");

        if (!string.IsNullOrEmpty(MachineAddress))
        {
            lines.Add($"Address: {Endpoint}");
        }

        if (!string.IsNullOrEmpty(Hostname))
        {
            lines.Add($"Hostname: {Hostname}");
        }

        if (!string.IsNullOrEmpty(ServerLocation))
        {
            lines.Add($"Location: {ServerLocation}");
        }

        if (!string.IsNullOrEmpty(Provider))
        {
            lines.Add($"Provider: {Provider}");
        }

        if (PlayerCount >= 0)
        {
            lines.Add($"Players: {PlayerCount}{(MaxPlayers > 0 ? $"/{MaxPlayers}" : "")}");
        }

        lines.Add($"Game page: {GameUrl}");
        lines.Add($"Join link: {GetInviteDeeplink()}");
        lines.Add($"Teleport: game:GetService(\"TeleportService\"):TeleportToPlaceInstance({PlaceId}, \"{JobId}\", game.Players.LocalPlayer)");

        return string.Join(Environment.NewLine, lines);
    }

    /// ipinfo.io is a third party over the internet, and the panel should not sit on
    /// "Looking up..." while it thinks about it.
    private static readonly TimeSpan LocationTimeout = TimeSpan.FromSeconds(5);

    /// Reverse DNS is the slow half. Roblox's own servers mostly have no PTR record at
    /// all, so this waits a short time and then says so rather than hanging.
    private static readonly TimeSpan HostnameTimeout = TimeSpan.FromSeconds(2);

    private static readonly Dictionary<string, string?> HostnameCache = new();

    /// The location and the hostname are looked up separately, because they used to share
    /// one call and one lock: a slow reverse DNS held up the city, and a second panel
    /// asking at the same time waited behind the first. Now the city lands as soon as it
    /// is known and the hostname catches up on its own.
    public async Task<string?> QueryServerLocationAsync(HttpClient http, CancellationToken ct)
    {
        if (!MachineAddressValid)
        {
            return null;
        }

        string address = MachineAddress;

        lock (LocationCache)
        {
            if (LocationCache.TryGetValue(address, out string? cached))
            {
                ServerLocation = cached;

                if (HostnameCache.TryGetValue(address, out string? name))
                {
                    Hostname = name;
                }

                return cached;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LocationTimeout);

        try
        {
            var info = await http
                .GetFromJsonAsync<IpInfoResponse>($"https://ipinfo.io/{address}/json", timeout.Token)
                .ConfigureAwait(false);

            if (info is null || string.IsNullOrEmpty(info.City))
            {
                throw new HttpRequestException("ipinfo.io reported a blank city.");
            }

            string location = info.City == info.Region
                ? $"{info.Region}, {info.Country}"
                : $"{info.City}, {info.Region}, {info.Country}";

            lock (LocationCache)
            {
                LocationCache[address] = location;
            }

            ServerLocation = location;
            Provider = info.Org;

            if (!string.IsNullOrEmpty(info.Hostname))
            {
                Hostname = info.Hostname;

                lock (LocationCache)
                {
                    HostnameCache[address] = info.Hostname;
                }
            }

            Log.Write("ActivityData::QueryServerLocation", $"{address} resolved to {location} ({info.Org})");

            return location;
        }
        catch (Exception ex)
        {
            Log.Write("ActivityData::QueryServerLocation",
                $"Could not resolve {address}: {(ex is OperationCanceledException ? $"gave up after {LocationTimeout.TotalSeconds:0}s" : ex.Message)}");

            lock (LocationCache)
            {
                LocationCache[address] = null;
            }

            return null;
        }
    }

    /// Reverse DNS, on its own short leash. Most Roblox machines publish no name, so a
    /// null answer here is the ordinary case and not a failure.
    public async Task<string?> QueryHostnameAsync(CancellationToken ct)
    {
        if (!MachineAddressValid)
        {
            return null;
        }

        string address = MachineAddress;

        lock (LocationCache)
        {
            if (HostnameCache.TryGetValue(address, out string? cached))
            {
                Hostname = cached;
                return cached;
            }
        }

        string? name = null;

        try
        {
            var lookup = System.Net.Dns.GetHostEntryAsync(address, ct);

            // GetHostEntryAsync ignores the token on some stacks, so the wait is bounded
            // here as well and the lookup is simply abandoned if it overruns.
            if (await Task.WhenAny(lookup, Task.Delay(HostnameTimeout, ct)).ConfigureAwait(false) == lookup)
            {
                name = (await lookup.ConfigureAwait(false)).HostName;

                if (string.Equals(name, address, StringComparison.Ordinal))
                {
                    // The resolver handed the address straight back: there is no name.
                    name = null;
                }
            }
            else
            {
                Log.Write("ActivityData::QueryHostname", $"{address} has no answer within {HostnameTimeout.TotalSeconds:0}s");
            }
        }
        catch (Exception ex)
        {
            Log.Write("ActivityData::QueryHostname", $"{address}: {ex.Message}");
        }

        lock (LocationCache)
        {
            HostnameCache[address] = name;
        }

        Hostname = name;

        return name;
    }

    public override string ToString() => $"{PlaceId}/{JobId}";
}
