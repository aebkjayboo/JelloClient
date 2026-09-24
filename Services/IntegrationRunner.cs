using System.Diagnostics;

namespace JelloClient.Services;

internal static class IntegrationRunner
{
    private static readonly List<int> AutoClosePids = new();

    public static void LaunchAll()
    {
        const string ident = "IntegrationRunner::LaunchAll";

        foreach (var integration in AppState.Settings.CustomIntegrations)
        {
            if (string.IsNullOrWhiteSpace(integration.Location))
            {
                Log.Write(ident, $"Skipping '{integration.Name}', no application is set");
                continue;
            }

            Log.Write(ident, $"Launching '{integration.Name}' ({integration.Location} {integration.LaunchArgs}), autoclose is {integration.AutoClose}");

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = integration.Location,
                    Arguments = integration.LaunchArgs.Replace("\r\n", " "),
                    WorkingDirectory = Path.GetDirectoryName(integration.Location),
                    UseShellExecute = true
                });

                if (process is null)
                {
                    continue;
                }

                Log.Write(ident, $"'{integration.Name}' is running as process {process.Id}");

                if (integration.AutoClose)
                {
                    AutoClosePids.Add(process.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Write(ident, $"Could not launch '{integration.Name}': {ex.Message}");
            }
        }
    }

    public static void CloseAll()
    {
        const string ident = "IntegrationRunner::CloseAll";

        foreach (int pid in AutoClosePids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                if (process.HasExited)
                {
                    continue;
                }

                process.CloseMainWindow();

                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                }

                Log.Write(ident, $"Closed integration process {pid}");
            }
            catch (ArgumentException)
            {
            }
            catch (Exception ex)
            {
                Log.Write(ident, $"Could not close process {pid}: {ex.Message}");
            }
        }

        AutoClosePids.Clear();
    }
}
