using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Picks a game server by measured latency instead of letting Roblox choose.
///
/// How it works, and why it is shaped the way it is:
///
/// The public server list (games.roblox.com) is free and unauthenticated, but it gives only
/// job ids and player counts - no address, so nothing to measure. The address comes from
/// gamejoin.roblox.com/v1/join-game-instance, which needs the account's own login token and
/// is a real join request, not a lookup.
///
/// That endpoint is the whole constraint. Measured on this machine: the first probe
/// returned status 2 with a full joinScript and an address; after roughly six probes every
/// further one came back status 12, "Unable to join Game 311", and stayed that way for
/// minutes - at one probe every six seconds it never recovered. Roblox lets you have one
/// outstanding join at a time and refuses the rest.
///
/// So this does not do what Voidstrap's does. Probing a hundred servers is not possible;
/// the budget here is a handful, spaced out, with a long backoff the moment a refusal
/// arrives. Once an address is in hand the latency is genuinely measured (see ServerPing),
/// which is the part that is better - Voidstrap estimates it from distance on a map.
///
/// If the budget runs out before anything resolves, Jello says so and launches normally.
/// A quiet fallback to Roblox's own pick, dressed up as a result, would be worse than not
/// having the feature.
internal static class Matchmaker
{
    private const string ServerList = "https://games.roblox.com/v1/games";

    private const string JoinEndpoint = "https://gamejoin.roblox.com/v1/join-game-instance";

    /// Roblox's join status codes. 2 is a real answer; 22 means queued behind our own
    /// pending join; 12 is the refusal that arrives once we have used our allowance.
    private const int StatusSuccess = 2;
    private const int StatusQueued = 22;
    private const int StatusRefused = 12;

    private static readonly TimeSpan BetweenProbes = TimeSpan.FromMilliseconds(900);

    /// A refusal means the allowance is gone; there is no point trying again this launch.
    /// A refusal is not a short throttle. Measured here, once gamejoin started refusing
    /// it kept refusing for the better part of an hour, on every game tried. Retrying
    /// sooner only spends launches on requests that will not be answered.
    private static readonly TimeSpan RefusalBackoff = TimeSpan.FromMinutes(45);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan AddressCacheFor = TimeSpan.FromMinutes(2);

    private static DateTime _refusedUntil = DateTime.MinValue;

    private static readonly Dictionary<string, (string Address, DateTime WhenUtc)> Addresses = new();

    private static readonly object Gate = new();

    private static readonly HttpClient Http = CreateClient();

    private static string? _csrf;

    /// What the last launch actually did, so the settings page can report it without
    /// anyone pressing anything.
    public static Outcome? Last { get; private set; }

    private static HttpClient CreateClient()
    {
        // UseCookies off: the token is attached per request and never kept in a container.
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };

        return new HttpClient(handler) { Timeout = ProbeTimeout };
    }

    internal sealed record Candidate(string JobId, int Playing, int MaxPlayers)
    {
        public string? Address { get; set; }

        public int? Ping { get; set; }

        public bool Resolved => Address is not null && Ping is not null;
    }

    internal sealed record Outcome(
        Candidate? Winner,
        int Listed,
        int Probed,
        int Measured,
        string Summary)
    {
        public bool Found => Winner is { Resolved: true };
    }

    private sealed class ServerListResponse
    {
        [JsonPropertyName("data")]
        public List<ServerEntry> Data { get; set; } = new();
    }

    private sealed class ServerEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("playing")]
        public int Playing { get; set; }

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; }
    }

    /// Finds the best server it can within the probe budget.
    public static async Task<Outcome> FindAsync(
        long placeId, int budget, bool preferEmptier, IProgress<string>? progress, CancellationToken token)
    {
        const string ident = "Matchmaker::Find";

        if (placeId <= 0)
        {
            return Record(new Outcome(null, 0, 0, 0, "No place to search."));
        }

        if (DateTime.UtcNow < _refusedUntil)
        {
            var left = _refusedUntil - DateTime.UtcNow;

            return Record(new Outcome(null, 0, 0, 0,
                $"Roblox is still refusing server lookups for another {left.TotalMinutes:0} minute(s). Launching normally."));
        }

        string? session = RobloxSession.Read();

        if (string.IsNullOrEmpty(session))
        {
            return Record(new Outcome(null, 0, 0, 0,
                "No Roblox login found on this machine, so servers cannot be looked up."));
        }

        progress?.Report("Listing servers...");

        var listed = await ListAsync(placeId, token);

        if (listed.Count == 0)
        {
            return Record(new Outcome(null, 0, 0, 0, "Roblox listed no joinable servers for this game."));
        }

        Log.Write(ident, $"{listed.Count} server(s) listed for place {placeId}, probing up to {budget}");

        // Emptier servers first when asked, otherwise fuller ones, which is what Roblox
        // itself favours and what most people want.
        var ordered = preferEmptier
            ? listed.OrderBy(c => c.Playing).ToList()
            : listed.OrderByDescending(c => c.Playing).ToList();

        var measured = new List<Candidate>();

        int probed = 0;

        foreach (var candidate in ordered)
        {
            if (token.IsCancellationRequested || probed >= budget)
            {
                break;
            }

            progress?.Report($"Checking server {probed + 1} of {budget}...");

            string? address = Known(candidate.JobId) ?? await ResolveAsync(placeId, candidate.JobId, session, token);

            probed++;

            if (address is null)
            {
                if (DateTime.UtcNow < _refusedUntil)
                {
                    Log.Write(ident, "Roblox refused a lookup, stopping here");
                    break;
                }

                await Task.Delay(BetweenProbes, token);
                continue;
            }

            candidate.Address = address;
            candidate.Ping = await ServerPing.MeasureAsync(address, token);

            if (candidate.Ping is not null)
            {
                measured.Add(candidate);
                Log.Write(ident, $"{candidate.JobId} at {address} measured {candidate.Ping} ms");
            }

            await Task.Delay(BetweenProbes, token);
        }

        if (measured.Count == 0)
        {
            return Record(new Outcome(null, listed.Count, probed, 0,
                probed == 0
                    ? "No servers could be checked. Launching normally."
                    : $"Roblox would not give an address for any of the {probed} server(s) checked. Launching normally."));
        }

        var winner = measured.OrderBy(c => c.Ping!.Value).First();

        string summary = measured.Count == 1
            ? $"Checked 1 server: {winner.Ping} ms, {winner.Playing} of {winner.MaxPlayers} players."
            : $"Checked {measured.Count} of {probed}: best {winner.Ping} ms, worst {measured.Max(c => c.Ping!.Value)} ms.";

        Log.Write(ident, $"Winner {winner.JobId} at {winner.Ping} ms out of {measured.Count} measured");

        return Record(new Outcome(winner, listed.Count, probed, measured.Count, summary));
    }

    private static Outcome Record(Outcome outcome)
    {
        Last = outcome;
        return outcome;
    }

    private static async Task<List<Candidate>> ListAsync(long placeId, CancellationToken token)
    {
        const string ident = "Matchmaker::List";

        try
        {
            // limit only accepts 10, 25, 50 or 100.
            var response = await Http.GetFromJsonAsync<ServerListResponse>(
                $"{ServerList}/{placeId}/servers/Public?excludeFullGames=true&limit=100&sortOrder=Desc",
                token);

            return response?.Data
                .Where(entry => !string.IsNullOrEmpty(entry.Id) && entry.Playing < entry.MaxPlayers)
                .Select(entry => new Candidate(entry.Id, entry.Playing, entry.MaxPlayers))
                .ToList() ?? new List<Candidate>();
        }
        catch (Exception ex)
        {
            Log.Write(ident, $"Could not list servers for {placeId}: {ex.Message}");
            return new List<Candidate>();
        }
    }

    private static string? Known(string jobId)
    {
        lock (Gate)
        {
            return Addresses.TryGetValue(jobId, out var cached) && DateTime.UtcNow - cached.WhenUtc < AddressCacheFor
                ? cached.Address
                : null;
        }
    }

    /// One join request, which is what makes an address available. Two attempts at most,
    /// the second only to pick up a CSRF token.
    private static async Task<string?> ResolveAsync(
        long placeId, string jobId, string session, CancellationToken token)
    {
        const string ident = "Matchmaker::Resolve";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = Build(placeId, jobId, session);
                using var response = await Http.SendAsync(request, token);

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    && response.Headers.TryGetValues("x-csrf-token", out var values))
                {
                    _csrf = values.FirstOrDefault();
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _refusedUntil = DateTime.UtcNow + RefusalBackoff;
                    Log.Write(ident, "Roblox rate limited the lookup");

                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Log.Write(ident, $"Lookup returned {(int)response.StatusCode}");
                    return null;
                }

                string body = await response.Content.ReadAsStringAsync(token);

                return Parse(body, jobId, ident);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Write(ident, $"Lookup of {jobId} failed: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    private static HttpRequestMessage Build(long placeId, string jobId, string session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, JoinEndpoint);

        // The token goes on this one request, to this one host, and nowhere else.
        request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={session}");
        request.Headers.Referrer = new Uri("https://www.roblox.com/");

        if (!string.IsNullOrEmpty(_csrf))
        {
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrf);
        }

        string payload = JsonSerializer.Serialize(new
        {
            placeId,
            gameId = jobId,
            gameJoinAttemptId = Guid.NewGuid().ToString()
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        return request;
    }

    /// The address lives under joinScript, either as a UdmuxEndpoints entry or as a plain
    /// MachineAddress depending on how the server is fronted.
    private static string? Parse(string body, string jobId, string ident)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            int status = root.TryGetProperty("status", out var s) && s.TryGetInt32(out int value) ? value : 0;

            if (status == StatusRefused)
            {
                _refusedUntil = DateTime.UtcNow + RefusalBackoff;

                Log.Write(ident,
                    $"Roblox refused the lookup for {jobId}; no more will be tried for {RefusalBackoff.TotalMinutes:0} minutes");

                return null;
            }

            if (status == StatusQueued)
            {
                Log.Write(ident, $"{jobId} is queued behind our own pending join, skipping it");
                return null;
            }

            if (status != StatusSuccess || !root.TryGetProperty("joinScript", out var script)
                || script.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? address = null;

            if (script.TryGetProperty("UdmuxEndpoints", out var endpoints)
                && endpoints.ValueKind == JsonValueKind.Array
                && endpoints.GetArrayLength() > 0
                && endpoints[0].TryGetProperty("Address", out var first))
            {
                address = first.GetString();
            }

            if (string.IsNullOrEmpty(address)
                && script.TryGetProperty("MachineAddress", out var machine))
            {
                address = machine.GetString();
            }

            if (string.IsNullOrEmpty(address) || address.StartsWith("10.", StringComparison.Ordinal))
            {
                return null;
            }

            lock (Gate)
            {
                Addresses[jobId] = (address, DateTime.UtcNow);
            }

            return address;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Describe()
    {
        if (!AppState.Settings.MatchmakerEnabled)
        {
            return "Off. Roblox picks the server, as it normally does.";
        }

        if (!RobloxSession.Available)
        {
            return "On, but no Roblox login was found on this machine, so it cannot look servers up.";
        }

        if (DateTime.UtcNow < _refusedUntil)
        {
            return $"On, but Roblox is refusing lookups for another {(_refusedUntil - DateTime.UtcNow).TotalMinutes:0} minute(s).";
        }

        return $"On. Checks up to {AppState.Settings.MatchmakerBudget} server(s) before each launch and joins the fastest.";
    }
}
