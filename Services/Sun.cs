using System.Reflection;

namespace JelloClient.Services;

/// Ownership and branding for the product, and the one Discord invite everything points
/// people to. The invite lives in server.txt so it has a single home; it is embedded at
/// build time and read back here, with a constant fallback if that ever fails.
internal static class Sun
{
    public const string Owner = "Sun";

    /// The owner as it should read on screen and in documents, with the trademark mark.
    public const string OwnerMark = "Sun™";

    public const string Product = "Jello";

    public const string InformationChannel = "#information";

    private const string InviteFallback = "https://discord.gg/tpnP9NNBap";

    private static readonly Lazy<string> Invite = new(ReadInvite);

    public static string DiscordInvite => Invite.Value;

    private static string ReadInvite()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            string? resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("server.txt", StringComparison.OrdinalIgnoreCase));

            if (resource is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resource);

                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    string text = reader.ReadToEnd().Trim();

                    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return text;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Sun::ReadInvite", ex.Message);
        }

        return InviteFallback;
    }

    /// Opens the Discord invite in the person's browser. Failures are logged, not thrown -
    /// a browser that will not open should never stop the thing that asked for it.
    public static void OpenDiscord()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(DiscordInvite) { UseShellExecute = true });

            Log.Write("Sun::OpenDiscord", $"Opened {DiscordInvite}");
        }
        catch (Exception ex)
        {
            Log.WriteException("Sun::OpenDiscord", ex);
        }
    }
}
