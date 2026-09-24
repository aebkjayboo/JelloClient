using System.Text.Json;

namespace JelloClient.Roblox;

internal enum MsaaMode
{
    Default,
    X1,
    X2,
    X4
}

internal enum TextureQuality
{
    Default,
    Level0,
    Level1,
    Level2,
    Level3
}

internal static class FastFlagPresets
{
    public const string ManualFullscreen = "FFlagHandleAltEnterFullscreenManually";
    public const string DisableScaling = "DFFlagDisableDPIScale";
    public const string Msaa = "FIntDebugForceMSAASamples";
    public const string TextureQualityOverrideEnabled = "DFFlagTextureQualityOverrideEnabled";
    public const string TextureQualityLevel = "DFIntTextureQualityOverride";

    public static readonly IReadOnlyDictionary<MsaaMode, string?> MsaaValues = new Dictionary<MsaaMode, string?>
    {
        { MsaaMode.Default, null },
        { MsaaMode.X1, "1" },
        { MsaaMode.X2, "2" },
        { MsaaMode.X4, "4" }
    };

    public static readonly IReadOnlyDictionary<TextureQuality, string?> TextureQualityValues = new Dictionary<TextureQuality, string?>
    {
        { TextureQuality.Default, null },
        { TextureQuality.Level0, "0" },
        { TextureQuality.Level1, "1" },
        { TextureQuality.Level2, "2" },
        { TextureQuality.Level3, "3" }
    };

    public static string? GetValue(Dictionary<string, JsonElement> flags, string key) =>
        flags.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    public static void SetValue(Dictionary<string, JsonElement> flags, string key, string? value)
    {
        if (value is null)
        {
            flags.Remove(key);
            return;
        }

        flags[key] = JsonSerializer.SerializeToElement(value);
    }

    public static MsaaMode GetMsaa(Dictionary<string, JsonElement> flags)
    {
        string? current = GetValue(flags, Msaa);
        return MsaaValues.FirstOrDefault(pair => pair.Value == current).Key;
    }

    public static void SetMsaa(Dictionary<string, JsonElement> flags, MsaaMode mode) =>
        SetValue(flags, Msaa, MsaaValues[mode]);

    public static TextureQuality GetTextureQuality(Dictionary<string, JsonElement> flags)
    {
        if (GetValue(flags, TextureQualityOverrideEnabled) is null)
        {
            return TextureQuality.Default;
        }

        string? current = GetValue(flags, TextureQualityLevel);
        return TextureQualityValues.FirstOrDefault(pair => pair.Value == current).Key;
    }

    public static void SetTextureQuality(Dictionary<string, JsonElement> flags, TextureQuality quality)
    {
        if (quality == TextureQuality.Default)
        {
            SetValue(flags, TextureQualityOverrideEnabled, null);
            SetValue(flags, TextureQualityLevel, null);
            return;
        }

        SetValue(flags, TextureQualityOverrideEnabled, "True");
        SetValue(flags, TextureQualityLevel, TextureQualityValues[quality]);
    }

    public static bool GetToggle(Dictionary<string, JsonElement> flags, string key) =>
        string.Equals(GetValue(flags, key), "True", StringComparison.OrdinalIgnoreCase);

    public static void SetToggle(Dictionary<string, JsonElement> flags, string key, bool enabled) =>
        SetValue(flags, key, enabled ? "True" : null);
}
