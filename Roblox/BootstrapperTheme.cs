using System.Xml.Linq;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed class ThemeParseException : Exception
{
    public ThemeParseException(string themeName, string message) : base(message)
    {
        ThemeName = themeName;
    }

    public string ThemeName { get; }
}

internal sealed class BootstrapperTheme
{
    public const string ThemeScheme = "theme://";

    public required string Name { get; init; }

    public required string Directory { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public bool RoundedCorners { get; init; } = true;

    public string TitleBarTitle { get; init; } = "";

    public bool ShowMinimize { get; init; }

    public bool ShowClose { get; init; }

    public required string PanelPath { get; init; }

    public string ManifestPath => Path.Combine(Directory, "Theme.xml");

    public string Resolve(string source)
    {
        if (source.StartsWith(ThemeScheme, StringComparison.OrdinalIgnoreCase))
        {
            source = source[ThemeScheme.Length..];
        }

        return Path.GetFullPath(Path.Combine(Directory, source.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static IReadOnlyList<string> Discover()
    {
        if (!System.IO.Directory.Exists(Paths.Themes))
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();

        foreach (var directory in new DirectoryInfo(Paths.Themes).GetDirectories())
        {
            if (File.Exists(Path.Combine(directory.FullName, "Theme.xml")))
            {
                names.Add(directory.Name);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);

        return names;
    }

    public static BootstrapperTheme Load(string name)
    {
        const string ident = "BootstrapperTheme::Load";

        string directory = Path.Combine(Paths.Themes, name);
        string manifest = Path.Combine(directory, "Theme.xml");

        if (!File.Exists(manifest))
        {
            throw new ThemeParseException(name, $"Theme.xml was not found in {directory}.");
        }

        XDocument document;

        try
        {
            document = XDocument.Load(manifest, LoadOptions.SetLineInfo);
        }
        catch (Exception ex)
        {
            throw new ThemeParseException(name, $"Theme.xml is not valid XML: {ex.Message}");
        }

        var root = document.Root
            ?? throw new ThemeParseException(name, "Theme.xml has no root element.");

        if (root.Name.LocalName is not ("VoidstrapCustomBootstrapper" or "BloxstrapCustomBootstrapper"))
        {
            throw new ThemeParseException(name,
                $"Unexpected root element <{root.Name.LocalName}>. Expected VoidstrapCustomBootstrapper or BloxstrapCustomBootstrapper.");
        }

        var panel = root.Elements().FirstOrDefault(e => e.Name.LocalName == "WebPanel")
            ?? throw new ThemeParseException(name,
                "This theme has no <WebPanel>. Jello renders HTML themes only, so native-element themes are not supported.");

        string? source = panel.Attribute("Source")?.Value;

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ThemeParseException(name, "<WebPanel> has no Source attribute.");
        }

        var titleBar = root.Elements().FirstOrDefault(e => e.Name.LocalName == "TitleBar");

        var theme = new BootstrapperTheme
        {
            Name = name,
            Directory = directory,
            Width = ReadDouble(root, "Width", 420, name),
            Height = ReadDouble(root, "Height", 330, name),
            RoundedCorners = !string.Equals(root.Attribute("WindowCornerPreference")?.Value, "DoNotRound", StringComparison.OrdinalIgnoreCase),
            TitleBarTitle = titleBar?.Attribute("Title")?.Value ?? "",
            ShowMinimize = ReadBool(titleBar, "ShowMinimize", false),
            ShowClose = ReadBool(titleBar, "ShowClose", false),
            PanelPath = ""
        };

        string panelPath = theme.Resolve(source);

        if (!File.Exists(panelPath))
        {
            throw new ThemeParseException(name, $"WebPanel Source '{source}' resolves to {panelPath}, which does not exist.");
        }

        theme = new BootstrapperTheme
        {
            Name = theme.Name,
            Directory = theme.Directory,
            Width = theme.Width,
            Height = theme.Height,
            RoundedCorners = theme.RoundedCorners,
            TitleBarTitle = theme.TitleBarTitle,
            ShowMinimize = theme.ShowMinimize,
            ShowClose = theme.ShowClose,
            PanelPath = panelPath
        };

        Log.Write(ident, $"Loaded '{name}' ({theme.Width}x{theme.Height}) panel {Path.GetFileName(panelPath)}");

        return theme;
    }

    private static double ReadDouble(XElement element, string attribute, double fallback, string themeName)
    {
        string? raw = element.Attribute(attribute)?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            || value <= 0)
        {
            throw new ThemeParseException(themeName, $"{attribute}='{raw}' is not a valid size.");
        }

        return value;
    }

    private static bool ReadBool(XElement? element, string attribute, bool fallback)
    {
        string? raw = element?.Attribute(attribute)?.Value;

        return bool.TryParse(raw, out bool value) ? value : fallback;
    }
}
