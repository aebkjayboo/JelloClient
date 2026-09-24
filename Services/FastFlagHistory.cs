using System.Text.Json;
using System.Text.Json.Serialization;
using JelloClient.Roblox;

namespace JelloClient.Services;

internal sealed class FastFlagSnapshot
{
    [JsonPropertyName("takenUtc")]
    public DateTime TakenUtc { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("flags")]
    public Dictionary<string, string> Flags { get; set; } = new();

    [JsonIgnore]
    public string Taken => TakenUtc.ToLocalTime().ToString("d MMM HH:mm:ss");
}

internal sealed record FlagChange(string Flag, string? Before, string? After)
{
    public string Kind => Before is null ? "added" : After is null ? "removed" : "changed";

    public string Summary => Before is null
        ? $"+ {Flag} = {After}"
        : After is null
            ? $"- {Flag}"
            : $"~ {Flag}: {Before} -> {After}";
}

/// Keeps a rolling record of every change made to the flag set, so a change can be
/// inspected and rolled back. Each entry stores the full set, which makes reverting
/// a straight restore rather than an inverse-diff.
internal static class FastFlagHistory
{
    private const int MaxEntries = 60;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string FilePath => Path.Combine(Paths.Base, "FastFlagHistory.json");

    public static List<FastFlagSnapshot> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new List<FastFlagSnapshot>();
            }

            return JsonSerializer.Deserialize<List<FastFlagSnapshot>>(File.ReadAllText(FilePath), Options)
                ?? new List<FastFlagSnapshot>();
        }
        catch (Exception ex)
        {
            Log.WriteException("FastFlagHistory::Load", ex);
            return new List<FastFlagSnapshot>();
        }
    }

    private static void Save(List<FastFlagSnapshot> entries)
    {
        try
        {
            Paths.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, Options));
        }
        catch (Exception ex)
        {
            Log.WriteException("FastFlagHistory::Save", ex);
        }
    }

    public static Dictionary<string, string> Flatten(IReadOnlyDictionary<string, JsonElement> flags) =>
        flags.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ValueKind == JsonValueKind.String
                ? pair.Value.GetString() ?? ""
                : pair.Value.ToString());

    public static void Record(string reason, IReadOnlyDictionary<string, JsonElement> flags)
    {
        var entries = Load();
        var current = Flatten(flags);

        if (entries.Count > 0 && SameAs(entries[0].Flags, current))
        {
            return;
        }

        entries.Insert(0, new FastFlagSnapshot
        {
            TakenUtc = DateTime.UtcNow,
            Reason = reason,
            Flags = current
        });

        while (entries.Count > MaxEntries)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        Save(entries);

        Log.Write("FastFlagHistory::Record", $"{reason} ({current.Count} flag(s)), {entries.Count} entries kept");
    }

    private static bool SameAs(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? v) && v == pair.Value);

    public static IReadOnlyList<FlagChange> Diff(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var changes = new List<FlagChange>();

        foreach (var pair in after)
        {
            if (!before.TryGetValue(pair.Key, out string? old))
            {
                changes.Add(new FlagChange(pair.Key, null, pair.Value));
            }
            else if (old != pair.Value)
            {
                changes.Add(new FlagChange(pair.Key, old, pair.Value));
            }
        }

        foreach (var pair in before)
        {
            if (!after.ContainsKey(pair.Key))
            {
                changes.Add(new FlagChange(pair.Key, pair.Value, null));
            }
        }

        return changes.OrderBy(change => change.Flag, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            Log.Write("FastFlagHistory::Clear", "History cleared");
        }
        catch (Exception ex)
        {
            Log.WriteException("FastFlagHistory::Clear", ex);
        }
    }
}
