using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// A community offset dump: the address, relative to the client's module base, of every
/// FastFlag variable in one specific Roblox build.
///
/// These are published per build by third parties. The build they were dumped from is in
/// the file header, and this refuses to hand back anything whose header does not match the
/// exact version we installed - an offset from a different build points at an unrelated
/// piece of memory, which is how live patching crashes a client.
internal sealed partial class OffsetDump
{
    private const int MaximumBytes = 16 * 1024 * 1024;

    private const uint MinimumRva = 0x100000;

    private const uint MaximumRva = 0x10000000;

    /// Three different hosts, all of which stamp the build they were dumped from. Sources
    /// that publish no stamp are deliberately not listed: without one there is no way to
    /// prove the addresses belong to the build being run, and a mismatched dump points at
    /// unrelated memory.
    private static readonly string[] Sources =
    {
        "https://offsets.imtheo.lol/FFlags.hpp",
        "https://raw.githubusercontent.com/4anti/Roblox-Fastflag-Manager/main/data/FFlags.hpp",
        "https://raw.githubusercontent.com/souloveryall/offsets.hpp/main/Offsets.hpp"
    };

    private static readonly string[] StructNames = { "Pointer", "ToFlag", "ToValue", "ValueGetSet", "FlagToValue" };

    public required string BuildVersion { get; init; }

    public required string Source { get; init; }

    public required IReadOnlyDictionary<string, uint> Offsets { get; init; }

    [GeneratedRegex(@"ClientVersion\s*=\s*""(version-[0-9a-fA-F]+)""")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"Roblox Version\s*:?\s*(version-[0-9a-fA-F]+)")]
    private static partial Regex HeaderVersionPattern();

    [GeneratedRegex(@"uintptr_t\s+([A-Za-z_][A-Za-z0-9_]{0,127})\s*=\s*0x([0-9a-fA-F]{1,8})")]
    private static partial Regex OffsetPattern();

    private static string CacheFile(string versionGuid) =>
        Path.Combine(Paths.Base, "Offsets", $"{versionGuid}.json");

    /// The dump for this exact build, from disk if it was fetched before, otherwise from
    /// the network. Null means no source has published offsets for this build yet.
    public static async Task<OffsetDump?> ForVersionAsync(
        HttpClient http,
        string versionGuid,
        CancellationToken ct)
    {
        const string ident = "OffsetDump::ForVersion";

        if (FromOverride(versionGuid) is { } overridden)
        {
            Log.Write(ident, $"Using pasted FastFlag offsets ({overridden.Offsets.Count} offsets) for {versionGuid}");
            return overridden;
        }

        if (Load(versionGuid) is { } cached)
        {
            Log.Write(ident, $"Using the cached dump for {versionGuid} ({cached.Offsets.Count} offsets)");
            return cached;
        }

        foreach (string source in Sources)
        {
            try
            {
                var dump = await FetchAsync(http, source, versionGuid, ct).ConfigureAwait(false);

                if (dump is null)
                {
                    continue;
                }

                Save(dump, versionGuid);

                Log.Write(ident, $"{source} published {dump.Offsets.Count} offsets for {versionGuid}");

                return dump;
            }
            catch (Exception ex)
            {
                Log.Write(ident, $"{source} failed: {ex.Message}");
            }
        }

        Log.Write(ident, $"No source has offsets for {versionGuid}");

        return null;
    }

    /// A pasted table wins over any fetch, so live flags can be pointed at a build no public
    /// source has dumped yet. Unlike a fetch, the build stamp is not enforced here - a paste
    /// is the person's explicit choice - but the settings page still flags a mismatch.
    private static OffsetDump? FromOverride(string versionGuid)
    {
        if (!AppState.Settings.OffsetsOverrideEnabled
            || string.IsNullOrWhiteSpace(AppState.Settings.FlagOffsetsOverrideJson))
        {
            return null;
        }

        var (offsets, version) = ParseJson(AppState.Settings.FlagOffsetsOverrideJson);

        if (offsets is null || offsets.Count == 0)
        {
            Log.Write("OffsetDump::Override", "The pasted FastFlag offsets could not be read; fetching instead.");
            return null;
        }

        return new OffsetDump
        {
            BuildVersion = version ?? versionGuid,
            Source = "your pasted offsets",
            Offsets = offsets
        };
    }

    /// Reads a pasted table and says whether it is usable and current for the build
    /// installed now. Touches no process; the settings page calls it as the box is typed in.
    public static OffsetOverride.Check Inspect(string? json, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OffsetOverride.Empty("The FastFlag offsets");
        }

        Dictionary<string, uint>? offsets;
        string? version;

        try
        {
            (offsets, version) = ParseJson(json);
        }
        catch (Exception ex)
        {
            return OffsetOverride.Invalid($"That is not valid JSON: {ex.Message}");
        }

        if (offsets is null || offsets.Count == 0)
        {
            return OffsetOverride.Invalid("No usable \"Offsets\" were found in that JSON.");
        }

        return OffsetOverride.Judge("the FastFlag offsets", $"{offsets.Count} offset(s)", version, installedVersion);
    }

    /// The flat name to address map from a pasted string, in the shape the dump publishes:
    /// an object with an "Offsets" object of name to number, and an optional build stamp
    /// under "BuildVersion" or "Roblox Version".
    private static (Dictionary<string, uint>? Offsets, string? Version) ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Offsets", out var table)
            || table.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        string? version =
            root.TryGetProperty("BuildVersion", out var build) && build.ValueKind == JsonValueKind.String
                ? build.GetString()
                : root.TryGetProperty("Roblox Version", out var stamp) && stamp.ValueKind == JsonValueKind.String
                    ? stamp.GetString()
                    : null;

        var offsets = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (var field in table.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.Number
                && field.Value.TryGetInt64(out long value)
                && value is >= MinimumRva and <= MaximumRva)
            {
                offsets[field.Name] = (uint)value;
            }
        }

        return (offsets.Count == 0 ? null : offsets, version);
    }

    private static async Task<OffsetDump?> FetchAsync(
        HttpClient http,
        string source,
        string versionGuid,
        CancellationToken ct)
    {
        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumBytes)
        {
            throw new InvalidOperationException("The dump is larger than the size cap.");
        }

        byte[] payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        if (payload.Length > MaximumBytes)
        {
            throw new InvalidOperationException("The dump is larger than the size cap.");
        }

        string text = System.Text.Encoding.UTF8.GetString(payload);

        string? published = VersionPattern().Match(text) is { Success: true } match
            ? match.Groups[1].Value
            : HeaderVersionPattern().Match(text) is { Success: true } header
                ? header.Groups[1].Value
                : null;

        if (published is null)
        {
            Log.Write("OffsetDump::Fetch",
                $"{source} carries no build stamp, so there is no way to prove it matches {versionGuid}. Skipped.");

            return null;
        }

        if (!string.Equals(published, versionGuid, StringComparison.OrdinalIgnoreCase))
        {
            Log.Write("OffsetDump::Fetch", $"{source} is dumped from {published}, we run {versionGuid}, skipping it");
            return null;
        }

        return new OffsetDump
        {
            BuildVersion = published,
            Source = source,
            Offsets = Parse(text)
        };
    }

    private static Dictionary<string, uint> Parse(string text)
    {
        var offsets = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (Match match in OffsetPattern().Matches(text))
        {
            string name = match.Groups[1].Value;

            if (StructNames.Contains(name))
            {
                continue;
            }

            if (!uint.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rva))
            {
                continue;
            }

            if (rva is < MinimumRva or > MaximumRva)
            {
                continue;
            }

            offsets[name] = rva;
        }

        return offsets;
    }

    private static OffsetDump? Load(string versionGuid)
    {
        try
        {
            string path = CacheFile(versionGuid);

            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredDump>(File.ReadAllText(path));

            if (stored is null
                || !string.Equals(stored.BuildVersion, versionGuid, StringComparison.OrdinalIgnoreCase)
                || stored.Offsets.Count == 0)
            {
                return null;
            }

            return new OffsetDump
            {
                BuildVersion = stored.BuildVersion,
                Source = stored.Source,
                Offsets = stored.Offsets
            };
        }
        catch (Exception ex)
        {
            Log.Write("OffsetDump::Load", $"Could not read the cached dump: {ex.Message}");
            return null;
        }
    }

    private static void Save(OffsetDump dump, string versionGuid)
    {
        try
        {
            string path = CacheFile(versionGuid);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(new StoredDump
            {
                BuildVersion = dump.BuildVersion,
                Source = dump.Source,
                Offsets = dump.Offsets.ToDictionary(pair => pair.Key, pair => pair.Value)
            }));
        }
        catch (Exception ex)
        {
            Log.Write("OffsetDump::Save", $"Could not cache the dump: {ex.Message}");
        }
    }

    private sealed class StoredDump
    {
        public string BuildVersion { get; set; } = "";

        public string Source { get; set; } = "";

        public Dictionary<string, uint> Offsets { get; set; } = new();
    }
}
