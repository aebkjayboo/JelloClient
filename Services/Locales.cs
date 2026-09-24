using System.Globalization;

namespace JelloClient.Services;

/// Region formatting for the app.
///
/// Jello ships no translations, so this deliberately does not claim to change the
/// language: it sets the culture every window formats dates, numbers and file sizes
/// with. "Match Windows" means whatever the machine is set to.
internal static class Locales
{
    public sealed record Option(string? Name, string Label);

    private static readonly string[] Offered =
    {
        "en-US", "en-GB", "de-DE", "fr-FR", "es-ES", "pt-BR", "it-IT",
        "nl-NL", "pl-PL", "tr-TR", "ru-RU", "uk-UA", "ja-JP", "ko-KR",
        "zh-CN", "zh-TW", "id-ID", "th-TH", "vi-VN"
    };

    public static IReadOnlyList<Option> All()
    {
        var options = new List<Option> { new(null, $"Match Windows ({CultureInfo.CurrentCulture.DisplayName})") };

        foreach (string name in Offered)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(name);
                options.Add(new Option(name, $"{culture.DisplayName} - {culture.Name}"));
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return options;
    }

    public static void Apply(string? name)
    {
        const string ident = "Locales::Apply";

        try
        {
            var culture = string.IsNullOrEmpty(name)
                ? CultureInfo.InstalledUICulture
                : CultureInfo.GetCultureInfo(name);

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.CurrentCulture = culture;

            Log.Write(ident, $"Formatting dates and numbers as {culture.Name}");
        }
        catch (Exception ex)
        {
            Log.Write(ident, $"Could not apply the {name} locale: {ex.Message}");
        }
    }

    public static string Describe(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return $"Dates and numbers follow Windows ({CultureInfo.CurrentCulture.DisplayName}).";
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(name);

            return $"Dates and numbers use {culture.DisplayName}. Jello's own text is English only.";
        }
        catch (CultureNotFoundException)
        {
            return "That region is not installed on this machine.";
        }
    }
}
