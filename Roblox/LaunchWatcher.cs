using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed class LaunchWatcher : IDisposable
{
    public static readonly TimeSpan LogTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan WindowSettleDelay = TimeSpan.FromSeconds(1);

    private readonly FileSystemWatcher? _watcher;
    private readonly TaskCompletionSource<string?> _appeared =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _disposed;

    private LaunchWatcher(FileSystemWatcher? watcher)
    {
        _watcher = watcher;
    }

    public static LaunchWatcher Arm()
    {
        const string ident = "LaunchWatcher::Arm";

        string directory = Path.Combine(Paths.LocalAppData, "Roblox", "logs");

        try
        {
            Directory.CreateDirectory(directory);

            var watcher = new FileSystemWatcher
            {
                Path = directory,
                Filter = "*.log",
                EnableRaisingEvents = true
            };

            var instance = new LaunchWatcher(watcher);

            watcher.Created += (_, e) =>
            {
                watcher.EnableRaisingEvents = false;
                instance._appeared.TrySetResult(e.FullPath);
            };

            Log.Write(ident, $"Watching {directory} for a new Roblox log");

            return instance;
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            return new LaunchWatcher(null);
        }
    }

    public async Task<string?> WaitAsync(CancellationToken ct)
    {
        const string ident = "LaunchWatcher::Wait";

        if (_watcher is null)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LogTimeout);

        using (timeout.Token.Register(() => _appeared.TrySetResult(null)))
        {
            string? path = await _appeared.Task.ConfigureAwait(false);

            if (path is null)
            {
                if (ct.IsCancellationRequested)
                {
                    Log.Write(ident, "Cancelled while waiting for Roblox to appear");
                }
                else
                {
                    Log.Write(ident, $"Roblox did not create a log within {LogTimeout.TotalSeconds:N0} seconds");
                }

                return null;
            }

            Log.Write(ident, $"Roblox opened its log at {path}");

            return path;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _appeared.TrySetResult(null);
        _watcher?.Dispose();
    }
}
