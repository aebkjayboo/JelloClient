using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal readonly record struct InstallProgress(string Stage, double Fraction, bool Indeterminate = false);

internal sealed class InstallState
{
    public string VersionGuid { get; set; } = "";

    public string Version { get; set; } = "";

    public string Channel { get; set; } = Deployment.DefaultChannel;

    public DateTime InstalledUtc { get; set; }
}

internal sealed class Installer
{
    private const string AppSettingsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Settings>\r\n" +
        "\t<ContentFolder>content</ContentFolder>\r\n" +
        "\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
        "</Settings>\r\n";

    public const string ExecutableName = "RobloxPlayerBeta.exe";
    private const int MaxDownloadAttempts = 5;

    private readonly HttpClient _http;

    public Installer(HttpClient http)
    {
        _http = http;
    }

    public static string? FindInstalledExecutable(string versionGuid)
    {
        string path = Path.Combine(Paths.VersionDirectory(versionGuid), ExecutableName);
        return File.Exists(path) ? path : null;
    }

    public static void ClearInstall()
    {
        if (!Directory.Exists(Paths.Versions))
        {
            return;
        }

        Log.Write("Installer::ClearInstall", $"Deleting {Paths.Versions}");

        Directory.Delete(Paths.Versions, true);

        if (File.Exists(Paths.StateFile))
        {
            File.Delete(Paths.StateFile);
        }
    }

    public static InstallState? ReadState()
    {
        try
        {
            if (!File.Exists(Paths.StateFile))
            {
                return null;
            }

            return JsonSerializer.Deserialize<InstallState>(File.ReadAllText(Paths.StateFile));
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(InstallState state)
    {
        Log.Write("Installer::WriteState", $"{state.Version} ({state.VersionGuid}) on the {state.Channel} channel");

        File.WriteAllText(
            Paths.StateFile,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<string> EnsureLatestAsync(
        string channel,
        IReadOnlyDictionary<string, object> fastFlags,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        Paths.EnsureCreated();

        Log.Write("Installer::EnsureLatest", $"Starting for the {channel} channel with {fastFlags.Count} fast flag(s)");

        AutoCleaner.RunOnLaunch();

        progress?.Report(new InstallProgress("Connecting to Roblox...", 0, true));
        await Deployment.InitializeConnectivityAsync(_http, ct).ConfigureAwait(false);

        progress?.Report(new InstallProgress("Resolving the latest version...", 0, true));
        var clientVersion = await Deployment.GetInfoAsync(_http, channel, ct).ConfigureAwait(false);

        string versionDirectory = Paths.VersionDirectory(clientVersion.VersionGuid);
        string executablePath = Path.Combine(versionDirectory, ExecutableName);

        if (File.Exists(executablePath))
        {
            Log.Write("Installer::EnsureLatest", $"{clientVersion.VersionGuid} is already installed, restaging only");

            progress?.Report(new InstallProgress("Applying Roblox modifications...", 0, true));
            await StageAsync(versionDirectory, fastFlags, ct).ConfigureAwait(false);

            WriteState(new InstallState
            {
                VersionGuid = clientVersion.VersionGuid,
                Version = clientVersion.Version,
                Channel = channel,
                InstalledUtc = DateTime.UtcNow
            });

            return executablePath;
        }

        progress?.Report(new InstallProgress("Fetching the package manifest...", 0, true));

        string manifestUrl = Deployment.GetLocation($"/{clientVersion.VersionGuid}-rbxPkgManifest.txt");

        string manifestData = await RobloxHttp.GetStringAsync(
            _http,
            manifestUrl,
            "Fetching the package manifest",
            $"Roblox {clientVersion.Version} is listed for the {channel} channel but its manifest is not on the CDN yet. Wait a few minutes and try again.",
            ct).ConfigureAwait(false);

        var manifest = new PackageManifest(manifestData);

        Log.Write("Installer::EnsureLatest", $"Manifest carries {manifest.TotalPackedSize} packed bytes");

        Directory.CreateDirectory(versionDirectory);

        long totalBytes = Math.Max(manifest.TotalPackedSize, 1);
        long downloadedBytes = 0;

        var extractions = new List<Task>();

        foreach (var package in manifest)
        {
            ct.ThrowIfCancellationRequested();

            if (!PackageMap.Player.ContainsKey(package.Name))
            {
                Log.Write("Installer::EnsureLatest", $"Skipping {package.Name}, not a player package");
                continue;
            }

            await DownloadPackageAsync(clientVersion.VersionGuid, package, bytes =>
            {
                long current = Interlocked.Add(ref downloadedBytes, bytes);
                double fraction = (double)current / totalBytes;
                progress?.Report(new InstallProgress("Installing Roblox...", Math.Clamp(fraction, 0, 1)));
            }, ct).ConfigureAwait(false);

            var local = package;
            extractions.Add(Task.Run(() => ExtractPackage(local, versionDirectory), ct));
        }

        progress?.Report(new InstallProgress("Configuring Roblox...", 0, true));
        Log.Write("Installer::EnsureLatest", $"Waiting on {extractions.Count} extraction task(s)");
        await Task.WhenAll(extractions).ConfigureAwait(false);

        progress?.Report(new InstallProgress("Applying Roblox modifications...", 0, true));
        await StageAsync(versionDirectory, fastFlags, ct).ConfigureAwait(false);

        if (!File.Exists(executablePath))
        {
            Log.Write("Installer::EnsureLatest", $"{ExecutableName} is missing from {versionDirectory}");

            throw new FileNotFoundException($"{ExecutableName} was not produced by the extraction.", executablePath);
        }

        WriteState(new InstallState
        {
            VersionGuid = clientVersion.VersionGuid,
            Version = clientVersion.Version,
            Channel = channel,
            InstalledUtc = DateTime.UtcNow
        });

        Log.Write("Installer::EnsureLatest", $"Installed {clientVersion.Version} to {versionDirectory}");

        return executablePath;
    }

    private async Task StageAsync(
        string versionDirectory,
        IReadOnlyDictionary<string, object> fastFlags,
        CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "AppSettings.xml"),
            AppSettingsXml,
            ct).ConfigureAwait(false);

        CustomFont.Synchronise(versionDirectory);
        ApplyModifications(versionDirectory);

        // Roblox reads ClientAppSettings.json once, at boot. A write that silently fails
        // looks exactly like flags that do nothing, so the file is flushed to disk and
        // read back before the client is allowed to start.
        bool written = await Task.Run(() => FastFlagWriter.WriteVerified(versionDirectory, fastFlags), ct)
            .ConfigureAwait(false);

        if (!written)
        {
            throw new IOException(
                $"Your FastFlags could not be written into {versionDirectory}. " +
                "Roblox would have started without them, so the launch was stopped instead.");
        }

        Log.Write("Installer::Stage", $"Wrote AppSettings.xml and {fastFlags.Count} fast flag(s) into {versionDirectory}");
    }

    private static void ApplyModifications(string versionDirectory)
    {
        if (!Directory.Exists(Paths.Modifications))
        {
            return;
        }

        int applied = 0;

        foreach (string source in Directory.EnumerateFiles(Paths.Modifications, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Paths.Modifications, source);
            string destination = Path.Combine(versionDirectory, relative);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
                applied++;
            }
            catch (Exception ex)
            {
                Log.Write("Installer::ApplyModifications", $"Could not copy {relative}: {ex.Message}");
            }
        }

        if (applied > 0)
        {
            Log.Write("Installer::ApplyModifications", $"Copied {applied} modification file(s)");
        }
    }

    private async Task DownloadPackageAsync(string versionGuid, Package package, Action<long> onBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Paths.Downloads);

        if (File.Exists(package.DownloadPath))
        {
            if (ComputeMd5(package.DownloadPath) == package.Signature)
            {
                Log.Write("Installer::DownloadPackage", $"{package.Name} is already cached");
                onBytes(package.PackedSize);
                return;
            }

            Log.Write("Installer::DownloadPackage", $"Cached {package.Name} failed verification, discarding");
            File.Delete(package.DownloadPath);
        }

        string stockCopy = Path.Combine(Paths.StockRobloxDownloads, package.Signature);

        if (File.Exists(stockCopy))
        {
            Log.Write("Installer::DownloadPackage", $"Reusing the stock Roblox blob for {package.Name}");
            File.Copy(stockCopy, package.DownloadPath, true);
            onBytes(package.PackedSize);
            return;
        }

        string url = Deployment.GetLocation($"/{versionGuid}-{package.Name}");

        for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            long attemptBytes = 0;

            try
            {
                using var response = await RobloxHttp.GetAsync(
                    _http,
                    url,
                    $"Downloading {package.Name}",
                    $"The CDN refused {package.Name} for version {versionGuid}. If this keeps happening the deployment may have been pulled; try again shortly.",
                    HttpCompletionOption.ResponseHeadersRead,
                    ct).ConfigureAwait(false);

                await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var file = new FileStream(package.DownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];

                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);

                        if (read == 0)
                        {
                            break;
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                        attemptBytes += read;
                        onBytes(read);
                    }
                }

                string hash = ComputeMd5(package.DownloadPath);

                if (hash != package.Signature)
                {
                    Log.Write("Installer::DownloadPackage", $"{package.Name} hashed to {hash}, expected {package.Signature}");

                    throw new InvalidDataException(
                        $"Checksum mismatch for {package.Name}. Expected {package.Signature}, got {hash}.");
                }

                Log.Write("Installer::DownloadPackage", $"Downloaded and verified {package.Name} ({attemptBytes} bytes)");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Write("Installer::DownloadPackage", $"Attempt {attempt} of {MaxDownloadAttempts} for {package.Name} failed: {ex.Message}");

                onBytes(-attemptBytes);

                if (File.Exists(package.DownloadPath))
                {
                    File.Delete(package.DownloadPath);
                }

                if (ex is RobloxRequestException rejected && !IsWorthRetrying(rejected.StatusCode))
                {
                    Log.Write("Installer::DownloadPackage", $"{(int)rejected.StatusCode} is a definitive rejection, not retrying");
                    throw;
                }

                if (attempt == MaxDownloadAttempts)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsWorthRetrying(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static void ExtractPackage(Package package, string versionDirectory)
    {
        if (!PackageMap.Player.TryGetValue(package.Name, out string? relativeDirectory))
        {
            return;
        }

        Log.Write("Installer::ExtractPackage", $"Extracting {package.Name} into {(relativeDirectory.Length == 0 ? "the version root" : relativeDirectory)}");

        string target = Path.Combine(versionDirectory, relativeDirectory);
        Directory.CreateDirectory(target);

        string targetFull = Path.GetFullPath(target);

        using var archive = ZipFile.OpenRead(package.DownloadPath);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            string destination = Path.GetFullPath(Path.Combine(target, entry.FullName));

            if (!destination.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = MD5.Create();

        byte[] hash = md5.ComputeHash(stream);

        var builder = new StringBuilder(hash.Length * 2);

        foreach (byte b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}
