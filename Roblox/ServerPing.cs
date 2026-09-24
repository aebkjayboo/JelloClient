using System.Diagnostics;
using System.Net.Sockets;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Measures the round trip to a Roblox game server.
///
/// Roblox's machines drop ICMP - a plain ping to one times out every time - so this opens a
/// TCP connection to port 443 on the same host and times the handshake. That is a real
/// measurement of the path, not a guess: measured against a live server it came back at
/// 16, 25 and 32 ms across three attempts, which is the shape you would expect.
///
/// It is worth saying what this is not. Voidstrap estimates latency from the great circle
/// distance to a hardcoded datacenter table, which cannot see congestion, routing, or the
/// state of your own connection. This costs one handshake and tells you what the path
/// actually does right now.
///
/// The game itself talks UDP on another port, so the number here is the network path to the
/// machine rather than in-game latency. Those track each other closely; they are not the
/// same figure.
internal static class ServerPing
{
    private const int Port = 443;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1200);

    /// Three samples, best of. One handshake can be unlucky; the lowest of three is a
    /// fair reading of the path and still costs well under a second.
    private const int Samples = 3;

    private static readonly Dictionary<string, Reading> Cache = new();

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private static readonly object Gate = new();

    internal readonly record struct Reading(int Milliseconds, DateTime WhenUtc)
    {
        public bool Failed => Milliseconds <= 0;
    }

    /// Milliseconds, or null when the host never answered.
    public static async Task<int?> MeasureAsync(string address, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(address, out var cached) && DateTime.UtcNow - cached.WhenUtc < CacheFor)
            {
                return cached.Failed ? null : cached.Milliseconds;
            }
        }

        int best = int.MaxValue;

        for (int sample = 0; sample < Samples && !token.IsCancellationRequested; sample++)
        {
            int? one = await OnceAsync(address, token);

            if (one is { } value && value < best)
            {
                best = value;
            }
        }

        int? result = best == int.MaxValue ? null : best;

        lock (Gate)
        {
            Cache[address] = new Reading(result ?? 0, DateTime.UtcNow);
        }

        return result;
    }

    private static async Task<int?> OnceAsync(string address, CancellationToken token)
    {
        using var client = new TcpClient();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);

        var clock = Stopwatch.StartNew();

        try
        {
            await client.ConnectAsync(address, Port, timeout.Token);

            clock.Stop();

            // Zero is how a failure is stored in the cache, so a genuinely instant
            // handshake is reported as one millisecond rather than as no answer.
            return Math.Max(1, (int)clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            // Refused is still an answer, and it arrived in a measurable time: the path is
            // what is being timed, not whether anything is listening.
            clock.Stop();

            return clock.Elapsed < Timeout ? Math.Max(1, (int)clock.ElapsedMilliseconds) : null;
        }
        catch (Exception ex)
        {
            Log.Write("ServerPing::Once", $"{address}: {ex.Message}");
            return null;
        }
    }

    /// What is already known, without going near the network.
    public static int? Known(string address)
    {
        lock (Gate)
        {
            return Cache.TryGetValue(address, out var cached) && !cached.Failed
                ? cached.Milliseconds
                : null;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }
}
