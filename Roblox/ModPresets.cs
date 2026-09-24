using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal enum CursorType
{
    Default,
    From2013,
    From2006,
    Custom
}

internal enum EmojiType
{
    Default,
    Catmoji,
    Windows11,
    Windows10,
    Windows8
}

internal sealed record ModFile(string Destination, string Resource);

internal static class ModResources
{
    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();
    private static readonly string[] Names = Assembly.GetManifestResourceNames();

    public static Stream Open(string suffix)
    {
        string name = Names.Single(candidate => candidate.EndsWith(suffix, StringComparison.Ordinal));

        return Assembly.GetManifestResourceStream(name)!;
    }

    public static bool Exists(string suffix) =>
        Names.Any(candidate => candidate.EndsWith(suffix, StringComparison.Ordinal));
}

internal static class ModPresets
{
    private const string EmojiDestination = @"content\fonts\TwemojiMozilla.ttf";

    private const string EmojiUrlPrefix =
        "https://github.com/bloxstraplabs/rbxcustom-fontemojis/releases/download/my-phone-is-78-percent/";

    public static readonly IReadOnlyList<ModFile> OldAvatarBackground = new[]
    {
        new ModFile(@"ExtraContent\places\Mobile.rbxl", "Mods.OldAvatarBackground.rbxl")
    };

    public static readonly IReadOnlyList<ModFile> OldCharacterSounds = new[]
    {
        new ModFile(@"content\sounds\action_footsteps_plastic.mp3", "Mods.Sounds.OldWalk.mp3"),
        new ModFile(@"content\sounds\action_jump.mp3", "Mods.Sounds.OldJump.mp3"),
        new ModFile(@"content\sounds\action_get_up.mp3", "Mods.Sounds.OldGetUp.mp3"),
        new ModFile(@"content\sounds\action_falling.mp3", "Mods.Sounds.Empty.mp3"),
        new ModFile(@"content\sounds\action_jump_land.mp3", "Mods.Sounds.Empty.mp3"),
        new ModFile(@"content\sounds\action_swim.mp3", "Mods.Sounds.Empty.mp3"),
        new ModFile(@"content\sounds\impact_water.mp3", "Mods.Sounds.Empty.mp3")
    };

    public static readonly IReadOnlyDictionary<CursorType, IReadOnlyList<ModFile>> Cursors =
        new Dictionary<CursorType, IReadOnlyList<ModFile>>
        {
            [CursorType.From2006] = new[]
            {
                new ModFile(@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", "Mods.Cursor.From2006.ArrowCursor.png"),
                new ModFile(@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Mods.Cursor.From2006.ArrowFarCursor.png")
            },
            [CursorType.From2013] = new[]
            {
                new ModFile(@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", "Mods.Cursor.From2013.ArrowCursor.png"),
                new ModFile(@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Mods.Cursor.From2013.ArrowFarCursor.png")
            }
        };

    private static readonly IReadOnlyDictionary<EmojiType, string> EmojiFiles = new Dictionary<EmojiType, string>
    {
        [EmojiType.Catmoji] = "Catmoji.ttf",
        [EmojiType.Windows11] = "Win1122H2SegoeUIEmoji.ttf",
        [EmojiType.Windows10] = "Win10April2018SegoeUIEmoji.ttf",
        [EmojiType.Windows8] = "Win8.1SegoeUIEmoji.ttf"
    };

    private static readonly IReadOnlyDictionary<EmojiType, string> EmojiHashes = new Dictionary<EmojiType, string>
    {
        [EmojiType.Catmoji] = "98138f398a8cde897074dd2b8d53eca0",
        [EmojiType.Windows11] = "d50758427673578ddf6c9edcdbf367f5",
        [EmojiType.Windows10] = "d8a7eecbebf9dfdf622db8ccda63aff5",
        [EmojiType.Windows8] = "2b01c6caabbe95afc92aa63b9bf100f3"
    };

    private static string DestinationPath(string relative) => Path.Combine(Paths.Modifications, relative);

    private static string OverridePath(string resource) => Path.Combine(Paths.ModAssets, resource);

    public static bool HasOverride(string resource) => File.Exists(OverridePath(resource));

    private static Stream OpenSource(string resource) =>
        HasOverride(resource) ? File.OpenRead(OverridePath(resource)) : ModResources.Open(resource);

    private static byte[] SourceHash(string resource)
    {
        using var stream = OpenSource(resource);
        using var md5 = MD5.Create();

        return md5.ComputeHash(stream);
    }

    private static bool Matches(string destination, string resource)
    {
        if (!File.Exists(destination))
        {
            return false;
        }

        using var stream = File.OpenRead(destination);
        using var md5 = MD5.Create();

        return md5.ComputeHash(stream).SequenceEqual(SourceHash(resource));
    }

    public static bool IsApplied(IReadOnlyList<ModFile> files) =>
        files.Count > 0 && files.All(file => Matches(DestinationPath(file.Destination), file.Resource));

    public static IReadOnlyList<string> OverriddenAssets(IReadOnlyList<ModFile> files) =>
        files.Select(file => file.Resource).Distinct().Where(HasOverride).ToList();

    public static void Apply(IReadOnlyList<ModFile> files, bool enabled)
    {
        const string ident = "ModPresets::Apply";

        Paths.EnsureCreated();

        int written = 0;
        int skipped = 0;

        foreach (var file in files)
        {
            string destination = DestinationPath(file.Destination);

            if (!enabled)
            {
                if (Matches(destination, file.Resource))
                {
                    File.Delete(destination);
                    Log.Write(ident, $"Removed {file.Destination}");
                }

                continue;
            }

            if (Matches(destination, file.Resource))
            {
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using (var source = OpenSource(file.Resource))
            using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }

            written++;
        }

        if (enabled)
        {
            Log.Write(ident, $"Wrote {written} file(s), {skipped} already matched");
        }
    }

    public static CursorType CurrentCursor()
    {
        foreach (var pair in Cursors)
        {
            if (IsApplied(pair.Value))
            {
                return pair.Key;
            }
        }

        return CursorType.Default;
    }

    public static readonly IReadOnlyList<string> CursorTargets = new[]
    {
        @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",
        @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png"
    };

    public static void ApplyCursor(CursorType type)
    {
        foreach (var pair in Cursors)
        {
            Apply(pair.Value, false);
        }

        if (type == CursorType.Custom)
        {
            return;
        }

        if (type != CursorType.Default && Cursors.TryGetValue(type, out var files))
        {
            Apply(files, true);
        }
    }

    public static void ApplyCustomCursor(string arrowPath, string arrowFarPath)
    {
        const string ident = "ModPresets::ApplyCustomCursor";

        foreach (var pair in Cursors)
        {
            Apply(pair.Value, false);
        }

        var sources = new[] { arrowPath, arrowFarPath };

        for (int index = 0; index < CursorTargets.Count; index++)
        {
            string destination = DestinationPath(CursorTargets[index]);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sources[index], destination, true);

            Log.Write(ident, $"Applied {Path.GetFileName(sources[index])} to {CursorTargets[index]}");
        }
    }

    public static EmojiType CurrentEmoji()
    {
        string destination = DestinationPath(EmojiDestination);

        if (!File.Exists(destination))
        {
            return EmojiType.Default;
        }

        string hash = Md5(destination);

        foreach (var pair in EmojiHashes)
        {
            if (pair.Value == hash)
            {
                return pair.Key;
            }
        }

        return EmojiType.Default;
    }

    public static async Task ApplyEmojiAsync(EmojiType type, HttpClient http, CancellationToken ct)
    {
        const string ident = "ModPresets::ApplyEmoji";

        string destination = DestinationPath(EmojiDestination);

        if (type == EmojiType.Default)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
                Log.Write(ident, "Removed the emoji font override");
            }

            return;
        }

        if (File.Exists(destination) && Md5(destination) == EmojiHashes[type])
        {
            Log.Write(ident, $"{type} is already applied");
            return;
        }

        string url = EmojiUrlPrefix + EmojiFiles[type];

        Log.Write(ident, $"Downloading {type} from {url}");

        using var response = await RobloxHttp.GetAsync(
            http,
            url,
            $"Downloading the {type} emoji font",
            "The emoji font could not be downloaded. Check your connection and try again.",
            HttpCompletionOption.ResponseContentRead,
            ct).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        Log.Write(ident, $"Applied {type} ({new FileInfo(destination).Length} bytes)");
    }

    private static string Md5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = MD5.Create();

        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
