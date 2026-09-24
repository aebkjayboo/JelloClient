using System.IO.Pipes;
using System.Text;

namespace JelloClient.Services;

/// Keeps one Jello running at a time, whatever started it.
///
/// The first copy opens a pipe and owns everything from then on. Any later copy - a
/// desktop shortcut, the start menu, a roblox:// link from the website - writes its command
/// line into that pipe and exits immediately, and the running copy acts on it: surfacing a
/// window, or launching Roblox.
///
/// Launches used to be exempt from this, which is how seven Jellos ended up running at
/// once: every game launched through the website left another one behind. Multi instance is
/// unaffected, because one Jello can start as many clients as it likes.
internal static class SingleInstance
{
    private const string MutexName = @"Global\JelloClient.Singleton";

    private const string PipeName = "JelloClient.Instance";

    private static Mutex? _mutex;

    private static CancellationTokenSource? _listening;

    public static bool Claim()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool first);

            if (first)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;

            return false;
        }
        catch (Exception ex)
        {
            Log.WriteException("SingleInstance::Claim", ex);

            // If the check itself fails, running is better than refusing to start.
            return true;
        }
    }

    /// Hands this copy's arguments to the one already running. False means nobody answered,
    /// in which case the caller should carry on and run normally.
    public static bool HandOff(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            pipe.Connect(2000);

            byte[] payload = Encoding.UTF8.GetBytes(string.Join('\n', args));

            pipe.Write(payload);
            pipe.Flush();

            Log.Write("SingleInstance::HandOff", $"Passed {args.Length} argument(s) to the running copy");

            return true;
        }
        catch (Exception ex)
        {
            Log.Write("SingleInstance::HandOff", $"Nobody answered the pipe: {ex.Message}");
            return false;
        }
    }

    /// Runs in the first copy: every later copy shows up here as its argument list.
    public static void Listen(Action<string[]> handle)
    {
        _listening = new CancellationTokenSource();

        var token = _listening.Token;

        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    pipe.WaitForConnection();

                    using var reader = new StreamReader(pipe, Encoding.UTF8);

                    string[] args = reader.ReadToEnd()
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    Log.Write("SingleInstance::Listen", $"Another copy started with: {string.Join(' ', args)}");

                    handle(args);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    Log.Write("SingleInstance::Listen", ex.Message);
                    Thread.Sleep(500);
                }
            }
        })
        {
            IsBackground = true,
            Name = "JelloInstancePipe"
        };

        thread.Start();
    }

    public static void Release()
    {
        _listening?.Cancel();

        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (Exception)
        {
            // The process is going away regardless.
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
