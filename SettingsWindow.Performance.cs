using System.Windows;
using System.Windows.Controls;
using JelloClient.Interop;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private static readonly int?[] FramerateOrder = { null, 30, 60, 75, 120, 144, 165, 240, 360 };

    private static readonly RenderingMode[] RendererOrder =
    {
        RenderingMode.Automatic,
        RenderingMode.Direct3D11,
        RenderingMode.Direct3D10,
        RenderingMode.Vulkan,
        RenderingMode.OpenGL
    };

    private static readonly string[] RendererLabels =
        { "Automatic", "Direct3D 11", "Direct3D 10", "Vulkan", "OpenGL" };

    private IReadOnlyList<DisplayInfo> _displays = Array.Empty<DisplayInfo>();

    private int _matchRefreshIndex;

    private void InitialisePerformanceLists()
    {
        _displays = Displays.All();

        var main = _displays.FirstOrDefault(display => display.Primary);

        foreach (int? cap in FramerateOrder)
        {
            FramerateCombo.Items.Add(new ComboBoxItem
            {
                Content = cap is null ? "Uncapped" : $"{cap} fps"
            });
        }

        _matchRefreshIndex = FramerateCombo.Items.Count;

        FramerateCombo.Items.Add(new ComboBoxItem
        {
            Content = main is { RefreshHz: > 0 } ? $"Match display ({main.RefreshHz} fps)" : "Match display"
        });

        foreach (var display in _displays)
        {
            DisplayCombo.Items.Add(new ComboBoxItem { Content = display.Label });
        }

        foreach (string label in RendererLabels)
        {
            RendererCombo.Items.Add(new ComboBoxItem { Content = label });
        }

        QualityCombo.Items.Add(new ComboBoxItem { Content = "Automatic" });

        for (int level = 1; level <= 21; level++)
        {
            QualityCombo.Items.Add(new ComboBoxItem { Content = $"Level {level}" });
        }
    }

    private void LoadPerformanceState()
    {
        int? framerate = Performance.Framerate(Settings.FastFlags);
        int index = Array.IndexOf(FramerateOrder, framerate);

        FramerateCombo.SelectedIndex = index >= 0 ? index : _matchRefreshIndex;

        int display = _displays.ToList().FindIndex(entry => entry.DeviceName == Settings.PreferredDisplay);
        DisplayCombo.SelectedIndex = display < 0
            ? Math.Max(0, _displays.ToList().FindIndex(entry => entry.Primary))
            : display;

        RendererCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RendererOrder, Performance.Renderer(Settings.FastFlags)));
        QualityCombo.SelectedIndex = Performance.Quality(Settings.FastFlags) is { } level && level is >= 1 and <= 21
            ? level
            : 0;

        ShowFpsToggle.IsChecked = Performance.ShowsFramerate(Settings.FastFlags);

        RefreshPerformanceState();
    }

    private DisplayInfo? ChosenDisplay =>
        DisplayCombo.SelectedIndex >= 0 && DisplayCombo.SelectedIndex < _displays.Count
            ? _displays[DisplayCombo.SelectedIndex]
            : _displays.FirstOrDefault(display => display.Primary);

    private void RefreshPerformanceState()
    {
        int hz = ChosenDisplay?.RefreshHz ?? 0;

        FramerateText.Text = Performance.DescribeFramerate(Performance.Framerate(Settings.FastFlags), hz);

        DisplayText.Text = _displays.Count <= 1
            ? "One display detected, so Roblox opens where it always does."
            : "Jello moves the client onto this display once it opens. The frame cap can follow its refresh rate.";

        RendererText.Text = Performance.DescribeRenderer(Performance.Renderer(Settings.FastFlags));
        QualityText.Text = Performance.DescribeQuality(Performance.Quality(Settings.FastFlags));
    }

    private void Framerate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        int? cap = FramerateCombo.SelectedIndex == _matchRefreshIndex
            ? ChosenDisplay?.RefreshHz is > 0 ? ChosenDisplay!.RefreshHz : 60
            : FramerateOrder[FramerateCombo.SelectedIndex];

        FastFlagHistory.Record(
            cap is null ? "Uncapped the frame rate" : $"Capped the frame rate at {cap}",
            Settings.FastFlags);

        Performance.SetFramerate(Settings.FastFlags, cap);
        Persist();

        AfterPerformanceChange(cap is null
            ? "Frame rate uncapped."
            : $"Frame rate capped at {cap} fps.");
    }

    private void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ChosenDisplay is not { } display)
        {
            return;
        }

        Settings.PreferredDisplay = display.Primary ? null : display.DeviceName;
        Persist();

        RefreshPerformanceState();

        SetStatus($"Roblox will open on {display.Label}.");
    }

    private void Renderer_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var mode = RendererOrder[RendererCombo.SelectedIndex];

        FastFlagHistory.Record($"Set the graphics API to {mode}", Settings.FastFlags);

        Performance.SetRenderer(Settings.FastFlags, mode);
        Persist();

        AfterPerformanceChange($"Graphics API set to {RendererLabels[RendererCombo.SelectedIndex]}.");
    }

    private void Quality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        int? level = QualityCombo.SelectedIndex == 0 ? null : QualityCombo.SelectedIndex;

        FastFlagHistory.Record(
            level is null ? "Cleared the quality override" : $"Pinned the quality level to {level}",
            Settings.FastFlags);

        Performance.SetQuality(Settings.FastFlags, level);
        Persist();

        AfterPerformanceChange(level is null
            ? "Quality left to the in-game slider."
            : $"Quality pinned to level {level}.");
    }

    private void ShowFps_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool shown = ShowFpsToggle.IsChecked == true;

        Performance.SetShowsFramerate(Settings.FastFlags, shown);
        Persist();

        AfterPerformanceChange(shown ? "Frame counter on." : "Frame counter off.");
    }

    /// These are flags like any other, so the editor, the history and the live client all
    /// need to hear about the change.
    private void AfterPerformanceChange(string status)
    {
        RefreshPerformanceState();
        RefreshFastFlagsEditor();
        RefreshFastFlagViews();

        if (Settings.LiveFlagInjection && PushLiveFlags())
        {
            SetStatus($"{status} Pushed to the running client.");
            return;
        }

        SetStatus($"{status} Applies on the next launch.");
    }
}
