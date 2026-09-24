namespace JelloClient.Roblox;

/// Tag buckets for a flag name, matching Voidstrap's FastFlagTagHelper token rules
/// and their two-tag display cap with a "+N" overflow pill.
internal static class FastFlagTags
{
    private const int MaxVisibleTags = 2;

    private static bool Has(string name, string token) =>
        name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasAny(string name, params string[] tokens) =>
        tokens.Any(token => Has(name, token));

    public static List<string> GetTags(string name)
    {
        var tags = new List<string>();

        if (string.IsNullOrEmpty(name))
        {
            tags.Add("Unknown");
            return tags;
        }

        if (HasAny(name, "perf", "fps", "frame", "frm", "render", "thread", "graphics"))
        {
            tags.Add("Performance");
        }

        if (HasAny(name, "fix", "debug", "crash", "stability"))
        {
            tags.Add("Fix");
        }

        if (HasAny(name, "experimental", "test", "task", "beta"))
        {
            tags.Add("Experimental");
        }

        if (HasAny(name, "graphics", "render", "quality", "gpu", "shader", "postfx", "texture", "blur", "voxel", "detail", "lighting"))
        {
            tags.Add("Graphics");
        }

        if (HasAny(name, "distance", "level", "lod"))
        {
            tags.Add("LOD");
        }

        if (HasAny(name, "ui", "ux", "menu", "title", "interface"))
        {
            tags.Add("UI");
        }

        if (tags.Count == 0)
        {
            tags.Add("Unknown");
        }

        return tags;
    }

    public static IReadOnlyList<string> VisibleTags(string name)
    {
        var tags = GetTags(name);

        if (tags.Count <= MaxVisibleTags)
        {
            return tags;
        }

        var visible = tags.GetRange(0, MaxVisibleTags);
        visible.Add($"+{tags.Count - MaxVisibleTags}");

        return visible;
    }
}
