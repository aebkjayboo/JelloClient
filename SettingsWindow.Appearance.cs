using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private static readonly LauncherLayout[] LayoutOrder = { LauncherLayout.Vertical, LauncherLayout.Horizontal };
    private static readonly string[] LayoutLabels = { "Vertical", "Horizontal" };



    private static readonly AppTheme[] ThemeOrder = { AppTheme.Dark, AppTheme.Light, AppTheme.System };
    private static readonly string[] ThemeLabels = { "Dark", "Light", "Match Windows" };

    private IReadOnlyList<Locales.Option> _locales = Array.Empty<Locales.Option>();

    private void InitialiseAppearanceLists()
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

        foreach (string label in LayoutLabels)
        {
            LayoutCombo.Items.Add(new ComboBoxItem { Content = label });
        }
    }

    private void LoadAppearanceState()
    {
        ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeOrder, Settings.Theme));

        int locale = _locales.ToList().FindIndex(option => option.Name == Settings.Locale);
        LocaleCombo.SelectedIndex = locale < 0 ? 0 : locale;

        LayoutCombo.SelectedIndex = Math.Max(0, Array.IndexOf(LayoutOrder, Settings.LauncherLayout));

        RefreshAppearanceState();
    }

    private void RefreshAppearanceState()
    {
        ThemeText.Text = Settings.Theme == AppTheme.System
            ? $"Following Windows, which is currently {(Themes.IsLight ? "light" : "dark")}."
            : $"Always {ThemeLabels[Math.Max(0, Array.IndexOf(ThemeOrder, Settings.Theme))].ToLowerInvariant()}.";

        LocaleText.Text = Locales.Describe(Settings.Locale);

        LayoutText.Text = Settings.LauncherLayout == LauncherLayout.Vertical
            ? "Tall: logo above the status, with a full width launch button underneath."
            : "Wide: logo beside the status, with the buttons stacked in a column on the right.";

        ShortcutText.Text = ShortcutManager.Exists
            ? $"On your desktop as {ShortcutManager.ShortcutName}, icon: {Settings.ShortcutIconPath ?? "the Jello icon"}"
            : "No shortcut yet. Choosing an icon creates one on your desktop.";

        RemoveShortcutButton.Visibility = ShortcutManager.Exists ? Visibility.Visible : Visibility.Collapsed;


        BootstrapperIconText.Text = string.IsNullOrEmpty(Settings.BootstrapperIconPath)
            ? "The dialog uses the Jello icon. Choose an .ico to override it."
            : Settings.BootstrapperIconPath;

        ResetIconButton.Visibility = string.IsNullOrEmpty(Settings.BootstrapperIconPath)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an icon for the bootstrapper dialog",
            Filter = "Icons (*.ico)|*.ico"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        Settings.BootstrapperIconPath = picker.FileName;
        Persist();

        RefreshAppearanceState();
        SetStatus("Bootstrapper icon updated.");
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        Settings.BootstrapperIconPath = null;
        Persist();

        RefreshAppearanceState();
        SetStatus("Bootstrapper icon reset.");
    }

    private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.Theme = ThemeOrder[ThemeCombo.SelectedIndex];
        Persist();

        Log.Write("SettingsWindow::ThemeCombo", $"Theme set to {Settings.Theme}");

        // Styles resolve their colours when a window loads, so the open windows are
        // rebuilt rather than left half painted in the old palette.
        App.ApplyTheme("Appearance");
    }

    private void LocaleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || LocaleCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.Locale = _locales[LocaleCombo.SelectedIndex].Name;
        Persist();

        Locales.Apply(Settings.Locale);
        RefreshAppearanceState();
        RefreshFastFlagViews();

        SetStatus(Locales.Describe(Settings.Locale));
    }

    private void LayoutCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.LauncherLayout = LayoutOrder[LayoutCombo.SelectedIndex];
        Persist();

        RefreshAppearanceState();
        App.ApplyLauncherLayout();

        SetStatus($"Launcher layout set to {LayoutLabels[LayoutCombo.SelectedIndex]}.");
    }

    private void PreviewLayout_Click(object sender, RoutedEventArgs e)
    {
        App.ApplyLauncherLayout();
        App.ShowLauncher();

        SetStatus($"Showing the {LayoutLabels[Math.Max(0, LayoutCombo.SelectedIndex)].ToLowerInvariant()} launcher.");
    }

    private void ChooseShortcutIcon_Click(object sender, RoutedEventArgs e)
    {
        ShortcutManager.ExtractIcons();

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an icon for the Roblox shortcut",
            Filter = "Icons (*.ico)|*.ico|Programs (*.exe)|*.exe",
            InitialDirectory = ShortcutManager.IconsDirectory
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ShortcutManager.CreateOrUpdate(picker.FileName);

            Settings.ShortcutIconPath = picker.FileName;
            Persist();

            RefreshAppearanceState();
            SetStatus($"Desktop shortcut created with {Path.GetFileName(picker.FileName)}.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ChooseShortcutIcon", ex);
            SetStatus($"Could not create the shortcut: {ex.Message}");
        }
    }

    private void RemoveShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShortcutManager.Remove();

            Settings.ShortcutIconPath = null;
            Persist();

            RefreshAppearanceState();
            SetStatus("Desktop shortcut removed.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::RemoveShortcut", ex);
            SetStatus($"Could not remove the shortcut: {ex.Message}");
        }
    }




}
