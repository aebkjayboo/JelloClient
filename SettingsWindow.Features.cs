using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using JelloClient.Roblox;
using JelloClient.Services;
using JelloClient.UI.Bootstrapper;

namespace JelloClient;

public partial class SettingsWindow
{
    private static readonly CursorType[] CursorOrder =
        { CursorType.Default, CursorType.From2013, CursorType.From2006, CursorType.Custom };

    private static readonly string[] CursorLabels = { "Default", "2013", "2006", "Custom..." };

    private static readonly EmojiType[] EmojiOrder =
        { EmojiType.Default, EmojiType.Catmoji, EmojiType.Windows11, EmojiType.Windows10, EmojiType.Windows8 };

    private static readonly string[] EmojiLabels = { "Default", "Catmoji", "Windows 11", "Windows 10", "Windows 8.1" };

    private CustomIntegration? SelectedIntegration => IntegrationsList.SelectedItem as CustomIntegration;

    private void InitialiseFeatureLists()
    {
        foreach (string label in CursorLabels)
        {
            CursorCombo.Items.Add(new ComboBoxItem { Content = label });
        }

        foreach (string label in EmojiLabels)
        {
            EmojiCombo.Items.Add(new ComboBoxItem { Content = label });
        }

        RebuildStyleCombo();
        InitialiseAppearanceLists();
        BuildResourceLists();
        BuildMatchmakerLists();
        BuildRobloxThemeList();
        BuildSkyboxFaces();
        InitialiseCleanupLists();
        InitialisePerformanceLists();
    }

    private void LoadFeatureState()
    {
        CursorCombo.SelectedIndex = Math.Max(0, Array.IndexOf(CursorOrder, Settings.CursorType));
        EmojiCombo.SelectedIndex = Math.Max(0, Array.IndexOf(EmojiOrder, Settings.EmojiType));
        OldSoundsToggle.IsChecked = Settings.OldCharacterSounds;
        OldAvatarToggle.IsChecked = Settings.OldAvatarBackground;

        SelectCurrentStyle();

        RefreshIntegrationList();
        LoadAppearanceState();
        LoadCleanupState();
        LoadPerformanceState();
        LoadPrivacyState();
        LoadResourceState();
        LoadDuckAudioState();
        LoadGameIconState();
        LoadMatchmakerState();
        LoadRobloxThemeState();
        LoadLabState();
        LoadOverrideState();
        RefreshSkyboxState();
        RefreshFlagAudit();
    }

    private void RefreshModState()
    {
        ModAssetsPathText.Text = $"Presets are built in. Drop a replacement here to override one: {Paths.ModAssets}";

        CursorAssetText.Text = Describe(
            "Replaces the in-game mouse cursor with the 2006 or 2013 artwork.",
            ModPresets.Cursors.Values.SelectMany(files => files).ToList());

        SoundsAssetText.Text = Describe(
            "Restores the pre-2015 walk, jump, land and swim sounds.",
            ModPresets.OldCharacterSounds);

        AvatarAssetText.Text = Describe(
            "Restores the old avatar editor backdrop.",
            ModPresets.OldAvatarBackground);

        BootstrapperStyleText.Text = string.IsNullOrEmpty(Settings.BootstrapperTheme)
            ? BootstrapperStyles.Explain(Settings.BootstrapperStyle)
            : $"Custom theme '{Settings.BootstrapperTheme}'.";

        RefreshThemeStatus();

        RefreshAppearanceState();
    }

    private static string Describe(string description, IReadOnlyList<ModFile> files)
    {
        var overridden = ModPresets.OverriddenAssets(files);

        return overridden.Count == 0
            ? description
            : $"{description} Using {overridden.Count} replacement file(s) from the preset assets folder.";
    }

    private void CursorCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var type = CursorOrder[CursorCombo.SelectedIndex];

        if (type == CursorType.Custom)
        {
            ChooseCustomCursor();
            return;
        }

        Settings.CursorType = type;
        Persist();

        try
        {
            ModPresets.ApplyCursor(type);
            RefreshModState();

            SetStatus(type == CursorType.Default
                ? "Cursor restored to the Roblox default."
                : $"Cursor set to {CursorLabels[CursorCombo.SelectedIndex]}. It applies on the next launch.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::CursorCombo", ex);
            SetStatus($"Could not apply the cursor: {ex.Message}");
        }
    }

    private void ChooseCustomCursor()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose two cursor images: ArrowCursor then ArrowFarCursor",
            Filter = "Cursor images (*.png)|*.png",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            _suppressEvents = true;
            CursorCombo.SelectedIndex = Math.Max(0, Array.IndexOf(CursorOrder, Settings.CursorType));
            _suppressEvents = false;
            return;
        }

        string arrow = dialog.FileNames[0];
        string arrowFar = dialog.FileNames.Length > 1 ? dialog.FileNames[1] : arrow;

        try
        {
            ModPresets.ApplyCustomCursor(arrow, arrowFar);

            Settings.CursorType = CursorType.Custom;
            Persist();
            RefreshModState();

            SetStatus($"Custom cursor applied from {Path.GetFileName(arrow)} and {Path.GetFileName(arrowFar)}.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ChooseCustomCursor", ex);
            SetStatus($"Could not apply the custom cursor: {ex.Message}");
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string url)
        {
            OpenUrl(url);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            Log.Write("SettingsWindow::OpenUrl", url);
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::OpenUrl", ex);
            SetStatus($"Could not open {url}: {ex.Message}");
        }
    }

    private async void EmojiCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var type = EmojiOrder[EmojiCombo.SelectedIndex];

        Settings.EmojiType = type;
        Persist();

        EmojiCombo.IsEnabled = false;
        EmojiStatusText.Text = type == EmojiType.Default ? "Removing the override..." : "Downloading the emoji font...";

        try
        {
            await ModPresets.ApplyEmojiAsync(type, AppState.Http, CancellationToken.None);

            EmojiStatusText.Text = type == EmojiType.Default
                ? "Replaces the in-game emoji font. Downloaded on demand, so nothing needs to be supplied."
                : $"{EmojiLabels[EmojiCombo.SelectedIndex]} is installed. It applies on the next launch.";
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::EmojiCombo", ex);
            EmojiStatusText.Text = $"Could not install the emoji font: {ex.Message}";
        }
        finally
        {
            EmojiCombo.IsEnabled = true;
        }
    }

    private void OldSoundsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        ApplyBoolPreset(
            OldSoundsToggle.IsChecked == true,
            ModPresets.OldCharacterSounds,
            enabled => Settings.OldCharacterSounds = enabled,
            "Old character sounds");
    }

    private void OldAvatarToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        ApplyBoolPreset(
            OldAvatarToggle.IsChecked == true,
            ModPresets.OldAvatarBackground,
            enabled => Settings.OldAvatarBackground = enabled,
            "Old avatar editor background");
    }

    private void ApplyBoolPreset(
        bool enabled,
        IReadOnlyList<ModFile> files,
        Action<bool> assign,
        string label)
    {
        try
        {
            ModPresets.Apply(files, enabled);

            assign(enabled);
            Persist();
            RefreshModState();

            SetStatus(enabled
                ? $"{label} applied. It takes effect on the next launch."
                : $"{label} removed.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ApplyBoolPreset", ex);
            SetStatus($"Could not apply {label}: {ex.Message}");
        }
    }

    private void OpenModAssets_Click(object sender, RoutedEventArgs e)
    {
        Paths.EnsureCreated();
        OpenInExplorer(Paths.ModAssets);
    }

    private void RefreshIntegrationList()
    {
        object? selected = IntegrationsList.SelectedItem;

        IntegrationsList.Items.Clear();

        foreach (var integration in Settings.CustomIntegrations)
        {
            IntegrationsList.Items.Add(integration);
        }

        if (selected is not null && IntegrationsList.Items.Contains(selected))
        {
            IntegrationsList.SelectedItem = selected;
        }

        RefreshIntegrationDetail();
    }

    private void RefreshIntegrationDetail()
    {
        var integration = SelectedIntegration;

        DeleteIntegrationButton.IsEnabled = integration is not null;
        IntegrationDetail.Visibility = integration is null ? Visibility.Collapsed : Visibility.Visible;
        NoIntegrationText.Visibility = integration is null ? Visibility.Visible : Visibility.Collapsed;

        if (integration is null)
        {
            return;
        }

        _suppressEvents = true;

        IntegrationNameBox.Text = integration.Name;
        IntegrationLocationBox.Text = integration.Location;
        IntegrationArgsBox.Text = integration.LaunchArgs;
        IntegrationAutoCloseBox.IsChecked = integration.AutoClose;

        _suppressEvents = false;
    }

    private void IntegrationSelection_Changed(object sender, SelectionChangedEventArgs e) => RefreshIntegrationDetail();

    private void AddIntegration_Click(object sender, RoutedEventArgs e)
    {
        var integration = new CustomIntegration { Name = "New integration" };

        Settings.CustomIntegrations.Add(integration);
        Persist();

        RefreshIntegrationList();

        IntegrationsList.SelectedItem = integration;
        IntegrationNameBox.Focus();
        IntegrationNameBox.SelectAll();
    }

    private void DeleteIntegration_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIntegration is not { } integration)
        {
            return;
        }

        Settings.CustomIntegrations.Remove(integration);
        Persist();

        RefreshIntegrationList();
        SetStatus($"Removed the '{integration.Name}' integration.");
    }

    private void IntegrationName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || SelectedIntegration is not { } integration)
        {
            return;
        }

        integration.Name = IntegrationNameBox.Text;

        int index = IntegrationsList.SelectedIndex;

        RefreshIntegrationList();
        IntegrationsList.SelectedIndex = index;

        Persist();
    }

    private void IntegrationLocation_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedIntegration is not { } integration)
        {
            return;
        }

        integration.Location = IntegrationLocationBox.Text.Trim();
        Persist();
    }

    private void IntegrationArgs_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedIntegration is not { } integration)
        {
            return;
        }

        integration.LaunchArgs = IntegrationArgsBox.Text;
        Persist();
    }

    private void IntegrationAutoClose_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedIntegration is not { } integration)
        {
            return;
        }

        integration.AutoClose = IntegrationAutoCloseBox.IsChecked == true;
        Persist();
    }

    private void BrowseIntegration_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIntegration is not { } integration)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a program to launch alongside Roblox",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        integration.Location = dialog.FileName;

        if (string.IsNullOrWhiteSpace(integration.Name) || integration.Name == "New integration")
        {
            integration.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        Persist();

        int index = IntegrationsList.SelectedIndex;

        RefreshIntegrationList();
        IntegrationsList.SelectedIndex = index;
    }

    private List<string> _themeNames = new();

    private void RebuildStyleCombo()
    {
        _themeNames = BootstrapperTheme.Discover().ToList();

        BootstrapperStyleCombo.Items.Clear();

        foreach (var style in BootstrapperStyles.Selectable)
        {
            BootstrapperStyleCombo.Items.Add(new ComboBoxItem { Content = BootstrapperStyles.Describe(style) });
        }

        foreach (string theme in _themeNames)
        {
            BootstrapperStyleCombo.Items.Add(new ComboBoxItem { Content = $"Theme: {theme}" });
        }
    }

    private void SelectCurrentStyle()
    {
        int builtIn = BootstrapperStyles.Selectable.Count;

        if (!string.IsNullOrEmpty(Settings.BootstrapperTheme))
        {
            int index = _themeNames.IndexOf(Settings.BootstrapperTheme);

            if (index >= 0)
            {
                BootstrapperStyleCombo.SelectedIndex = builtIn + index;
                return;
            }
        }

        BootstrapperStyleCombo.SelectedIndex =
            Math.Max(0, BootstrapperStyles.Selectable.ToList().IndexOf(Settings.BootstrapperStyle));
    }

    private void BootstrapperStyleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        int index = BootstrapperStyleCombo.SelectedIndex;
        int builtIn = BootstrapperStyles.Selectable.Count;

        if (index >= builtIn)
        {
            string theme = _themeNames[index - builtIn];

            try
            {
                var loaded = BootstrapperTheme.Load(theme);

                Settings.BootstrapperTheme = theme;
                Persist();

                BootstrapperStyleText.Text =
                    $"Custom theme '{loaded.Name}', {loaded.Width:N0} by {loaded.Height:N0}, rendering {Path.GetFileName(loaded.PanelPath)}.";

                SetStatus($"Bootstrapper theme set to '{theme}'.");
            }
            catch (ThemeParseException ex)
            {
                Log.Write("SettingsWindow::BootstrapperStyle", $"Theme '{theme}' is invalid: {ex.Message}");

                BootstrapperStyleText.Text = $"'{theme}' could not be loaded: {ex.Message}";
                SetStatus($"Theme '{theme}' is not usable.");

                _suppressEvents = true;
                SelectCurrentStyle();
                _suppressEvents = false;
            }

            return;
        }

        Settings.BootstrapperTheme = null;
        Settings.BootstrapperStyle = BootstrapperStyles.Selectable[index];
        Persist();

        BootstrapperStyleText.Text = BootstrapperStyles.Explain(Settings.BootstrapperStyle);
        SetStatus($"Bootstrapper style set to {BootstrapperStyles.Describe(Settings.BootstrapperStyle)}.");
    }

    private void OpenThemes_Click(object sender, RoutedEventArgs e)
    {
        Paths.EnsureCreated();
        OpenInExplorer(Paths.Themes);
    }

    private void ReloadThemes_Click(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;
        RebuildStyleCombo();
        SelectCurrentStyle();
        _suppressEvents = false;

        RefreshThemeStatus();
        SetStatus($"Found {_themeNames.Count} theme(s).");
    }

    private void RefreshThemeStatus()
    {
        bool runtime = UI.Bootstrapper.CustomDialog.IsRuntimeAvailable(out string? version);

        string themes = _themeNames.Count == 0
            ? $"No themes installed. Drop a theme folder containing Theme.xml into {Paths.Themes}."
            : $"{_themeNames.Count} installed: {string.Join(", ", _themeNames)}.";

        ThemeStatusText.Text = runtime
            ? $"{themes} WebView2 runtime {version} is present."
            : $"{themes} WebView2 runtime is NOT installed, so themes cannot render and Jello falls back to a built-in style.";
    }

    private async void PreviewBootstrapper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = await BootstrapperStyles.CreateAsync();

        if (dialog is null)
        {
            SetStatus("The None style has no dialog to preview.");
            return;
        }

        dialog.CancelEnabled = true;
        dialog.Message = "Downloading RobloxApp.zip";
        dialog.ProgressIndeterminate = false;
        dialog.ProgressMaximum = 100;
        dialog.ShowBootstrapper();

        for (int value = 0; value <= 100; value += 4)
        {
            dialog.ProgressValue = value;
            await Task.Delay(60);
        }

        dialog.ShowSuccess("Starting Roblox...");

        await Task.Delay(1200);

        dialog.CloseBootstrapper();
    }


}
