using System.Text.Json;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// What Roblox did with the flags Jello wrote, read back from the client's own log.
///
/// Roblox 0.737 refuses most locally configured flags. Each one it drops is written to the
/// client log as
///
///     [FLog::FlagFetchingStarterModule] Denied local configuration for: FFlagWhatever
///
/// and nothing else says so: the launch succeeds, ClientAppSettings.json still holds the
/// flag, and the setting simply never takes effect. Measured on this build, the refusal is
/// an allowlist rather than a blanket ban - FFlagDebugGraphicsPreferD3D11 and
/// DFIntDebugFRMQualityLevelOverride were honoured in the same launch that dropped
/// FFlagDebugDisplayFPS and DFIntTaskSchedulerTargetFps - and a name Roblox does not know
/// at all is refused the same way as one it knows but will not let you set.
///
/// So Jello reads the log after every launch and records what actually happened. It cannot
/// change the outcome, but it can stop the person editing a flag that was never going to
/// do anything.
internal static class FlagAudit
{
    private const string Marker = "Denied local configuration for: ";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string StatePath => Path.Combine(Paths.Base, "FlagAudit.json");

    internal sealed record Result(
        DateTime WhenUtc,
        string Version,
        IReadOnlyList<string> Refused,
        IReadOnlyList<string> Accepted)
    {
        public bool IsEmpty => Refused.Count == 0 && Accepted.Count == 0;
    }

    private static Result? _current;

    public static Result? Current => _current ??= Load();

    /// How long to wait for Roblox to have finished with the flags. It echoes
    /// ClientAppSettings.json into its log and then refuses what it will not take, all
    /// inside the first second, but the log is barely created when Jello gets here.
    private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// Reads one launch log and records which of the flags Jello wrote were refused.
    /// Anything Jello wrote that is not named in a refusal was accepted.
    public static async Task<Result?> InspectAsync(
        string logPath, IReadOnlyDictionary<string, object> written, string version)
    {
        const string ident = "FlagAudit::Inspect";

        try
        {
            var refused = await WaitForVerdictAsync(logPath, written);

            if (refused is null)
            {
                Log.Write(ident, "Roblox never echoed the flags into its log, so nothing could be checked");
                return null;
            }

            var accepted = written.Keys
                .Where(name => !refused.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new Result(
                DateTime.UtcNow,
                version,
                refused.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
                accepted);

            _current = result;
            Save(result);

            Log.Write(ident, Describe(result));

            return result;
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            return null;
        }
    }

    /// Waits until Roblox has echoed every flag Jello wrote, which is the point at which
    /// it has also decided what to refuse, then reads the verdict once more so a refusal
    /// written microseconds later is not missed.
    ///
    /// Reading straight away is what made the first version of this report every flag as
    /// accepted: the log existed but held nothing yet.
    private static async Task<HashSet<string>?> WaitForVerdictAsync(
        string logPath, IReadOnlyDictionary<string, object> written)
    {
        var deadline = DateTime.UtcNow + SettleWindow;

        while (DateTime.UtcNow < deadline)
        {
            string? body = ReadAll(logPath);

            if (body is not null && written.Keys.All(name => body.Contains(name, StringComparison.Ordinal)))
            {
                // The refusals follow the echo immediately, but not atomically.
                await Task.Delay(PollInterval);

                return ReadRefusals(logPath);
            }

            await Task.Delay(PollInterval);
        }

        // Nothing was written, so there is nothing to wait for and nothing to refuse.
        return written.Count == 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
    }

    private static string? ReadAll(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return null;
            }

            using var stream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// The log is still open in the client, so it is read share-all rather than copied.
    private static HashSet<string>? ReadRefusals(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return null;
        }

        var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var stream = new FileStream(
            logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            int at = line.IndexOf(Marker, StringComparison.Ordinal);

            if (at < 0)
            {
                continue;
            }

            string name = line[(at + Marker.Length)..].Trim();

            if (name.Length > 0)
            {
                refused.Add(name);
            }
        }

        return refused;
    }

    /// True when this exact flag was refused on the last launch. Unknown flags, and the
    /// state before any launch, both come back false so nothing is accused wrongly.
    public static bool WasRefused(string name) =>
        Current is { } result && result.Refused.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool WasAccepted(string name) =>
        Current is { } result && result.Accepted.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static string Describe() => Current is { } result ? Describe(result) : "No launch has been checked yet.";

    private static string Describe(Result result)
    {
        if (result.IsEmpty)
        {
            return "No flags were set on the last launch, so there was nothing to check.";
        }

        if (result.Refused.Count == 0)
        {
            return $"Roblox took all {result.Accepted.Count} flag(s) on the last launch.";
        }

        if (result.Accepted.Count == 0)
        {
            return $"Roblox refused every one of the {result.Refused.Count} flag(s) on the last launch.";
        }

        return $"Roblox took {result.Accepted.Count} flag(s) and refused {result.Refused.Count} on the last launch.";
    }

    private static Result? Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<Result>(File.ReadAllText(StatePath))
                : null;
        }
        catch (Exception ex)
        {
            Log.Write("FlagAudit::Load", $"Could not read the last audit: {ex.Message}");
            return null;
        }
    }

    private static void Save(Result result)
    {
        try
        {
            Directory.CreateDirectory(Paths.Base);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(result, Json));
        }
        catch (Exception ex)
        {
            Log.Write("FlagAudit::Save", $"Could not record the audit: {ex.Message}");
        }
    }

    public static void Clear()
    {
        _current = null;

        try
        {
            if (File.Exists(StatePath))
            {
                File.Delete(StatePath);
            }
        }
        catch (Exception ex)
        {
            Log.Write("FlagAudit::Clear", ex.Message);
        }
    }
}
