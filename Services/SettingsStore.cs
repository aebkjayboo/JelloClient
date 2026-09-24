using System.Text.Json;
using System.Text.Json.Serialization;
using JelloClient.Roblox;
using JelloClient.UI.Bootstrapper;

namespace JelloClient.Services;

internal enum LauncherLayout
{
    Vertical,
    Horizontal
}

internal enum AutoCleanupMode
{
    Off,
    EveryLaunch,
    Interval
}

internal sealed class CustomIntegration
{
    public string Name { get; set; } = "";

    public string Location { get; set; } = "";

    public string LaunchArgs { get; set; } = "";

    public bool AutoClose { get; set; } = true;
}

internal sealed class UserSettings
{
    public string Channel { get; set; } = Deployment.DefaultChannel;

    public bool AcrylicEnabled { get; set; } = true;

    public bool SecureUi { get; set; }

    public bool AlwaysOnTop { get; set; } = true;

    public bool MultiInstance { get; set; }

    public bool ConfirmLaunches { get; set; }

    public bool CloseToTray { get; set; } = true;

    public bool HideLauncherOnLaunch { get; set; } = true;

    public bool EnableActivityTracking { get; set; } = true;

    public bool UseDiscordRichPresence { get; set; } = true;

    public bool HideRichPresenceButtons { get; set; }

    /// Adds "(4 of 12)" to the Discord status, from the player count Jello already reads
    /// out of the client log.
    public bool ShowServerFillOnDiscord { get; set; } = true;

    public bool ShowServerDetails { get; set; }

    public bool NotifyOnServerJoin { get; set; } = true;


    public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.JelloDialog;

    public string? BootstrapperIconPath { get; set; }

    public string? BootstrapperTheme { get; set; }

    public LauncherLayout LauncherLayout { get; set; } = LauncherLayout.Vertical;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string? Locale { get; set; }

    public bool FirstRunComplete { get; set; }

    public bool LiveFlagInjection { get; set; }

    public string? PreferredDisplay { get; set; }


    /// Swaps the Roblox window's icon for the icon of whatever game is being played, so
    /// the taskbar says what it is. The window's icon, not the executable's - the exe is
    /// signed and cannot be rewritten.
    public bool GameIconOnWindow { get; set; }

    public string? GameIconCustomPath { get; set; }

    public bool DuckAudioUnfocused { get; set; }

    public double DuckAudioLevel { get; set; } = 0.3;

    /// Light or dark for the Roblox app itself, through the Windows app colour setting the
    /// Lua app reads. Leave alone by default, because it is a Windows-wide setting.
    public RobloxAppTheme RobloxAppTheme { get; set; } = RobloxAppTheme.LeaveAlone;

    /// The Lab: experimental features that read the running client's memory. Off by
    /// default and gated behind an explicit opt-in, because attaching to another process,
    /// even read only, should be a deliberate choice.
    public bool LabEnabled { get; set; }

    /// The Lab's GUI tint: recolours the Roblox app by writing its frames directly. Off by
    /// default; only runs while the Lab is on.
    public bool GuiTintEnabled { get; set; }

    public string? GuiTintColour { get; set; } = "#3B2A6B";

    public double GuiTintStrength { get; set; } = 1.0;

    /// Overrides for the offset tables Jello would otherwise fetch. When this is off, both
    /// tables come from the published sources for the running build, as before. When it is
    /// on, whichever box below holds valid JSON is used in place of a fetch, and an empty
    /// box still falls back to the fetch.
    public bool OffsetsOverrideEnabled { get; set; }

    /// A pasted memory-layout table (the grouped struct offsets the Lab walker reads), in
    /// the same shape the dump publishes: an object with an "Offsets" object of groups.
    public string? MemoryOffsetsOverrideJson { get; set; }

    /// A pasted FastFlag address table (the flat name to relative-address map live flags
    /// write through), as an object with an "Offsets" object of name to number.
    public string? FlagOffsetsOverrideJson { get; set; }

    /// Whether the person has accepted the License, Terms of Service and Privacy Policy on
    /// the consent screen. The installer will not proceed until this is true.
    public bool AcceptedTerms { get; set; }

    public DateTime? AcceptedTermsUtc { get; set; }

    /// Whether the licensed background agent (Private) runs on this install. Default on. Set
    /// off locally, or remotely when the server puts this install on the disabled list; when
    /// off, the agent does not run, does not report, and its autostart is removed.
    public bool PrivateEnabled { get; set; } = true;

    /// Blocks Roblox's telemetry hosts through the Windows hosts file. Machine wide, and
    /// applying it needs administrator rights.
    public bool BlockTelemetry { get; set; }

    /// 0 leaves Roblox on every core; anything else caps it to that many.
    public int CoreLimit { get; set; }

    public ProcessPriority RobloxPriority { get; set; } = ProcessPriority.Leave;

    /// Looks up a few servers before launching and joins the one with the lowest measured
    /// round trip. Needs the Roblox login already on this machine.
    public bool MatchmakerEnabled { get; set; }

    /// How many servers to check. Roblox refuses lookups after a handful, so this is small
    /// on purpose.
    public int MatchmakerBudget { get; set; } = 5;

    /// Prefer emptier servers among the candidates rather than fuller ones.
    public bool MatchmakerPreferEmptier { get; set; }

    public string? ShortcutIconPath { get; set; }

    public AutoCleanupMode AutoCleanup { get; set; } = AutoCleanupMode.Off;

    public int AutoCleanupMinutes { get; set; } = 720;

    public bool AutoCleanupIncludesCache { get; set; }

    public DateTime? LastAutoCleanupUtc { get; set; }

    public string? LastPlayedName { get; set; }

    public long LastPlayedPlaceId { get; set; }

    public DateTime? LastPlayedUtc { get; set; }

    public CursorType CursorType { get; set; } = CursorType.Default;

    public EmojiType EmojiType { get; set; } = EmojiType.Default;

    public bool OldCharacterSounds { get; set; }

    public bool OldAvatarBackground { get; set; }

    public List<CustomIntegration> CustomIntegrations { get; set; } = new();

    public Dictionary<string, JsonElement> FastFlags { get; set; } = new();

    [JsonIgnore]
    public IReadOnlyDictionary<string, object> FastFlagValues =>
        FastFlags.ToDictionary(pair => pair.Key, pair => (object)pair.Value);
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(Paths.SettingsFile))
            {
                return new UserSettings();
            }

            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Paths.SettingsFile), Options)
                ?? new UserSettings();
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsStore::Load", ex);
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        try
        {
            Paths.EnsureCreated();
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsStore::Save", ex);
        }
    }
}
