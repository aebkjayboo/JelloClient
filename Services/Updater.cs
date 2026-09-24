using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using JelloClient.Roblox;

namespace JelloClient.Services;

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

internal enum UpdateOutcome
{
    UpToDate,
    Available,
    Unavailable
}

internal readonly record struct UpdateCheck(UpdateOutcome Outcome, Version? Version, GitHubRelease? Release, string Message);

internal sealed class Updater
{
    private const string ReleasesUrl = "https://api.github.com/repos/aebkjayboo/JelloClient/releases/latest";
    private const string ExecutableName = "JelloClient.exe";
    private const string BackupSuffix = ".old";

    private readonly HttpClient _http;

    public Updater(HttpClient http)
    {
        _http = http;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 0, 0);

    public static void CleanUpPreviousUpdate()
    {
        string path = ProtocolHandler.ApplicationPath;

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string backup = path + BackupSuffix;

        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
                Log.Write("Updater::CleanUpPreviousUpdate", "Removed the previous executable left by an update");
            }
        }
        catch (Exception ex)
        {
            Log.Write("Updater::CleanUpPreviousUpdate", $"Could not remove {backup}: {ex.Message}");
        }
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        Log.Write("Updater::Check", $"Querying {ReleasesUrl}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Write("Updater::Check", "The releases endpoint returned 404");
                return new UpdateCheck(UpdateOutcome.Unavailable, null, null, "No published releases were found.");
            }

            response.EnsureSuccessStatusCode();

            var release = await response.Content
                .ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct)
                .ConfigureAwait(false);

            if (release is null || string.IsNullOrEmpty(release.Tag))
            {
                Log.Write("Updater::Check", "The releases endpoint returned an empty payload");
                return new UpdateCheck(UpdateOutcome.Unavailable, null, null, "The release feed returned nothing usable.");
            }

            if (!TryParseTag(release.Tag, out var latest))
            {
                Log.Write("Updater::Check", $"Could not read a version out of the tag '{release.Tag}'");
                return new UpdateCheck(UpdateOutcome.Unavailable, null, release, $"Could not read a version out of the tag '{release.Tag}'.");
            }

            var current = CurrentVersion;

            Log.Write("Updater::Check", $"Current {current.ToString(3)}, latest {latest.ToString(3)}");

            if (latest <= current)
            {
                return new UpdateCheck(UpdateOutcome.UpToDate, latest, release, $"Jello {current.ToString(3)} is up to date.");
            }

            return new UpdateCheck(UpdateOutcome.Available, latest, release, $"Jello {latest.ToString(3)} is available.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.WriteException("Updater::Check", ex);
            return new UpdateCheck(UpdateOutcome.Unavailable, null, null, $"Could not check for updates: {ex.Message}");
        }
    }

    public async Task<string> DownloadAsync(GitHubRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, ExecutableName, StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Release {release.Tag} has no executable asset.");

        string directory = Path.Combine(Paths.Base, "Updates");
        Directory.CreateDirectory(directory);

        string destination = Path.Combine(directory, $"JelloClient-{release.Tag}.exe");

        Log.Write("Updater::Download", $"Downloading {asset.Name} ({asset.Size} bytes) from {asset.DownloadUrl}");

        using var response = await _http
            .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? asset.Size;
        long received = 0;

        await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
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

                received += read;

                if (total > 0)
                {
                    progress?.Report((double)received / total);
                }
            }
        }

        Log.Write("Updater::Download", $"Wrote {received} bytes to {destination}");

        return destination;
    }

    public static void ApplyAndRestart(string downloadedPath)
    {
        string current = ProtocolHandler.ApplicationPath;

        if (string.IsNullOrEmpty(current))
        {
            throw new InvalidOperationException("The running executable path could not be determined.");
        }

        string backup = current + BackupSuffix;

        Log.Write("Updater::Apply", $"Swapping {current} for {downloadedPath}");

        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        File.Move(current, backup);

        try
        {
            File.Copy(downloadedPath, current, true);
        }
        catch
        {
            File.Move(backup, current, true);
            throw;
        }

        Log.Write("Updater::Apply", "Restarting into the new build");
        Log.Stop();

        Process.Start(new ProcessStartInfo
        {
            FileName = current,
            WorkingDirectory = Path.GetDirectoryName(current)!,
            UseShellExecute = true
        });
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        string trimmed = tag.TrimStart('v', 'V').Trim();

        int end = 0;

        while (end < trimmed.Length && (char.IsAsciiDigit(trimmed[end]) || trimmed[end] == '.'))
        {
            end++;
        }

        return Version.TryParse(trimmed[..end], out version!);
    }
}
