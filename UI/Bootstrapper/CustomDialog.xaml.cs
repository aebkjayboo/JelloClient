using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.UI.Bootstrapper;

public partial class CustomDialog : BootstrapperWindow
{
    private static readonly TimeSpan InitialiseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RenderGrace = TimeSpan.FromSeconds(3);

    private readonly BootstrapperTheme _theme;
    private readonly TextBlock _messageSink = new();
    private readonly ProgressBar _progressSink = new() { Maximum = 100 };
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _ready;
    private bool _revealed;

    private CustomDialog(BootstrapperTheme theme)
    {
        _theme = theme;

        InitializeComponent();

        Width = theme.Width;
        Height = theme.Height;
        Shell.CornerRadius = new CornerRadius(theme.RoundedCorners ? 10 : 0);

        // transparent before the control initialises, so its default surface is never what shows
        Panel.DefaultBackgroundColor = System.Drawing.Color.Transparent;

        // prewarm completely off screen, so a half-painted frame can never be seen
        Opacity = 0;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
    }

    protected override TextBlock MessageTarget => _messageSink;

    protected override ProgressBar ProgressTarget => _progressSink;

    protected override UIElement? CancelTarget => null;

    public static bool IsRuntimeAvailable(out string? version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch (Exception ex)
        {
            Log.Write("CustomDialog::IsRuntimeAvailable", $"WebView2 runtime not usable: {ex.Message}");
            version = null;
            return false;
        }
    }

    internal static async Task<CustomDialog?> TryCreateAsync(BootstrapperTheme theme)
    {
        const string ident = "CustomDialog::TryCreate";

        CustomDialog? dialog = null;

        try
        {
            dialog = new CustomDialog(theme);

            // the control needs a window handle before it will initialise, so show it fully transparent
            dialog.Show();

            using var timeout = new CancellationTokenSource(InitialiseTimeout);

            await dialog.InitialiseAsync(timeout.Token).ConfigureAwait(true);

            Log.Write(ident, $"Theme '{theme.Name}' is initialised and painted, ready to reveal");

            return dialog;
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);

            try
            {
                dialog?.Shutdown();
            }
            catch (Exception inner)
            {
                Log.Write(ident, inner.Message);
            }

            return null;
        }
    }

    private async Task InitialiseAsync(CancellationToken ct)
    {
        string cache = Path.Combine(Paths.Base, "WebView2");
        Directory.CreateDirectory(cache);

        var environment = await CoreWebView2Environment.CreateAsync(null, cache).ConfigureAwait(true);

        await Panel.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        var core = Panel.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 was still null after initialisation.");

        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            "theme.jello",
            _theme.Directory,
            CoreWebView2HostResourceAccessKind.Allow);

        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript).ConfigureAwait(true);

        core.WebMessageReceived += OnWebMessage;

        core.DOMContentLoaded += async (_, _) =>
        {
            try
            {
                // two frames after the DOM is up means the first paint has actually happened
                await core.ExecuteScriptAsync(
                    "requestAnimationFrame(function(){requestAnimationFrame(function(){" +
                    "window.chrome.webview.postMessage('rendered');});});");
            }
            catch (Exception ex)
            {
                Log.Write("CustomDialog::DOMContentLoaded", ex.Message);
                _rendered.TrySetResult();
            }
        };

        Panel.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                _rendered.TrySetException(new InvalidOperationException($"Navigation failed: {e.WebErrorStatus}."));
                return;
            }

            // if the page never signals a frame, do not hang on it forever
            _ = Task.Delay(RenderGrace, ct).ContinueWith(
                _ => _rendered.TrySetResult(),
                TaskContinuationOptions.OnlyOnRanToCompletion);
        };

        core.Navigate(new Uri(_theme.PanelPath).AbsoluteUri);

        using (ct.Register(() => _rendered.TrySetException(new TimeoutException("The theme did not render in time."))))
        {
            await _rendered.Task.ConfigureAwait(true);
        }

        _ready = true;

        PushState();
    }

    private const string BridgeScript = """
        (function () {
            var api = {
                _cb: null,
                _last: null,
                onUpdate: function (cb) {
                    this._cb = cb;
                    if (this._last) { try { cb(this._last); } catch (e) {} }
                },
                cancel: function () {
                    window.chrome.webview.postMessage('cancel');
                },
                close: function () {
                    window.chrome.webview.postMessage('cancel');
                }
            };

            window.__jelloPush = function (state) {
                api._last = state;
                if (api._cb) { try { api._cb(state); } catch (e) {} }
            };

            window.voidstrap = api;
            window.bloxstrap = api;
            window.jello = api;
        })();
        """;

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;

        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (Exception)
        {
            return;
        }

        if (message == "rendered")
        {
            Log.Write("CustomDialog::WebMessage", "Theme reported its first painted frame");
            _rendered.TrySetResult();
            return;
        }

        Log.Write("CustomDialog::WebMessage", message);

        if (message == "cancel")
        {
            Dispatcher.Invoke(RaiseCancelled);
        }
    }

    public override void ShowBootstrapper()
    {
        Dispatcher.Invoke(() =>
        {
            if (_revealed)
            {
                return;
            }

            _revealed = true;

            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;

            Show();
            ShowInTaskbar = true;
            Activate();

            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

            Log.Write("CustomDialog::Show", "Faded the theme in");
        });
    }

    private void Shutdown()
    {
        Dispatcher.Invoke(() =>
        {
            BeginAnimation(OpacityProperty, null);
            base.CloseBootstrapper();
        });
    }

    public override void CloseBootstrapper()
    {
        Dispatcher.Invoke(() => BeginAnimation(OpacityProperty, null));

        base.CloseBootstrapper();
    }

    private void PushState()
    {
        if (!_ready)
        {
            return;
        }

        var state = new
        {
            status = Message,
            percent = ProgressMaximum <= 0 ? 0 : ProgressValue * 100.0 / ProgressMaximum,
            indeterminate = ProgressIndeterminate,
            cancelEnabled = CancelEnabled
        };

        string json = JsonSerializer.Serialize(state);

        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Panel.ExecuteScriptAsync($"window.__jelloPush({json})");
            }
            catch (Exception ex)
            {
                Log.Write("CustomDialog::PushState", ex.Message);
            }
        });
    }

    protected override void OnStateChanged() => PushState();
}
