using System.Text.Json;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed record FlagProblem(string Flag, string Kind, string Detail, string? Suggestion);

/// Writes ClientSettings\ClientAppSettings.json the way the client actually reads it,
/// then proves the write landed.
///
/// Roblox parses this file once, at process start, so a silently failed or half-flushed
/// write shows up as "my flags did nothing" with no error anywhere. The Roblox FastFlag
/// Manager reference handles that by writing, fsyncing, reading the file back, comparing
/// every expected key, retrying once, and refusing to launch on a second miss. That part
/// is worth copying exactly; its other half (writing flags into the live client's memory
/// through offsets and syscalls) is not - see the notes in the docs folder.
internal static class FastFlagWriter
{
    private const string SettingsDirectoryName = "ClientSettings";
    private const string SettingsFileName = "ClientAppSettings.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string PathFor(string versionDirectory) =>
        Path.Combine(versionDirectory, SettingsDirectoryName, SettingsFileName);

    /// Coerces every value to the string form Roblox expects for that prefix.
    /// A boolean flag holding JSON true, 1 or "yes" all become "True".
    public static Dictionary<string, string> Normalise(IReadOnlyDictionary<string, object> flags)
    {
        var normalised = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in flags)
        {
            string name = pair.Key.Trim();

            if (name.Length == 0)
            {
                continue;
            }

            normalised[name] = NormaliseValue(name, Stringify(pair.Value));
        }

        return normalised;
    }

    private static string Stringify(object? value) => value switch
    {
        null => "",
        bool flag => flag ? "True" : "False",
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "",
            _ => element.ToString()
        },
        _ => value.ToString() ?? ""
    };

    private static string NormaliseValue(string flag, string value)
    {
        string trimmed = value.Trim();

        if (FastFlagTypes.KindOf(flag) != FlagValueKind.Boolean)
        {
            return trimmed;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => "True",
            "false" or "0" or "no" or "off" => "False",
            _ => trimmed
        };
    }

    /// Writes the file, flushes it to disk, reads it back and checks every key survived.
    /// One retry, then it gives up and says so - the caller decides whether that blocks
    /// the launch.
    public static bool WriteVerified(string versionDirectory, IReadOnlyDictionary<string, object> flags)
    {
        const string ident = "FastFlagWriter::WriteVerified";

        var wanted = Normalise(flags);

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Write(versionDirectory, wanted);

                var missing = Verify(versionDirectory, wanted);

                if (missing.Count == 0)
                {
                    Log.Write(ident, $"{wanted.Count} flag(s) written and verified in {versionDirectory}");
                    return true;
                }

                Log.Write(ident,
                    $"Attempt {attempt}: {missing.Count} flag(s) did not survive the write ({string.Join(", ", missing.Take(3))})");
            }
            catch (Exception ex)
            {
                Log.Write(ident, $"Attempt {attempt} failed: {ex.Message}");
            }
        }

        Log.Write(ident, $"Could not write the flags into {versionDirectory}");

        return false;
    }

    private static void Write(string versionDirectory, Dictionary<string, string> flags)
    {
        string directory = Path.Combine(versionDirectory, SettingsDirectoryName);

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, SettingsFileName);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(flags, WriteOptions);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        stream.Write(payload);
        stream.Flush(true);
    }

    /// The keys that are missing or hold the wrong value after reading the file back.
    public static List<string> Verify(string versionDirectory, IReadOnlyDictionary<string, string> wanted)
    {
        string path = PathFor(versionDirectory);

        Dictionary<string, JsonElement>? onDisk;

        try
        {
            onDisk = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Write("FastFlagWriter::Verify", $"Could not read {path} back: {ex.Message}");
            return wanted.Keys.ToList();
        }

        if (onDisk is null)
        {
            return wanted.Keys.ToList();
        }

        return wanted
            .Where(pair => !onDisk.TryGetValue(pair.Key, out var element)
                           || Stringify(element) != pair.Value)
            .Select(pair => pair.Key)
            .ToList();
    }

    /// Malformed entries the editor should offer to fix: names carrying a prefix Roblox
    /// does not use, and values that do not match the type their prefix implies.
    public static List<FlagProblem> Inspect(IReadOnlyDictionary<string, JsonElement> flags)
    {
        var problems = new List<FlagProblem>();

        foreach (var pair in flags)
        {
            string name = pair.Key;
            string value = Stringify(pair.Value);

            if (name != name.Trim())
            {
                problems.Add(new FlagProblem(name, "Whitespace", "The name has stray spaces around it.", name.Trim()));
                continue;
            }

            if (FastFlagTypes.PrefixOf(name) is null)
            {
                string? healed = FastFlagTypes.StripBogusPrefix(name);

                problems.Add(healed is null
                    ? new FlagProblem(name, "Unknown prefix",
                        "Roblox does not use this prefix, so the client will ignore the flag.", null)
                    : new FlagProblem(name, "Bogus prefix",
                        $"Roblox has no '{name[..(name.Length - healed.Length)]}' prefix. Lists pasted from elsewhere often add one.",
                        healed));

                continue;
            }

            string normalised = NormaliseValue(name, value);

            switch (FastFlagTypes.KindOf(name))
            {
                case FlagValueKind.Boolean when normalised is not ("True" or "False"):
                    problems.Add(new FlagProblem(name, "Wrong type",
                        $"This is a boolean flag but the value is '{value}'.",
                        normalised.Length == 0 ? "False" : null));
                    break;

                case FlagValueKind.Integer when !long.TryParse(normalised, out _):
                    problems.Add(new FlagProblem(name, "Wrong type",
                        $"This is a numeric flag but the value is '{value}'.",
                        double.TryParse(normalised, out double number)
                            ? ((long)Math.Round(number)).ToString()
                            : null));
                    break;
            }
        }

        return problems;
    }
}
