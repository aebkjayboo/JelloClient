using System.Diagnostics;
using Microsoft.Win32;

namespace JelloClient.Services;

internal static class ProtocolHandler
{
    private static readonly string[] PlayerKeys = { "roblox", "roblox-player" };

    public static string ApplicationPath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    public static bool IsPlayerRegistered()
    {
        try
        {
            string expected = BuildCommand(ApplicationPath);

            foreach (string key in PlayerKeys)
            {
                using var command = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{key}\shell\open\command");

                if (command?.GetValue("") as string != expected)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.WriteException("ProtocolHandler::IsPlayerRegistered", ex);
            return false;
        }
    }

    public static void RegisterPlayer()
    {
        string handler = ApplicationPath;

        foreach (string key in PlayerKeys)
        {
            RegisterProtocol(key, "Roblox", handler);
        }

        Log.Write("ProtocolHandler::RegisterPlayer", $"Registered {string.Join(", ", PlayerKeys)} to {handler}");
    }

    public static void UnregisterPlayer()
    {
        foreach (string key in PlayerKeys)
        {
            Unregister(key);
        }
    }

    public static void ReassertIfRegistered()
    {
        try
        {
            using var existing = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{PlayerKeys[0]}\shell\open\command");

            if (existing?.GetValue("") is not string command || !command.Contains("JelloClient", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsPlayerRegistered())
            {
                return;
            }

            Log.Write("ProtocolHandler::ReassertIfRegistered", "Handler path drifted, re-registering");
            RegisterPlayer();
        }
        catch (Exception ex)
        {
            Log.WriteException("ProtocolHandler::ReassertIfRegistered", ex);
        }
    }

    private static string BuildCommand(string handler) => $"\"{handler}\" -player \"%1\"";

    private static void RegisterProtocol(string key, string name, string handler)
    {
        using var uriKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{key}");
        using var iconKey = uriKey.CreateSubKey("DefaultIcon");
        using var commandKey = uriKey.CreateSubKey(@"shell\open\command");

        string command = BuildCommand(handler);

        if (uriKey.GetValue("") is not string existing || !existing.StartsWith("URL:", StringComparison.Ordinal))
        {
            uriKey.SetValue("", $"URL: {name} Protocol");
        }

        uriKey.SetValue("URL Protocol", "");

        if (iconKey.GetValue("") as string != handler)
        {
            iconKey.SetValue("", handler);
        }

        if (commandKey.GetValue("") as string != command)
        {
            commandKey.SetValue("", command);
        }
    }

    private static void Unregister(string key)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{key}", false);
            Log.Write("ProtocolHandler::Unregister", $"Removed {key}");
        }
        catch (Exception ex)
        {
            Log.WriteException("ProtocolHandler::Unregister", ex);
        }
    }
}
