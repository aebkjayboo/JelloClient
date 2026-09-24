using System.Windows.Media;
using JelloClient.Services;

namespace JelloClient.Roblox.Memory;

/// Keeps the GUI tint applied while it is on.
///
/// One apply is not enough: the app spawns new frames as you move around it, and those
/// come up in their original colour. So this re-applies on a slow timer, which also picks
/// up the client being started or restarted. When it is turned off it puts the colours
/// back and lets go.
///
/// It runs only while both the Lab and the tint are on, and it is the only thing in Jello
/// that writes to the client's memory. Everything else in the Lab reads.
internal static class GuiTintRunner
{
    private const int IntervalMilliseconds = 1500;

    private static readonly object Gate = new();

    private static Timer? _timer;
    private static GuiTint? _tint;
    private static int _busy;

    public static string Status { get; private set; } = "Off.";

    public static void Refresh()
    {
        bool wanted = AppState.Settings.LabEnabled && AppState.Settings.GuiTintEnabled;

        if (wanted)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private static void Start()
    {
        lock (Gate)
        {
            if (_timer is not null)
            {
                return;
            }

            Status = "Waiting for the Roblox app.";
            _timer = new Timer(_ => Tick(), null, 0, IntervalMilliseconds);

            Log.Write("GuiTintRunner::Start", "GUI tint running");
        }
    }

    public static void Stop()
    {
        Timer? timer;

        lock (Gate)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();

        lock (Gate)
        {
            if (_tint is not null)
            {
                try
                {
                    _tint.Restore();
                }
                catch (Exception ex)
                {
                    Log.Write("GuiTintRunner::Stop", ex.Message);
                }

                _tint.Dispose();
                _tint = null;
            }
        }

        Status = "Off.";
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            RunOnce().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.WriteException("GuiTintRunner::Tick", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task RunOnce()
    {
        // The client may have closed and reopened; re-attach if what we hold is gone.
        if (_tint is null)
        {
            _tint = await GuiTint.AttachAsync();

            if (_tint is null)
            {
                Status = "Waiting for the Roblox app.";
                return;
            }
        }

        var (r, g, b) = ParseColour(AppState.Settings.GuiTintColour);

        var result = _tint.Apply(r, g, b, AppState.Settings.GuiTintStrength);

        if (!result.Ok)
        {
            // Attach is stale (client gone), so drop it and try fresh next tick.
            _tint.Dispose();
            _tint = null;

            Status = "Waiting for the Roblox app.";
            return;
        }

        Status = result.Summary;
    }

    private static (byte R, byte G, byte B) ParseColour(string? hex)
    {
        try
        {
            var colour = (Color)ColorConverter.ConvertFromString(hex ?? "#3B2A6B");
            return (colour.R, colour.G, colour.B);
        }
        catch (Exception)
        {
            return (0x3B, 0x2A, 0x6B);
        }
    }
}
