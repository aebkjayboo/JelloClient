using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using JelloClient.Roblox;

namespace JelloClient.Services;

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkObject
{
}

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr data, int flags);
    void GetIDList(out IntPtr idList);
    void SetIDList(IntPtr idList);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxArgs);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon, int maxPath, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
    void Resolve(IntPtr hwnd, int flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

/// Creates a desktop shortcut that launches Roblox through Jello, with an icon of the
/// user's choosing.
///
/// This is the safe half of "change Roblox's icon": RobloxPlayerBeta.exe is Authenticode
/// signed by Roblox Corporation, so rewriting its PE icon resource would invalidate that
/// signature and risk tripping Hyperion. Bloxstrap does not patch it either - their
/// ExtractIconsTask only unpacks their bundled .ico files for the user to apply
/// themselves. This does the same, and then wires up the shortcut for you.
internal static class ShortcutManager
{
    public const string ShortcutName = "Roblox (Jello).lnk";

    public static string IconsDirectory => Path.Combine(Paths.Base, "Icons");

    public static string DesktopPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

    public static bool Exists => File.Exists(DesktopPath);

    /// Unpacks Jello's bundled icons so they can be picked without hunting for a file.
    public static IReadOnlyList<string> ExtractIcons()
    {
        const string ident = "ShortcutManager::ExtractIcons";

        Directory.CreateDirectory(IconsDirectory);

        var written = new List<string>();

        foreach (string name in new[] { "icon.ico" })
        {
            string destination = Path.Combine(IconsDirectory, $"Jello.{name}");

            try
            {
                if (!File.Exists(destination))
                {
                    var stream = System.Windows.Application
                        .GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!
                        .Stream;

                    using (stream)
                    using (var file = File.Create(destination))
                    {
                        stream.CopyTo(file);
                    }

                    Log.Write(ident, $"Extracted {destination}");
                }

                written.Add(destination);
            }
            catch (Exception ex)
            {
                Log.WriteException(ident, ex);
            }
        }

        return written;
    }

    public static void CreateOrUpdate(string? iconPath)
    {
        const string ident = "ShortcutManager::CreateOrUpdate";

        string target = ProtocolHandler.ApplicationPath;

        if (string.IsNullOrEmpty(target))
        {
            throw new InvalidOperationException("Jello's own path could not be determined.");
        }

        var link = (IShellLinkW)new ShellLinkObject();

        link.SetPath(target);
        link.SetArguments("-player");
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetDescription("Launch Roblox through Jello Client");

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            link.SetIconLocation(iconPath, 0);
        }
        else
        {
            link.SetIconLocation(target, 0);
        }

        ((IPersistFile)link).Save(DesktopPath, false);

        Log.Write(ident, $"Wrote {DesktopPath} with icon {iconPath ?? target}");
    }

    public static void Remove()
    {
        if (File.Exists(DesktopPath))
        {
            File.Delete(DesktopPath);
            Log.Write("ShortcutManager::Remove", "Desktop shortcut removed");
        }
    }
}
