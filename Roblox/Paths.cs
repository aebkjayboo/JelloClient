namespace JelloClient.Roblox;

internal static class Paths
{
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string _base = Default;

    public static string Default => Path.Combine(LocalAppData, "JelloClient");

    /// Everything Jello owns lives under here. The first run installer can move it, so it
    /// has to be set before anything touches the log or the settings file.
    public static string Base => _base;

    public static void Initialize(string? location)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            _base = location;
        }
    }

    public static string Downloads => Path.Combine(Base, "Downloads");

    public static string Versions => Path.Combine(Base, "Versions");

    public static string Modifications => Path.Combine(Base, "Modifications");

    public static string Logs => Path.Combine(Base, "Logs");

    public static string ModAssets => Path.Combine(Base, "ModAssets");

    public static string Themes => Path.Combine(Base, "Themes");

    public static string SettingsFile => Path.Combine(Base, "Settings.json");

    public static string StateFile => Path.Combine(Base, "State.json");

    public static string InstallMarker => Path.Combine(Base, "Install.json");

    public static string ApplicationFile => Path.Combine(Base, "JelloClient.exe");

    public static string StockRobloxDownloads => Path.Combine(LocalAppData, "Roblox", "Downloads");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Modifications);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(ModAssets);
        Directory.CreateDirectory(Themes);
    }

    public static string VersionDirectory(string versionGuid) => Path.Combine(Versions, versionGuid);
}
