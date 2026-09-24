using System.Text.Json;
using System.Text.RegularExpressions;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal enum ServerSessionJoinType
{
    SpecificGame = 0,
    NewGamePrivateGame = 1,
    SpecificPrivateGame = 2
}

internal sealed partial class ActivityWatcher : IDisposable
{
    private const string GameJoiningEntry = "[FLog::Output] ! Joining game";
    private const string GameJoiningUniverseEntry = "[FLog::GameJoinLoadTime] Report game_join_loadtime:";
    private const string GameJoiningUdmuxEntry = "[FLog::Network] UDMUX Address = ";
    private const string GameJoinedEntry = "[FLog::Network] Replicator created: ";
    private const string GameDisconnectedEntry = "[FLog::Network] Time to disconnect replication data:";
    private const string GameTeleportingEntry = "[FLog::UgcExperienceController] UgcExperienceController: doTeleport: joinScriptUrl";
    private const string GameLeavingEntry = "[FLog::SingleSurfaceApp] leaveUGCGameInternal";
    private const string GameMessageEntry = "[FLog::CreatorOutput] [JelloRPC]";

    private readonly CancellationTokenSource _cancellation = new();

    private bool _reservedTeleportMarker;
    private int _entriesRead;
    private bool _disposed;

    public event EventHandler? OnGameJoin;
    public event EventHandler? OnGameLeave;
    public event EventHandler? OnLogOpen;

    public ActivityWatcher(string? logFile = null)
    {
        LogLocation = logFile;
    }

    public string? LogLocation { get; private set; }

    public bool InGame { get; private set; }

    public ActivityData Data { get; private set; } = new();

    public async Task StartAsync()
    {
        const string ident = "ActivityWatcher::Start";

        try
        {
            string? path = LogLocation ?? await FindLatestLogAsync(_cancellation.Token).ConfigureAwait(false);

            if (path is null)
            {
                Log.Write(ident, "No Roblox log directory, activity tracking is inactive");
                return;
            }

            LogLocation = path;
            Log.Write(ident, $"Tailing {path}");

            OnLogOpen?.Invoke(this, EventArgs.Empty);

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            while (!_cancellation.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);

                if (line is null)
                {
                    await Task.Delay(1000, _cancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        ReadLogEntry(line);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteException(ident, ex);
                        Log.Write(ident, $"Skipping the entry that caused it: {line}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Write(ident, "Stopped");
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
        }
    }

    private static async Task<string?> FindLatestLogAsync(CancellationToken ct)
    {
        string directory = Path.Combine(Paths.LocalAppData, "Roblox", "logs");

        if (!Directory.Exists(directory))
        {
            return null;
        }

        while (!ct.IsCancellationRequested)
        {
            var newest = new DirectoryInfo(directory)
                .GetFiles()
                .Where(file => file.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) && file.CreationTime <= DateTime.Now)
                .OrderByDescending(file => file.CreationTime)
                .FirstOrDefault();

            if (newest is not null && newest.CreationTime.AddSeconds(15) > DateTime.Now)
            {
                return newest.FullName;
            }

            Log.Write("ActivityWatcher::FindLatestLog", $"Waiting for a recent log file (newest is {newest?.Name ?? "none"})");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        return null;
    }

    private void ReadLogEntry(string entry)
    {
        const string ident = "ActivityWatcher::ReadLogEntry";

        _entriesRead++;

        if (_entriesRead % 500 == 0)
        {
            Log.Write(ident, $"Read {_entriesRead} log entries");
        }

        int separator = entry.IndexOf(' ');

        if (separator == -1)
        {
            return;
        }

        string message = entry[(separator + 1)..];

        if (message.StartsWith(GameLeavingEntry, StringComparison.Ordinal))
        {
            Log.Write(ident, "Back in the desktop app");

            // This is the first and most reliable sign of returning to the menu. The
            // disconnect line that used to be the only trigger arrives about twenty
            // milliseconds later and sometimes not at all, which left everything keyed on
            // leaving a game - the taskbar icon, Discord - stuck showing it after the
            // person was back on the home screen.
            if (InGame)
            {
                InGame = false;
                Data = new ActivityData();

                OnGameLeave?.Invoke(this, EventArgs.Empty);

                return;
            }

            if (Data.PlaceId != 0)
            {
                Data = new ActivityData();
            }

            return;
        }

        if (!InGame && Data.PlaceId == 0)
        {
            ReadJoiningEntry(message, ident);
        }
        else if (!InGame && Data.PlaceId != 0)
        {
            ReadPendingJoinEntry(message, ident);
        }
        else if (InGame && Data.PlaceId != 0)
        {
            ReadInGameEntry(message, ident);
        }
    }

    private void ReadJoiningEntry(string message, string ident)
    {
        if (!message.StartsWith(GameJoiningEntry, StringComparison.Ordinal))
        {
            return;
        }

        var match = JoiningPattern().Match(message);

        if (match.Groups.Count != 4)
        {
            Log.Write(ident, $"Unexpected game join entry format: {message}");
            return;
        }

        InGame = false;
        Data.JobId = match.Groups[1].Value;
        Data.PlaceId = long.Parse(match.Groups[2].Value);
        Data.MachineAddress = match.Groups[3].Value;

        if (_reservedTeleportMarker)
        {
            Data.ServerType = ServerType.Reserved;
            _reservedTeleportMarker = false;
        }

        Log.Write(ident, $"Joining game ({Data})");
    }

    private void ReadPendingJoinEntry(string message, string ident)
    {
        if (message.StartsWith(GameJoiningUniverseEntry, StringComparison.Ordinal))
        {
            var match = UniversePattern().Match(message);

            if (match.Groups.Count != 2)
            {
                Log.Write(ident, $"Unexpected universe entry format: {message}");
                return;
            }

            Data.UniverseId = long.Parse(match.Groups[1].Value);

            var referral = ReferralPattern().Match(message);

            if (referral.Groups.Count == 2)
            {
                string value = referral.Groups[1].Value;

                if (value.Contains("RequestPrivateGame", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("GameDetailPageJSHybridEvent", StringComparison.OrdinalIgnoreCase))
                {
                    Data.ServerType = ServerType.Private;
                }
            }
        }
        else if (message.StartsWith(GameJoiningUdmuxEntry, StringComparison.Ordinal))
        {
            var match = UdmuxPattern().Match(message);

            if (match.Groups.Count != 3 || match.Groups[2].Value != Data.MachineAddress)
            {
                Log.Write(ident, $"Unexpected UDMUX entry format: {message}");
                return;
            }

            Data.MachineAddress = match.Groups[1].Value;
            Log.Write(ident, $"Server is UDMUX protected ({Data})");
        }
        else if (message.StartsWith(GameJoinedEntry, StringComparison.Ordinal))
        {
            var serverId = ServerIdPattern().Match(message);

            if (serverId.Success)
            {
                Data.MachineAddress = serverId.Groups[1].Value;
                Data.Port = int.Parse(serverId.Groups[2].Value);
            }

            InGame = true;
            Data.TimeJoined = DateTime.Now;

            Log.Write(ident, $"Joined game ({Data}, {Data.ServerTypeLabel})");

            OnGameJoin?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadInGameEntry(string message, string ident)
    {
        if (message.StartsWith(GameDisconnectedEntry, StringComparison.Ordinal))
        {
            Log.Write(ident, $"Disconnected from game ({Data})");

            InGame = false;
            Data = new ActivityData();

            OnGameLeave?.Invoke(this, EventArgs.Empty);
        }
        else if (message.StartsWith(GameJoiningEntry, StringComparison.Ordinal))
        {
            Log.Write(ident, $"Joining a new server with no disconnect first ({Data}), treating it as a hop");

            InGame = false;
            Data = new ActivityData();

            OnGameLeave?.Invoke(this, EventArgs.Empty);

            ReadJoiningEntry(message, ident);
        }
        else if (message.StartsWith(GameTeleportingEntry, StringComparison.Ordinal))
        {
            var match = JoinTypePattern().Match(message);

            if (match.Success && int.TryParse(match.Groups[1].Value, out int joinTypeId))
            {
                var joinType = (ServerSessionJoinType)joinTypeId;

                if (joinType is ServerSessionJoinType.NewGamePrivateGame or ServerSessionJoinType.SpecificPrivateGame)
                {
                    _reservedTeleportMarker = true;
                    Log.Write(ident, "Detected a reserved server teleport");
                }
            }
        }
        else if (message.StartsWith(GameMessageEntry, StringComparison.Ordinal))
        {
            var match = MessagePattern().Match(message);

            if (match.Groups.Count != 2)
            {
                return;
            }

            ReadLaunchData(match.Groups[1].Value, ident);
        }
    }

    private void ReadLaunchData(string payload, string ident)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("command", out var command)
                || command.GetString() != "SetLaunchData"
                || !document.RootElement.TryGetProperty("data", out var data))
            {
                return;
            }

            string? value = data.GetString();

            if (value is null || value.Length > 200)
            {
                return;
            }

            Data.LaunchData = value;
            Log.Write(ident, "Received launch data");
        }
        catch (JsonException)
        {
            Log.Write(ident, "Could not parse an RPC message");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    [GeneratedRegex(@"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at ([0-9\.]+)")]
    private static partial Regex JoiningPattern();

    [GeneratedRegex(@"universeid:([0-9]+)")]
    private static partial Regex UniversePattern();

    [GeneratedRegex(@"serverId: ([0-9\.]+)\|([0-9]+)")]
    private static partial Regex ServerIdPattern();

    [GeneratedRegex(@"referral_page:([^,]+)")]
    private static partial Regex ReferralPattern();

    [GeneratedRegex(@"UDMUX Address = ([0-9\.]+), Port = [0-9]+ \| RCC Server Address = ([0-9\.]+), Port = [0-9]+")]
    private static partial Regex UdmuxPattern();

    [GeneratedRegex(@"JoinTypeId""%3a(\d+)%2c")]
    private static partial Regex JoinTypePattern();

    [GeneratedRegex(@"\[JelloRPC\] (.*)")]
    private static partial Regex MessagePattern();
}
