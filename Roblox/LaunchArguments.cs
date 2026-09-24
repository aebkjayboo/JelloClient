using System.Text.RegularExpressions;
using System.Web;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Reads and rewrites the argument string Roblox is started with.
///
/// A launch from the website looks like
///
///     roblox-player:1+launchmode:play+gameinfo:TICKET+placelauncherurl:https://...
///
/// where placelauncherurl is itself a URL with placeId and, when a particular server is
/// wanted, gameId. Sending someone to a chosen server means putting a gameId into that
/// inner URL, which is the same thing the website does when you click a specific server.
internal static class LaunchArguments
{
    private static readonly Regex PlaceLauncher = new(
        @"placelauncherurl:([^\+]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlaceIdOnly = new(
        @"placeId[=:](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// The place being launched, or 0 when the arguments do not name one - which is the
    /// case for a plain "open Roblox" with no game.
    public static long PlaceIdOf(string launchArguments)
    {
        if (string.IsNullOrEmpty(launchArguments))
        {
            return 0;
        }

        var launcher = PlaceLauncher.Match(launchArguments);

        if (launcher.Success)
        {
            string inner = Uri.UnescapeDataString(launcher.Groups[1].Value);

            var fromInner = PlaceIdOnly.Match(inner);

            if (fromInner.Success && long.TryParse(fromInner.Groups[1].Value, out long place))
            {
                return place;
            }
        }

        var direct = PlaceIdOnly.Match(launchArguments);

        return direct.Success && long.TryParse(direct.Groups[1].Value, out long id) ? id : 0;
    }

    /// Points the launch at one particular server. When the arguments carry a
    /// placelauncherurl its query is edited in place; otherwise a fresh one is built, which
    /// is what a launch straight from Jello needs.
    public static string WithJob(string launchArguments, long placeId, string jobId)
    {
        const string ident = "LaunchArguments::WithJob";

        if (string.IsNullOrEmpty(jobId))
        {
            return launchArguments;
        }

        try
        {
            var launcher = PlaceLauncher.Match(launchArguments);

            if (!launcher.Success)
            {
                Log.Write(ident, $"No placelauncherurl in the arguments, joining {jobId} by a fresh one");

                string built = HttpUtility.UrlEncode(
                    "https://assetgame.roblox.com/game/PlaceLauncher.ashx"
                    + "?request=RequestGameJob"
                    + $"&placeId={placeId}"
                    + $"&gameId={jobId}"
                    + "&isPlayTogetherGame=false");

                return string.IsNullOrEmpty(launchArguments)
                    ? $"roblox-player:1+launchmode:play+placelauncherurl:{built}"
                    : $"{launchArguments}+placelauncherurl:{built}";
            }

            string inner = Uri.UnescapeDataString(launcher.Groups[1].Value);

            var uri = new UriBuilder(inner);
            var query = HttpUtility.ParseQueryString(uri.Query);

            // RequestGame asks for any server; RequestGameJob asks for this one.
            query["request"] = "RequestGameJob";
            query["gameId"] = jobId;
            query["placeId"] = placeId.ToString();

            uri.Query = query.ToString();

            string replaced = HttpUtility.UrlEncode(uri.Uri.ToString());

            return launchArguments[..launcher.Index]
                 + $"placelauncherurl:{replaced}"
                 + launchArguments[(launcher.Index + launcher.Length)..];
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);

            // A launch that works beats a launch aimed at the right server, so on any
            // doubt the original arguments are used.
            return launchArguments;
        }
    }
}
