using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JelloClient.Services;

namespace JelloClient.UI.Bootstrapper;

public abstract class BootstrapperWindow : Window, IBootstrapperDialog
{
    private string _message = "Please wait...";
    private bool _indeterminate = true;
    private int _value;
    private int _maximum = 100;
    private bool _cancelEnabled = true;
    private bool _closing;

    protected BootstrapperWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Title = "Jello Client";
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;

        ApplyCustomIcon();

        MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    public event EventHandler? Cancelled;

    protected virtual void OnStateChanged()
    {
    }

    private void ApplyCustomIcon()
    {
        string? path = AppState.Settings.BootstrapperIconPath;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(path));
            Log.Write($"{GetType().Name}::ApplyCustomIcon", $"Using {path}");
        }
        catch (Exception ex)
        {
            Log.WriteException($"{GetType().Name}::ApplyCustomIcon", ex);
        }
    }

    protected abstract TextBlock MessageTarget { get; }

    protected abstract ProgressBar ProgressTarget { get; }

    protected abstract UIElement? CancelTarget { get; }

    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            OnStateChanged();
            Dispatcher.Invoke(() => MessageTarget.Text = value);
        }
    }

    public bool ProgressIndeterminate
    {
        get => _indeterminate;
        set
        {
            _indeterminate = value;
            OnStateChanged();
            Dispatcher.Invoke(() => ProgressTarget.IsIndeterminate = value);
        }
    }

    public int ProgressValue
    {
        get => _value;
        set
        {
            _value = value;
            OnStateChanged();
            Dispatcher.Invoke(() => ProgressTarget.Value = value);
        }
    }

    public int ProgressMaximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            OnStateChanged();
            Dispatcher.Invoke(() => ProgressTarget.Maximum = value);
        }
    }

    public bool CancelEnabled
    {
        get => _cancelEnabled;
        set
        {
            _cancelEnabled = value;
            OnStateChanged();

            Dispatcher.Invoke(() =>
            {
                if (CancelTarget is not null)
                {
                    CancelTarget.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                }
            });
        }
    }

    public virtual void ShowBootstrapper() => Dispatcher.Invoke(Show);

    public virtual void CloseBootstrapper()
    {
        _closing = true;
        Dispatcher.Invoke(Close);
    }

    public void ShowSuccess(string message)
    {
        Dispatcher.Invoke(() =>
        {
            Message = message;
            ProgressIndeterminate = false;
            ProgressValue = ProgressMaximum;
            CancelEnabled = false;
        });
    }

    protected void RaiseCancelled()
    {
        Log.Write($"{GetType().Name}::Cancel", "Cancelled from the bootstrapper dialog");

        Cancelled?.Invoke(this, EventArgs.Empty);
        CloseBootstrapper();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_closing)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}
