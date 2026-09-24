using System.Windows;
using System.Windows.Controls;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.UI;

public partial class InstallerWindow : JelloWindow
{
    private static readonly AppTheme[] ThemeOrder = { AppTheme.Dark, AppTheme.Light, AppTheme.System };
    private static readonly string[] ThemeLabels = { "Dark", "Light", "Match Windows" };

    private IReadOnlyList<Locales.Option> _locales = Array.Empty<Locales.Option>();

    private int _step;

    private bool _loading = true;

    private bool _installed;

    public InstallerWindow()
    {
        InitializeComponent();
    }

    protected override FrameworkElement EffectsRoot => Root;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Guard(nameof(Load), Load);
    }

    private void Load()
    {
        foreach (string label in ThemeLabels)
        {
            ThemeCombo.Items.Add(new ComboBoxItem { Content = label });
        }

        _locales = Locales.All();

        foreach (var option in _locales)
        {
            LocaleCombo.Items.Add(new ComboBoxItem { Content = option.Label });
        }

        LocationBox.Text = FirstRun.RecordedLocation() ?? Paths.Default;

        ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeOrder, AppState.Settings.Theme));
        LocaleCombo.SelectedIndex = 0;

        string existing = FirstRun.DescribeLocation(LocationBox.Text);

        ExistingDataCard.Visibility = existing.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ExistingDataText.Text = existing;

        _loading = false;

        RefreshStep();
        RefreshNotes();

        // The consent screen is the first thing shown, and the Terms live in the Discord, so
        // open it straight away to make joining and reading them one less step.
        Sun.OpenDiscord();
    }

    private void RefreshStep()
    {
        WelcomePage.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetupPage.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        FinishPage.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

        StepOne.Tag = _step == 0 ? "Current" : _step > 0 ? "Done" : null;
        StepTwo.Tag = _step == 1 ? "Current" : _step > 1 ? "Done" : null;
        StepThree.Tag = _step == 2 ? "Current" : null;

        BackButton.IsEnabled = _step == 1;
        NextButton.Content = _step switch
        {
            0 => "Next",
            1 => "Install",
            _ => "Finish"
        };

        // The consent screen gates everything after it: no acceptance, no install.
        NextButton.IsEnabled = _step != 0 || AcceptToggle.IsChecked == true;

        FooterText.Text = _step switch
        {
            0 => $"Jello Client {Updater.CurrentVersion.ToString(3)}",
            1 => "Nothing is written until you press Install.",
            _ => $"Installed to {Paths.Base}"
        };
    }

    private void RefreshNotes()
    {
        if (_loading)
        {
            return;
        }

        string note = FirstRun.DescribeLocation(LocationBox.Text);

        LocationNote.Text = note.Length == 0
            ? $"Jello will use {Path.Combine(LocationBox.Text, "Versions")} for the Roblox builds it downloads."
            : note;

        var theme = ThemeOrder[Math.Max(0, ThemeCombo.SelectedIndex)];

        ThemeNote.Text = theme == AppTheme.System
            ? $"Follows Windows, which is currently {(Themes.WindowsPrefersLight() ? "light" : "dark")}."
            : $"Always {ThemeLabels[Math.Max(0, ThemeCombo.SelectedIndex)].ToLowerInvariant()}.";

        LocaleNote.Text = Locales.Describe(_locales[Math.Max(0, LocaleCombo.SelectedIndex)].Name);
    }

    private void Location_Changed(object sender, TextChangedEventArgs e)
    {
        LocationError.Visibility = Visibility.Collapsed;
        RefreshNotes();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where Jello Client should be installed",
            UseDescriptionForTitle = true,
            SelectedPath = LocationBox.Text
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        string chosen = dialog.SelectedPath;

        // Installing straight into a folder full of other things is almost never what
        // someone means, so a subfolder is suggested the way Bloxstrap does it.
        if (!chosen.EndsWith("JelloClient", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(chosen)
            && Directory.EnumerateFileSystemEntries(chosen).Any())
        {
            string suggestion = Path.Combine(chosen, "JelloClient");

            var answer = MessageBox.Show(
                $"{chosen} is not empty.{Environment.NewLine}{Environment.NewLine}Install into {suggestion} instead?",
                "Jello Client",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                chosen = suggestion;
            }
        }

        LocationBox.Text = chosen;
    }

    private void ResetLocation_Click(object sender, RoutedEventArgs e) => LocationBox.Text = Paths.Default;

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // Applied straight away so the choice is visible before it is committed.
        AppState.Settings.Theme = ThemeOrder[ThemeCombo.SelectedIndex];
        Themes.Apply(AppState.Settings.Theme);

        Guard(nameof(RefreshEffects), RefreshEffects);
        RefreshNotes();
    }

    private void RefreshEffects() => ApplyAllEffects();

    private void Locale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Locales.Apply(_locales[LocaleCombo.SelectedIndex].Name);
        RefreshNotes();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            return;
        }

        _step--;
        RefreshStep();
    }

    private void Accept_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        RefreshStep();
    }

    private void Discord_Click(object sender, RoutedEventArgs e) => Sun.OpenDiscord();

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 0:
                AppState.Settings.AcceptedTerms = true;
                AppState.Settings.AcceptedTermsUtc = DateTime.UtcNow;
                AppState.Persist();

                _step++;
                RefreshStep();
                RefreshNotes();
                break;

            case 1:
                if (RunInstall())
                {
                    _step = 2;
                    RefreshStep();
                }

                break;

            default:
                Finish(launcher: true);
                break;
        }
    }





    private bool RunInstall()
    {
        string location = LocationBox.Text.Trim();

        if (FirstRun.Validate(location) is { } error)
        {
            LocationError.Text = error;
            LocationError.Visibility = Visibility.Visible;

            Log.Write("InstallerWindow::RunInstall", $"Rejected {location}: {error}");

            return false;
        }

        try
        {
            FirstRun.Install(new FirstRun.Options(
                location,
                DesktopToggle.IsChecked == true,
                StartMenuToggle.IsChecked == true,
                ProtocolToggle.IsChecked == true,
                StartupToggle.IsChecked == true,
                ThemeOrder[ThemeCombo.SelectedIndex],
                _locales[LocaleCombo.SelectedIndex].Name));

            _installed = true;

            FinishSummary.Text =
                $"Jello is installed in {location}. It is in your list of installed programs, so it uninstalls like anything else." +
                (ProtocolToggle.IsChecked == true
                    ? " Roblox links now launch through Jello."
                    : " Roblox links still go to the stock Roblox player.");

            return true;
        }
        catch (Exception ex)
        {
            Log.WriteException("InstallerWindow::RunInstall", ex);

            LocationError.Text = ex.Message;
            LocationError.Visibility = Visibility.Visible;

            return false;
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => Finish(launcher: false);

    private void OpenLauncher_Click(object sender, RoutedEventArgs e) => Finish(launcher: true);

    private void Finish(bool launcher)
    {
        Completed = true;
        OpenLauncherOnClose = launcher;

        Close();
    }

    public bool Completed { get; private set; }

    public bool OpenLauncherOnClose { get; private set; }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_installed)
        {
            Finish(launcher: true);
            return;
        }

        var answer = MessageBox.Show(
            "Close without installing? Jello will ask again next time you run it.",
            "Jello Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            Close();
        }
    }
}
