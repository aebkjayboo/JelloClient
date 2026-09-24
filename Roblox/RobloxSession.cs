using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JelloClient.Services;
using Microsoft.Win32;

namespace JelloClient.Roblox;

/// Reads the Roblox login token that is already on this machine.
///
/// `.ROBLOSECURITY` is the whole account: anything holding it can act as the person until
/// they log out. So the rules here are narrow on purpose.
///
/// It is read only when a feature that cannot work without it is switched on, it is never
/// written anywhere, never logged, never shown in the interface, and it goes to exactly one
/// host - roblox.com - over TLS. Jello keeps it in memory for a couple of minutes and then
/// reads it again rather than holding it for the session.
///
/// Roblox keeps it in RobloxCookies.dat, encrypted with DPAPI under the current user, which
/// means this only ever decrypts what the person running Jello could already decrypt
/// themselves. Studio's copy in the registry is the fallback.
internal static class RobloxSession
{
    private static readonly Regex Warned = new(
        @"(_\|WARNING:-DO-NOT-SHARE[^\s;,""']+)", RegexOptions.Compiled);

    private static readonly Regex Named = new(
        @"\.ROBLOSECURITY[\s=]+([^\s;,""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private static string? _cached;
    private static DateTime _cachedAt = DateTime.MinValue;

    private static string CookiesPath =>
        Path.Combine(Paths.LocalAppData, "Roblox", "LocalStorage", "RobloxCookies.dat");

    /// True when a token can be read. Does not keep it.
    public static bool Available => Read() is not null;

    public static string? Read()
    {
        if (_cached is not null && DateTime.UtcNow - _cachedAt < CacheFor)
        {
            return _cached;
        }

        string? token = FromCookiesFile() ?? FromRegistry();

        _cached = token;
        _cachedAt = DateTime.UtcNow;

        return token;
    }

    public static void Forget()
    {
        _cached = null;
        _cachedAt = DateTime.MinValue;
    }

    private static string? FromCookiesFile()
    {
        const string ident = "RobloxSession::FromCookiesFile";

        try
        {
            if (!File.Exists(CookiesPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(CookiesPath));

            if (!document.RootElement.TryGetProperty("CookiesData", out var element))
            {
                return null;
            }

            string? encoded = element.GetString();

            if (string.IsNullOrEmpty(encoded))
            {
                return null;
            }

            byte[] plain = ProtectedData.Unprotect(
                Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser);

            return Extract(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            // Never the token itself, only why it could not be had.
            Log.Write(ident, $"Could not read the Roblox cookie store: {ex.Message}");
            return null;
        }
    }

    private static string? FromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Roblox\RobloxStudioBrowser\roblox.com");

            if (key is null)
            {
                return null;
            }

            foreach (string name in key.GetValueNames())
            {
                if (!name.Equals(".ROBLOSECURITY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (key.GetValue(name) is string blob && Extract(blob) is { } token)
                {
                    return token;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("RobloxSession::FromRegistry", ex.Message);
        }

        return null;
    }

    /// The stored blob holds the token among other fields; Roblox prefixes the real one
    /// with its own do-not-share warning.
    private static string? Extract(string blob)
    {
        if (string.IsNullOrEmpty(blob))
        {
            return null;
        }

        var warned = Warned.Match(blob);

        if (warned.Success)
        {
            return warned.Groups[1].Value.Trim();
        }

        var named = Named.Match(blob);

        return named.Success ? named.Groups[1].Value.Trim() : null;
    }
}
