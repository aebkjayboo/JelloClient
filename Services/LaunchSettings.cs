using System.Text.RegularExpressions;

namespace JelloClient.Services;

internal sealed partial class LaunchSettings
{
    private static readonly string[] PlayerUriSchemes = { "roblox:", "roblox-player:" };

    public LaunchSettings(IReadOnlyList<string> args)
    {
        int index = 0;

        if (args.Count > 0 && PlayerUriSchemes.Any(scheme => args[0].StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
        {
            RobloxLaunchArgs = args[0];
            IsPlayerLaunch = true;
            index = 1;
        }

        for (; index < args.Count; index++)
        {
            string arg = args[index];

            if (!arg.StartsWith('-'))
            {
                continue;
            }

            string identifier = arg[1..].ToLowerInvariant();
            string? data = index + 1 < args.Count && !args[index + 1].StartsWith('-') ? args[++index] : null;

            switch (identifier)
            {
                case "player":
                    IsPlayerLaunch = true;

                    if (!string.IsNullOrEmpty(data))
                    {
                        RobloxLaunchArgs = data;
                    }

                    break;

                case "channel":
                    if (!string.IsNullOrEmpty(data))
                    {
                        ChannelOverride = data.ToLowerInvariant();
                    }

                    break;

                case "settings":
                case "menu":
                    ForceSettings = true;
                    break;

                case "nolaunch":
                    NoLaunch = true;
                    break;

                case "agent":
                    IsAgent = true;
                    break;

                case "quiet":
                    Quiet = true;
                    break;

                case "blocktelemetry":
                    TelemetryBlockChange = true;
                    break;

                case "unblocktelemetry":
                    TelemetryBlockChange = false;
                    break;
            }
        }

        ChannelOverride ??= ReadChannelFromLaunchArgs(RobloxLaunchArgs);
    }

    public string RobloxLaunchArgs { get; } = "";

    public bool IsPlayerLaunch { get; }

    public bool ForceSettings { get; }

    public bool NoLaunch { get; }

    public bool Quiet { get; }

    /// The headless background agent: no window, no tray, just the identity link. Spawned when
    /// the user exits the UI so the link keeps running. Same executable, no separate file.
    public bool IsAgent { get; }

    /// Set only by the elevated pass Jello starts for itself: true writes the hosts block,
    /// false takes it out. Nothing else runs when it is set.
    public bool? TelemetryBlockChange { get; }

    public string? ChannelOverride { get; }

    public bool ShouldBootstrap => IsPlayerLaunch && !ForceSettings && !NoLaunch;

    public string Describe()
    {
        var parts = new List<string>();

        if (IsPlayerLaunch)
        {
            parts.Add("player launch");
        }

        if (ForceSettings)
        {
            parts.Add("forced settings");
        }

        if (NoLaunch)
        {
            parts.Add("no launch");
        }

        if (Quiet)
        {
            parts.Add("quiet");
        }

        if (TelemetryBlockChange is { } change)
        {
            parts.Add(change ? "block telemetry" : "unblock telemetry");
        }

        if (ChannelOverride is not null)
        {
            parts.Add($"channel {ChannelOverride}");
        }

        return parts.Count == 0 ? "normal startup" : string.Join(", ", parts);
    }

    private static string? ReadChannelFromLaunchArgs(string launchArgs)
    {
        if (string.IsNullOrEmpty(launchArgs))
        {
            return null;
        }

        var match = ChannelPattern().Match(launchArgs);

        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex("channel:([a-zA-Z0-9-_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();
}
