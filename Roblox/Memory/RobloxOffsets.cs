using System.Net.Http;
using System.Text.Json;
using JelloClient.Services;

namespace JelloClient.Roblox.Memory;

/// The per-build memory layout for the running client, from the community offset dump.
///
/// Roblox's object layout shifts between builds, so the offsets are published per version
/// and fetched by the build id - the same id that names the version folder. The result is
/// cached, because it does not change for a given build and there is no reason to fetch it
/// twice.
///
/// This is the same data the Lab's Python walker used while it was being worked out; here
/// it is read in the app so the walk can run natively.
internal sealed class RobloxOffsets
{
    private const string Endpoint = "https://offsets.imtheo.lol";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Dictionary<string, Dictionary<string, long>> _tables;

    public string Version { get; }

    private RobloxOffsets(string version, Dictionary<string, Dictionary<string, long>> tables)
    {
        Version = version;
        _tables = tables;
    }

    /// One field, e.g. Get("Instance", "Name"). Missing entries come back as 0, which is
    /// exactly how the dump itself marks a field it did not record, so callers already have
    /// to treat 0 as "not available".
    public long Get(string group, string field) =>
        _tables.TryGetValue(group, out var table) && table.TryGetValue(field, out long value)
            ? value
            : 0;

    public bool Has(string group) => _tables.ContainsKey(group);

    private static string CachePath(string version) =>
        Path.Combine(Paths.Base, "Offsets", $"{version}.memory.json");

    /// A pasted table wins over anything fetched, so the Lab can be pointed at a build the
    /// public dump has not caught up with yet. An empty or unparseable box falls through to
    /// the normal fetch rather than failing the attach.
    private static RobloxOffsets? FromOverride(string version)
    {
        if (!AppState.Settings.OffsetsOverrideEnabled
            || string.IsNullOrWhiteSpace(AppState.Settings.MemoryOffsetsOverrideJson))
        {
            return null;
        }

        var tables = TryParse(AppState.Settings.MemoryOffsetsOverrideJson);

        if (tables is null || tables.Count == 0)
        {
            Log.Write("RobloxOffsets::Override", "The pasted memory offsets could not be read; fetching instead.");
            return null;
        }

        Log.Write("RobloxOffsets::Override", $"Using pasted memory offsets ({tables.Count} group(s)) for {version}");

        return new RobloxOffsets(version, tables);
    }

    /// Reads a pasted table and says whether it is usable and whether it looks current for
    /// the build installed now. Touches no process; the settings page calls it as the box
    /// is typed into.
    public static OffsetOverride.Check Inspect(string? json, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OffsetOverride.Empty("The memory layout");
        }

        string? version;
        Dictionary<string, Dictionary<string, long>>? tables;

        try
        {
            using var document = JsonDocument.Parse(json);

            version = document.RootElement.TryGetProperty("Roblox Version", out var stamp)
                      && stamp.ValueKind == JsonValueKind.String
                ? stamp.GetString()
                : null;

            tables = ParseElement(document.RootElement);
        }
        catch (Exception ex)
        {
            return OffsetOverride.Invalid($"That is not valid JSON: {ex.Message}");
        }

        if (tables is null || tables.Count == 0)
        {
            return OffsetOverride.Invalid("No \"Offsets\" groups were found in that JSON.");
        }

        int fields = tables.Sum(table => table.Value.Count);

        return OffsetOverride.Judge("the memory layout", $"{tables.Count} group(s), {fields} field(s)", version, installedVersion);
    }

    /// The grouped table from a pasted string, or null if it is not the expected shape.
    public static Dictionary<string, Dictionary<string, long>>? TryParse(string json)
    {
        try
        {
            return Parse(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<RobloxOffsets?> LoadAsync(string version, CancellationToken token = default)
    {
        const string ident = "RobloxOffsets::Load";

        if (FromOverride(version) is { } overridden)
        {
            return overridden;
        }

        string cache = CachePath(version);

        try
        {
            if (File.Exists(cache))
            {
                var cached = Parse(await File.ReadAllTextAsync(cache, token));

                if (cached is not null)
                {
                    return new RobloxOffsets(version, cached);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write(ident, $"Cached offsets for {version} were unusable: {ex.Message}");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/{version}/offsets.json");
            request.Headers.TryAddWithoutValidation("User-Agent", "JelloClient");

            using var response = await Http.SendAsync(request, token);

            if (!response.IsSuccessStatusCode)
            {
                Log.Write(ident, $"No offsets published for {version} ({(int)response.StatusCode})");
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(token);

            var tables = Parse(body);

            if (tables is null)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            await File.WriteAllTextAsync(cache, body, token);

            Log.Write(ident, $"Fetched and cached offsets for {version}");

            return new RobloxOffsets(version, tables);
        }
        catch (Exception ex)
        {
            Log.Write(ident, $"Could not fetch offsets for {version}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, Dictionary<string, long>>? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseElement(document.RootElement);
    }

    private static Dictionary<string, Dictionary<string, long>>? ParseElement(JsonElement root)
    {
        if (!root.TryGetProperty("Offsets", out var offsets)
            || offsets.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tables = new Dictionary<string, Dictionary<string, long>>();

        foreach (var group in offsets.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var table = new Dictionary<string, long>();

            foreach (var field in group.Value.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt64(out long value))
                {
                    table[field.Name] = value;
                }
            }

            tables[group.Name] = table;
        }

        return tables;
    }
}
