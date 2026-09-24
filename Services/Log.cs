using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using JelloClient.Roblox;

namespace JelloClient.Services;

internal static class Log
{
    private const int RetainedSessions = 20;

    private static readonly object Gate = new();

    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void Start(IReadOnlyList<string> args)
    {
        lock (Gate)
        {
            if (_writer is not null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Paths.Logs);

                string path = Path.Combine(Paths.Logs, $"Session_{DateTime.Now:yyyyMMdd'T'HHmmss'.'fff}.log");

                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                FilePath = path;
            }
            catch (Exception ex)
            {
                _writer = null;
                FilePath = null;
                Debug.WriteLine($"Log could not be opened: {ex}");
                return;
            }
        }

        var name = Assembly.GetExecutingAssembly().GetName();

        Write("Log::Start", $"{name.Name} {name.Version?.ToString(3) ?? "unknown"}");
        Write("Log::Start", $"{Environment.OSVersion.VersionString}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
        Write("Log::Start", $"Executable: {Environment.ProcessPath}");
        Write("Log::Start", args.Count == 0
            ? "Launched with no arguments"
            : $"Launched with: {string.Join(" ", args)}");

        Prune();
    }

    public static void Write(string ident, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{ident}] {message}";

        Debug.WriteLine(line);

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
            }
        }
    }

    public static void WriteException(string ident, Exception exception)
    {
        Write(ident, $"An exception occurred: {exception.GetType().FullName}: {exception.Message}");

        string line = exception.ToString();

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
            }
        }

        Debug.WriteLine(line);
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (_writer is null)
            {
                return;
            }

            try
            {
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [Log::Stop] Session ended");
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
            }

            _writer = null;
        }
    }

    private static void Prune()
    {
        try
        {
            var stale = new DirectoryInfo(Paths.Logs)
                .GetFiles("Session_*.log")
                .Where(file => !string.Equals(file.FullName, FilePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.CreationTimeUtc)
                .Skip(RetainedSessions - 1)
                .ToList();

            foreach (var file in stale)
            {
                file.Delete();
            }

            if (stale.Count > 0)
            {
                Write("Log::Prune", $"Deleted {stale.Count} old session log(s)");
            }
        }
        catch (Exception ex)
        {
            Write("Log::Prune", $"Could not prune old logs: {ex.Message}");
        }
    }
}
