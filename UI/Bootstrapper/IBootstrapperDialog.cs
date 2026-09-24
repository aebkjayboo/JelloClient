using JelloClient.Services;

namespace JelloClient.UI.Bootstrapper;

internal enum BootstrapperStyle
{
    None,
    JelloDialog,
    RobloxDialog,
    ProgressDialog,
    LegacyDialog
}

internal interface IBootstrapperDialog
{
    string Message { get; set; }

    bool ProgressIndeterminate { get; set; }

    int ProgressValue { get; set; }

    int ProgressMaximum { get; set; }

    bool CancelEnabled { get; set; }

    event EventHandler? Cancelled;

    void ShowBootstrapper();

    void CloseBootstrapper();

    void ShowSuccess(string message);
}

internal static class BootstrapperStyles
{
    public static readonly IReadOnlyList<BootstrapperStyle> Selectable = new[]
    {
        BootstrapperStyle.JelloDialog,
        BootstrapperStyle.RobloxDialog,
        BootstrapperStyle.ProgressDialog,
        BootstrapperStyle.LegacyDialog,
        BootstrapperStyle.None
    };

    public static string Describe(BootstrapperStyle style) => style switch
    {
        BootstrapperStyle.JelloDialog => "Jello",
        BootstrapperStyle.RobloxDialog => "Roblox",
        BootstrapperStyle.ProgressDialog => "Progress",
        BootstrapperStyle.LegacyDialog => "Legacy",
        _ => "None"
    };

    public static string Explain(BootstrapperStyle style) => style switch
    {
        BootstrapperStyle.JelloDialog => "Jello's own dialog, matching the launcher and your theme.",
        BootstrapperStyle.RobloxDialog => "A dark rounded loader with the Roblox wordmark, a progress bar and a cancel button.",
        BootstrapperStyle.ProgressDialog => "A compact progress window in the style of the classic bootstrapper.",
        BootstrapperStyle.LegacyDialog => "A small fixed-width dialog with a marquee bar, closest to the 2011 installer.",
        _ => "No dialog at all. Progress is shown on the launcher instead."
    };

    public static async Task<IBootstrapperDialog?> CreateAsync()
    {
        const string ident = "BootstrapperStyles::CreateAsync";

        var settings = AppState.Settings;

        if (string.IsNullOrEmpty(settings.BootstrapperTheme))
        {
            return Create(settings.BootstrapperStyle);
        }

        try
        {
            var theme = Roblox.BootstrapperTheme.Load(settings.BootstrapperTheme);

            if (!CustomDialog.IsRuntimeAvailable(out _))
            {
                Log.Write(ident, "WebView2 runtime is missing, falling back to the Jello dialog");
                return new JelloDialog();
            }

            Log.Write(ident, $"Preparing the custom theme dialog for '{theme.Name}'");

            var dialog = await CustomDialog.TryCreateAsync(theme).ConfigureAwait(true);

            if (dialog is not null)
            {
                return dialog;
            }

            Log.Write(ident, "The theme dialog could not be prepared, falling back to the Jello dialog");
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
        }

        return new JelloDialog();
    }

    public static IBootstrapperDialog? Create(BootstrapperStyle style)
    {
        Log.Write("BootstrapperStyles::Create", $"Creating the {Describe(style)} dialog");

        return style switch
        {
            BootstrapperStyle.JelloDialog => new JelloDialog(),
            BootstrapperStyle.RobloxDialog => new RobloxDialog(),
            BootstrapperStyle.ProgressDialog => new ProgressDialog(),
            BootstrapperStyle.LegacyDialog => new LegacyDialog(),
            _ => null
        };
    }
}
