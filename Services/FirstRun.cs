using System.Security.Cryptography;
using System.Text.Json;
using JelloClient.Roblox;
using Microsoft.Win32;

namespace JelloClient.Services;

internal sealed class InstallMarker
{
    public string Version { get; set; } = "";

    public string Location { get; set; } = "";

    /// SHA-256 (hex) of the primary payload that was installed (JelloClient.dll for a
    /// framework build, else JelloClient.exe). Lets a later run tell whether the installed
    /// copy is actually the same code, not just the same version number. Older markers written
    /// before this field simply leave it empty, which reads as "unknown" and triggers one
    /// self-healing refresh.
    public string Sha256 { get; set; } = "";

    public DateTime InstalledUtc { get; set; }
}

/// The first run installation, modelled on Bloxstrap's Installer: pick a location,
/// validate it can actually be written to, copy the executable there, record the install
/// in the registry the way an installed program is expected to, register the shortcuts and
/// the Roblox protocol, and hand off to the launcher.
internal static class FirstRun
{
    private const string SettingsKey = @"Software\JelloClient";

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\JelloClient";

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jello Client.lnk");

    public static string StartMenuShortcut =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "Jello Client.lnk");

    /// Where a previous install put itself, straight from the registry. Read before
    /// anything touches Paths, since it decides where the log and settings live.
    public static string? RecordedLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);

            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// An install is real when the registry points somewhere that holds both our marker
    /// file and our executable. A Settings.json on its own proves nothing: the app writes
    /// one the first time it runs from anywhere, portable copies included.
    public static bool IsInstalled()
    {
        string? location = RecordedLocation();

        if (string.IsNullOrEmpty(location))
        {
            return false;
        }

        string marker = Path.Combine(location, "Install.json");
        string executable = Path.Combine(location, "JelloClient.exe");

        bool installed = File.Exists(marker) && File.Exists(executable);

        if (!installed)
        {
            Log.Write("FirstRun::IsInstalled",
                $"{location} is recorded but " +
                $"{(File.Exists(marker) ? "" : "Install.json ")}{(File.Exists(executable) ? "" : "JelloClient.exe ")}is missing");
        }

        return installed;
    }

    public static string DescribeLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return "Choose where Jello should install itself.";
        }

        if (File.Exists(Path.Combine(location, "Install.json")))
        {
            return "Jello is already installed here. Continuing will reuse the settings and flags already in this folder.";
        }

        return Directory.Exists(location) && Directory.EnumerateFileSystemEntries(location).Any()
            ? "This folder is not empty. Jello will add its own files alongside what is already there."
            : "";
    }

    /// The rules Bloxstrap uses, for the same reasons: those locations either need
    /// elevation, get synced by something else, or are cleaned out from under you.
    public static string? Validate(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return "Pick a folder first.";
        }

        if (location.Length <= 3)
        {
            return "Jello cannot install to the root of a drive.";
        }

        if (location.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return "Network locations are not supported.";
        }

        if (location.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
            || location.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows clears the temp folder, so Jello cannot live there.";
        }

        if (location.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
        {
            return "OneDrive syncs and locks files, which breaks the Roblox install underneath it.";
        }

        if (location.Contains("Program Files", StringComparison.OrdinalIgnoreCase))
        {
            return "Program Files needs administrator rights for every update. Pick somewhere in your user folder.";
        }

        string? parent = Directory.GetParent(location)?.FullName;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.Equals(parent, profile, StringComparison.OrdinalIgnoreCase))
        {
            return "That is one of your profile folders. Use a subfolder such as AppData instead.";
        }

        try
        {
            Directory.CreateDirectory(location);

            string test = Path.Combine(location, "JelloWriteTest.tmp");

            File.WriteAllText(test, "");
            File.Delete(test);
        }
        catch (UnauthorizedAccessException)
        {
            return "Jello does not have permission to write there.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return null;
    }

    public sealed record Options(
        string Location,
        bool DesktopShortcut,
        bool StartMenuShortcut,
        bool RegisterProtocol,
        bool RunAtStartup,
        AppTheme Theme,
        string? Locale);

    public static void Install(Options options, bool implicitInstall = false)
    {
        const string ident = "FirstRun::Install";

        Log.Write(ident, $"Installing to {options.Location} (implicit: {implicitInstall})");

        Directory.CreateDirectory(options.Location);
        Paths.Initialize(options.Location);
        Paths.EnsureCreated();

        CopyExecutable(options.Location);
        WriteMarker(options.Location);
        WriteRegistry(options.Location);

        if (options.RegisterProtocol)
        {
            ProtocolHandler.RegisterPlayer();
        }

        if (options.DesktopShortcut)
        {
            CreateShortcut(DesktopShortcut, options.Location);
        }

        if (options.StartMenuShortcut)
        {
            CreateShortcut(StartMenuShortcut, options.Location);
        }

        Startup.Set(options.RunAtStartup, Path.Combine(options.Location, "JelloClient.exe"));

        var settings = AppState.Settings;

        settings.Theme = options.Theme;
        settings.Locale = options.Locale;
        settings.FirstRunComplete = true;

        AppState.Persist();

        Log.Write(ident, "Installation finished");
    }

    /// Removes Jello, and only Jello: its autostart, protocol registration, registry entries,
    /// shortcuts, and its own install/data folder. Nothing outside Jello's own footprint is
    /// touched. The running executable cannot delete itself, so a short detached command does
    /// the final folder removal once this process has exited.
    public static void Uninstall()
    {
        const string ident = "FirstRun::Uninstall";

        string location = RecordedLocation() ?? Path.GetDirectoryName(ProtocolHandler.ApplicationPath) ?? Paths.Base;

        Try(() => Startup.RemoveAgentAutostart(), "autostart");
        Try(() => ProtocolHandler.UnregisterPlayer(), "protocol");
        Try(() => Registry.CurrentUser.DeleteSubKeyTree(SettingsKey, throwOnMissingSubKey: false), "settings key");
        Try(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false), "uninstall key");
        Try(() => File.Delete(DesktopShortcut), "desktop shortcut");
        Try(() => File.Delete(StartMenuShortcut), "start menu shortcut");

        Log.Write(ident, $"Removed registry, shortcuts and autostart; scheduling folder removal of {location}");
        Log.Stop();

        // Wait for this process to exit, then delete the whole install/data folder.
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{location}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception)
        {
            // Nothing more we can do from here; the folder simply stays.
        }
    }

    private static void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Write("FirstRun::Uninstall", $"Could not remove {what}: {ex.Message}");
        }
    }

    private static void CopyExecutable(string location)
    {
        string source = ProtocolHandler.ApplicationPath;
        string destination = Path.Combine(location, "JelloClient.exe");

        if (string.IsNullOrEmpty(source)
            || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(source))
        {
            return;
        }

        try
        {
            File.Copy(source, destination, true);
            Log.Write("FirstRun::CopyExecutable", $"Copied {source} to {destination}");

            CopyRuntimeFiles(Path.GetDirectoryName(source)!, location);
        }
        catch (Exception ex)
        {
            Log.WriteException("FirstRun::CopyExecutable", ex);

            throw new IOException(
                $"Jello could not copy itself into {location}. Close any running copy and try again.", ex);
        }
    }

    /// A released Jello is one published file, so the copy above is the whole install. A
    /// build straight out of the IDE is not: the exe next to it is only a launcher for
    /// JelloClient.dll, and copying it alone leaves an install that cannot start. When
    /// those files are present they come along too.
    private static void CopyRuntimeFiles(string from, string location)
    {
        string[] runtime = { "JelloClient.dll", "JelloClient.runtimeconfig.json", "JelloClient.deps.json" };

        if (!runtime.All(name => File.Exists(Path.Combine(from, name))))
        {
            return;
        }

        int copied = 0;

        foreach (string source in Directory.EnumerateFiles(from))
        {
            string name = Path.GetFileName(source);

            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "JelloClient.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Copy(source, Path.Combine(location, name), true);
                copied++;
            }
            catch (Exception ex)
            {
                Log.Write("FirstRun::CopyRuntimeFiles", $"Skipped {name}: {ex.Message}");
            }
        }

        Log.Write("FirstRun::CopyRuntimeFiles", $"Copied {copied} runtime file(s) alongside the executable");
    }

    private static void WriteMarker(string location)
    {
        var marker = new InstallMarker
        {
            Version = Updater.CurrentVersion.ToString(3),
            Location = location,
            Sha256 = HashPayload(PrimaryPayload(location)),
            InstalledUtc = DateTime.UtcNow
        };

        File.WriteAllText(
            Path.Combine(location, "Install.json"),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// The file that actually carries the code, so "is the install the latest?" is answered
    /// against real bytes. A released Jello is one published .exe; a framework build is a thin
    /// .exe launcher next to JelloClient.dll - there the .dll is the payload.
    private static string PrimaryPayload(string directory)
    {
        string dll = Path.Combine(directory, "JelloClient.dll");

        return File.Exists(dll) ? dll : Path.Combine(directory, "JelloClient.exe");
    }

    private static string HashPayload(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static InstallMarker? ReadMarker(string location)
    {
        try
        {
            string path = Path.Combine(location, "Install.json");

            return JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Write("FirstRun::ReadMarker", $"Could not read the install marker: {ex.Message}");
            return null;
        }
    }

    /// Keeps the installed copy honest. On every normal start (once we know Jello is installed)
    /// this compares the build that is running right now against the one sitting in the install
    /// folder, and refreshes the install when the running build is newer, or is the same version
    /// but different code. It only ever acts when the running copy is somewhere OTHER than the
    /// install folder - i.e. you launched a fresh build or a downloaded exe - so launching the
    /// installed copy itself is always a no-op. It never downgrades: an older build run by hand
    /// leaves a newer install alone.
    public static void SyncInstallIfStale()
    {
        try
        {
            string? location = RecordedLocation();

            if (string.IsNullOrEmpty(location) || !IsInstalled())
            {
                return;
            }

            string installedExe = Path.Combine(location, "JelloClient.exe");
            string runningExe = ProtocolHandler.ApplicationPath;
            string? runningDir = Path.GetDirectoryName(runningExe);

            // Running the installed copy itself: there is nothing newer to copy from.
            if (string.IsNullOrEmpty(runningDir)
                || string.Equals(runningExe, installedExe, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var marker = ReadMarker(location);
            Version installedVersion = Version.TryParse(marker?.Version, out var mv) ? mv : new Version(0, 0, 0);
            Version runningVersion = Updater.CurrentVersion;

            string reason;

            if (runningVersion > installedVersion)
            {
                reason = $"running {runningVersion.ToString(3)} is newer than installed {installedVersion.ToString(3)}";
            }
            else if (runningVersion < installedVersion)
            {
                Log.Write("FirstRun::Sync",
                    $"Running {runningVersion.ToString(3)} is older than installed {installedVersion.ToString(3)}; leaving the install alone");
                return;
            }
            else
            {
                // Same version - compare the actual code so a rebuilt-but-not-bumped build still
                // refreshes the install. Trust the installed file's real bytes, not the marker.
                string installedHash = HashPayload(PrimaryPayload(location));
                string runningHash = HashPayload(PrimaryPayload(runningDir));

                if (installedHash.Length == 0 || runningHash.Length == 0
                    || string.Equals(installedHash, runningHash, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                reason = $"same version {runningVersion.ToString(3)} but the installed code differs";
            }

            Log.Write("FirstRun::Sync", $"Refreshing the install at {location}: {reason}");

            RefreshInstall(location);
        }
        catch (Exception ex)
        {
            Log.WriteException("FirstRun::Sync", ex);
        }
    }

    /// Copies the running build over the installed one and rewrites the marker + registry to
    /// match. Any background agent is told to stand down first so it releases the installed exe,
    /// and the exe is moved aside (not overwritten in place) so a locked target never blocks the
    /// refresh - CleanUpPreviousUpdate clears the .old on the next run.
    private static void RefreshInstall(string location)
    {
        App.SignalAgentStop();

        string source = ProtocolHandler.ApplicationPath;
        string sourceDir = Path.GetDirectoryName(source)!;
        string destExe = Path.Combine(location, "JelloClient.exe");

        // Move the installed exe aside if it is there, so a running copy can't lock the copy.
        try
        {
            if (File.Exists(destExe))
            {
                string aside = destExe + ".old";

                if (File.Exists(aside))
                {
                    File.Delete(aside);
                }

                File.Move(destExe, aside);
            }
        }
        catch (Exception ex)
        {
            Log.Write("FirstRun::Refresh", $"Could not move the old exe aside: {ex.Message}");
        }

        File.Copy(source, destExe, true);
        CopyRuntimeFiles(sourceDir, location);

        WriteMarker(location);
        WriteRegistry(location);

        Log.Write("FirstRun::Refresh", $"Install refreshed to {Updater.CurrentVersion.ToString(3)}");
    }

    private static void WriteRegistry(string location)
    {
        string executable = Path.Combine(location, "JelloClient.exe");

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                key.SetValue("InstallLocation", location);
                key.SetValue("Version", Updater.CurrentVersion.ToString(3));
            }

            using (var uninstall = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                uninstall.SetValue("DisplayName", "Jello Client");
                uninstall.SetValue("DisplayIcon", $"{executable},0");
                uninstall.SetValue("DisplayVersion", Updater.CurrentVersion.ToString(3));
                uninstall.SetValue("InstallLocation", location);
                uninstall.SetValue("Publisher", "Jello");
                uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                uninstall.SetValue("ModifyPath", $"\"{executable}\" -settings");
                uninstall.SetValue("UninstallString", $"\"{executable}\" -uninstall");

                if (uninstall.GetValue("InstallDate") is null)
                {
                    uninstall.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
            }
        }
        catch (Exception ex)
        {
            Log.WriteException("FirstRun::WriteRegistry", ex);
        }
    }

    private static void CreateShortcut(string path, string location)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var link = (IShellLinkW)new ShellLinkObject();

            link.SetPath(Path.Combine(location, "JelloClient.exe"));
            link.SetWorkingDirectory(location);
            link.SetDescription("Jello Client");
            link.SetIconLocation(Path.Combine(location, "JelloClient.exe"), 0);

            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, false);

            Log.Write("FirstRun::CreateShortcut", $"Wrote {path}");
        }
        catch (Exception ex)
        {
            Log.WriteException("FirstRun::CreateShortcut", ex);
        }
    }
}
