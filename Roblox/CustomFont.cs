using System.Text.Json;
using System.Text.Json.Serialization;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed class FontFace
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("style")]
    public string Style { get; set; } = "normal";

    [JsonPropertyName("assetId")]
    public string AssetId { get; set; } = "";
}

internal sealed class FontFamilyFile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("faces")]
    public List<FontFace> Faces { get; set; } = new();
}

internal static class CustomFont
{
    private const string AssetPath = "rbxasset://fonts/CustomFont.ttf";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string ModFontPath => Path.Combine(Paths.Modifications, "content", "fonts", "CustomFont.ttf");

    private static string ModFamiliesDirectory => Path.Combine(Paths.Modifications, "content", "fonts", "families");

    public static bool IsInstalled => File.Exists(ModFontPath);

    public static void Install(string sourcePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModFontPath)!);
        File.Copy(sourcePath, ModFontPath, true);

        Log.Write("CustomFont::Install", $"Installed {Path.GetFileName(sourcePath)} as the custom font");
    }

    public static void Remove()
    {
        if (File.Exists(ModFontPath))
        {
            File.Delete(ModFontPath);
        }

        if (Directory.Exists(ModFamiliesDirectory))
        {
            Directory.Delete(ModFamiliesDirectory, true);
        }

        Log.Write("CustomFont::Remove", "Removed the custom font and its family overrides");
    }

    public static void Synchronise(string versionDirectory)
    {
        const string ident = "CustomFont::Synchronise";

        string familiesDirectory = Path.Combine(versionDirectory, "content", "fonts", "families");

        if (!IsInstalled)
        {
            if (Directory.Exists(ModFamiliesDirectory))
            {
                Directory.Delete(ModFamiliesDirectory, true);
                Log.Write(ident, "No custom font is set, cleared the family overrides");
            }

            return;
        }

        if (!Directory.Exists(familiesDirectory))
        {
            Log.Write(ident, $"{familiesDirectory} does not exist yet, skipping");
            return;
        }

        Directory.CreateDirectory(ModFamiliesDirectory);

        int written = 0;

        foreach (string source in Directory.GetFiles(familiesDirectory, "*.json"))
        {
            string destination = Path.Combine(ModFamiliesDirectory, Path.GetFileName(source));

            if (File.Exists(destination))
            {
                continue;
            }

            try
            {
                var family = JsonSerializer.Deserialize<FontFamilyFile>(File.ReadAllText(source));

                if (family is null || family.Faces.Count == 0)
                {
                    continue;
                }

                bool changed = false;

                foreach (var face in family.Faces)
                {
                    if (face.AssetId != AssetPath)
                    {
                        face.AssetId = AssetPath;
                        changed = true;
                    }
                }

                if (changed)
                {
                    File.WriteAllText(destination, JsonSerializer.Serialize(family, WriteOptions));
                    written++;
                }
            }
            catch (Exception ex)
            {
                Log.Write(ident, $"Could not rewrite {Path.GetFileName(source)}: {ex.Message}");
            }
        }

        if (written > 0)
        {
            Log.Write(ident, $"Redirected {written} font family file(s) to the custom font");
        }
    }
}
