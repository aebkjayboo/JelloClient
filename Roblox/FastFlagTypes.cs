namespace JelloClient.Roblox;

internal enum FlagValueKind
{
    Boolean,
    Integer,
    Text
}

/// Roblox names its flags by type: FFlag/DFFlag/SFFlag are booleans, FInt/DFInt are
/// integers, FString/DFString are strings. That convention is what drives the editor's
/// choice of control, so a value field always matches the flag it belongs to.
internal static class FastFlagTypes
{
    /// Every prefix Roblox actually ships, longest first so DFString wins over DF.
    private static readonly (string Prefix, FlagValueKind Kind)[] Prefixes =
    {
        ("DFString", FlagValueKind.Text),
        ("SFString", FlagValueKind.Text),
        ("FString", FlagValueKind.Text),
        ("DFFlag", FlagValueKind.Boolean),
        ("SFFlag", FlagValueKind.Boolean),
        ("GFFlag", FlagValueKind.Boolean),
        ("FFlag", FlagValueKind.Boolean),
        ("DFInt", FlagValueKind.Integer),
        ("SFInt", FlagValueKind.Integer),
        ("GFInt", FlagValueKind.Integer),
        ("FInt", FlagValueKind.Integer),
        ("DFLog", FlagValueKind.Integer),
        ("SFLog", FlagValueKind.Integer),
        ("FLog", FlagValueKind.Integer)
    };

    public static string? PrefixOf(string flag) =>
        Prefixes.FirstOrDefault(entry => flag.StartsWith(entry.Prefix, StringComparison.Ordinal)).Prefix;

    /// Pasted flag lists often carry a made up prefix, most commonly DFlag, which is not
    /// one of Roblox's. Returns the name with that prefix removed when what is left does
    /// start with a real one, otherwise null.
    public static string? StripBogusPrefix(string flag)
    {
        foreach (string bogus in new[] { "DFlag", "SFlag", "GFlag", "Flag" })
        {
            if (!flag.StartsWith(bogus, StringComparison.Ordinal) || PrefixOf(flag) is not null)
            {
                continue;
            }

            string rest = flag[bogus.Length..];

            if (rest.Length > 0 && PrefixOf(rest) is not null)
            {
                return rest;
            }
        }

        return null;
    }

    public static FlagValueKind KindOf(string flag)
    {
        foreach (string prefix in new[] { "DFFlag", "SFFlag", "FFlag" })
        {
            if (flag.StartsWith(prefix, StringComparison.Ordinal))
            {
                return FlagValueKind.Boolean;
            }
        }

        foreach (string prefix in new[] { "DFInt", "SFInt", "FInt" })
        {
            if (flag.StartsWith(prefix, StringComparison.Ordinal))
            {
                return FlagValueKind.Integer;
            }
        }

        return FlagValueKind.Text;
    }

    public static string GroupOf(string flag)
    {
        string name = flag;

        foreach (string prefix in new[] { "DFFlag", "SFFlag", "FFlag", "DFInt", "SFInt", "FInt", "DFString", "FString", "FLog", "DFLog" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        if (Contains(name, "Render", "Graphics", "Shadow", "Texture", "Mesh", "Grass", "Light", "MSAA", "Bloom", "PostFx", "Sky", "Terrain", "CSG", "Particle"))
        {
            return "Rendering";
        }

        if (Contains(name, "Network", "Replicat", "Payload", "Packet", "Bandwidth", "Http", "Connection", "MTU"))
        {
            return "Network";
        }

        if (Contains(name, "Cache", "Preload", "Prerender", "Asset", "Memory", "TaskScheduler", "Concurrency", "Parallel", "Fps", "FPS", "Frame"))
        {
            return "Performance";
        }

        if (Contains(name, "Telemetry", "Analytics", "Diagnostic", "Report", "Tencent"))
        {
            return "Telemetry";
        }

        if (Contains(name, "Menu", "Gui", "Chat", "Ad", "Chrome", "Unibar", "Text", "Locali", "Titlebar", "Haptic", "Emoji", "Font"))
        {
            return "Interface";
        }

        if (Contains(name, "Debug", "Log", "Verbose"))
        {
            return "Debug";
        }

        return "Other";
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
