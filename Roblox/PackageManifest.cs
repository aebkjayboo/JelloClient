namespace JelloClient.Roblox;

internal sealed class Package
{
    public required string Name { get; init; }

    public required string Signature { get; init; }

    public required long PackedSize { get; init; }

    public required long Size { get; init; }

    public string DownloadPath => Path.Combine(Paths.Downloads, Signature);

    public override string ToString() => $"[{Signature}] {Name}";
}

internal sealed class PackageManifest : List<Package>
{
    public PackageManifest(string data)
    {
        using var reader = new StringReader(data);

        string? version = reader.ReadLine();

        if (version != "v0")
        {
            throw new NotSupportedException($"Unexpected package manifest version '{version}', expected v0.");
        }

        while (true)
        {
            string? fileName = reader.ReadLine();
            string? signature = reader.ReadLine();
            string? rawPackedSize = reader.ReadLine();
            string? rawSize = reader.ReadLine();

            if (string.IsNullOrEmpty(fileName) ||
                string.IsNullOrEmpty(signature) ||
                string.IsNullOrEmpty(rawPackedSize) ||
                string.IsNullOrEmpty(rawSize))
            {
                break;
            }

            if (fileName == "RobloxPlayerLauncher.exe")
            {
                break;
            }

            Add(new Package
            {
                Name = fileName,
                Signature = signature,
                PackedSize = long.Parse(rawPackedSize),
                Size = long.Parse(rawSize)
            });
        }
    }

    public long TotalPackedSize => this.Sum(p => p.PackedSize);
}

internal static class PackageMap
{
    public static readonly IReadOnlyDictionary<string, string> Player = new Dictionary<string, string>
    {
        { "RobloxApp.zip", "" },
        { "Libraries.zip", "" },
        { "redist.zip", "" },
        { "shaders.zip", @"shaders\" },
        { "ssl.zip", @"ssl\" },

        { "WebView2.zip", "" },
        { "WebView2RuntimeInstaller.zip", @"WebView2RuntimeInstaller\" },

        { "content-avatar.zip", @"content\avatar\" },
        { "content-configs.zip", @"content\configs\" },
        { "content-fonts.zip", @"content\fonts\" },
        { "content-sky.zip", @"content\sky\" },
        { "content-sounds.zip", @"content\sounds\" },
        { "content-textures2.zip", @"content\textures\" },
        { "content-models.zip", @"content\models\" },

        { "content-textures3.zip", @"PlatformContent\pc\textures\" },
        { "content-terrain.zip", @"PlatformContent\pc\terrain\" },
        { "content-platform-fonts.zip", @"PlatformContent\pc\fonts\" },
        { "content-platform-dictionaries.zip", @"PlatformContent\pc\shared_compression_dictionaries\" },

        { "extracontent-luapackages.zip", @"ExtraContent\LuaPackages\" },
        { "extracontent-translations.zip", @"ExtraContent\translations\" },
        { "extracontent-models.zip", @"ExtraContent\models\" },
        { "extracontent-textures.zip", @"ExtraContent\textures\" },
        { "extracontent-places.zip", @"ExtraContent\places\" }
    };
}
