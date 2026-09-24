using System.Text.Json;

namespace JelloClient.Roblox;

internal enum RenderingMode
{
    Automatic,
    Direct3D11,
    Direct3D10,
    Vulkan,
    OpenGL
}

/// The performance knobs that are really just FastFlags underneath.
///
/// Every flag here was checked twice: it appears in Voidstrap's own preset table, and it
/// exists in the offset dump for the build we run, which is how "TaskSchedulerLimitTargetFps"
/// was caught as dead and left out.
internal static class Performance
{
    public const string FramerateFlag = "DFIntTaskSchedulerTargetFps";

    public const string QualityFlag = "DFIntDebugFRMQualityLevelOverride";

    public const string DisplayFpsFlag = "FFlagDebugDisplayFPS";

    private static readonly IReadOnlyDictionary<RenderingMode, string> RendererFlags =
        new Dictionary<RenderingMode, string>
        {
            [RenderingMode.Direct3D11] = "FFlagDebugGraphicsPreferD3D11",
            [RenderingMode.Direct3D10] = "FFlagDebugGraphicsPreferD3D11FL10",
            [RenderingMode.Vulkan] = "FFlagDebugGraphicsPreferVulkan",
            [RenderingMode.OpenGL] = "FFlagDebugGraphicsPreferOpenGL"
        };

    // ---------- framerate ----------

    public static int? Framerate(Dictionary<string, JsonElement> flags) =>
        int.TryParse(FastFlagPresets.GetValue(flags, FramerateFlag), out int fps) ? fps : null;

    public static void SetFramerate(Dictionary<string, JsonElement> flags, int? fps) =>
        FastFlagPresets.SetValue(flags, FramerateFlag, fps?.ToString());

    public static string DescribeFramerate(int? fps, int displayHz) => fps switch
    {
        null => "Uncapped. Roblox runs as fast as the machine allows, which is what it does by default.",
        var value when displayHz > 0 && value == displayHz =>
            $"Capped at {value} frames per second, matching the display you picked.",
        var value => $"Capped at {value} frames per second."
    };

    // ---------- renderer ----------

    public static RenderingMode Renderer(Dictionary<string, JsonElement> flags)
    {
        foreach (var pair in RendererFlags)
        {
            if (FastFlagPresets.GetValue(flags, pair.Value) == "True")
            {
                return pair.Key;
            }
        }

        return RenderingMode.Automatic;
    }

    public static void SetRenderer(Dictionary<string, JsonElement> flags, RenderingMode mode)
    {
        // Only one may be set: two preferences at once and the client picks for itself.
        foreach (var pair in RendererFlags)
        {
            FastFlagPresets.SetValue(flags, pair.Value, pair.Key == mode ? "True" : null);
        }
    }

    public static string DescribeRenderer(RenderingMode mode) => mode switch
    {
        RenderingMode.Direct3D11 => "Forces Direct3D 11. The usual choice on Windows.",
        RenderingMode.Direct3D10 => "Forces Direct3D 11 at feature level 10, for older cards.",
        RenderingMode.Vulkan => "Forces Vulkan. Faster on some AMD and Intel cards, unstable on others.",
        RenderingMode.OpenGL => "Forces OpenGL. Slowest of the four, useful when the others will not start.",
        _ => "Roblox picks the graphics API itself."
    };

    // ---------- quality ----------

    public static int? Quality(Dictionary<string, JsonElement> flags) =>
        int.TryParse(FastFlagPresets.GetValue(flags, QualityFlag), out int level) ? level : null;

    public static void SetQuality(Dictionary<string, JsonElement> flags, int? level) =>
        FastFlagPresets.SetValue(flags, QualityFlag, level?.ToString());

    public static string DescribeQuality(int? level) => level switch
    {
        null => "The in-game quality slider decides, as normal.",
        <= 5 => $"Pinned to level {level} of 21. Lowest detail, highest framerate.",
        <= 15 => $"Pinned to level {level} of 21.",
        _ => $"Pinned to level {level} of 21. Full detail, ignores the in-game slider."
    };

    // ---------- fps counter ----------

    public static bool ShowsFramerate(Dictionary<string, JsonElement> flags) =>
        FastFlagPresets.GetValue(flags, DisplayFpsFlag) == "True";

    public static void SetShowsFramerate(Dictionary<string, JsonElement> flags, bool shown) =>
        FastFlagPresets.SetValue(flags, DisplayFpsFlag, shown ? "True" : null);
}
