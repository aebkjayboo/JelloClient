using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JelloClient.Roblox.Memory;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private static readonly double[] TintStrengthOrder = { 0.25, 0.5, 0.75, 1.0 };
    private static readonly string[] TintStrengthLabels = { "Light", "Half", "Heavy", "Solid" };

    private void LoadLabState()
    {
        if (TintStrengthCombo.Items.Count == 0)
        {
            foreach (string label in TintStrengthLabels)
            {
                TintStrengthCombo.Items.Add(new ComboBoxItem { Content = label });
            }
        }

        LabToggle.IsChecked = Settings.LabEnabled;
        TintToggle.IsChecked = Settings.GuiTintEnabled;
        TintColourBox.Text = Settings.GuiTintColour ?? "#3B2A6B";

        int strength = Array.FindIndex(TintStrengthOrder,
            v => Math.Abs(v - Settings.GuiTintStrength) < 0.01);
        TintStrengthCombo.SelectedIndex = strength < 0 ? 3 : strength;

        RefreshLabState();
        RefreshTint();
    }

    private void RefreshTint()
    {
        TintText.Text = !Settings.GuiTintEnabled
            ? "Off. The app looks the way Roblox draws it."
            : GuiTintRunner.Status;

        try
        {
            var colour = (Color)ColorConverter.ConvertFromString(TintColourBox.Text);
            TintSwatch.Background = new SolidColorBrush(colour);
        }
        catch (Exception)
        {
            TintSwatch.Background = Brushes.Transparent;
        }
    }

    private void Tint_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.GuiTintEnabled = TintToggle.IsChecked == true;
        Persist();

        GuiTintRunner.Refresh();
        RefreshTint();

        SetStatus(Settings.GuiTintEnabled
            ? "App tint on. Bring the Roblox app to the front to see it."
            : "App tint off. The colours are put back.");
    }

    private void TintColour_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        Settings.GuiTintColour = TintColourBox.Text.Trim();
        Persist();

        RefreshTint();
    }

    private void TintStrength_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || TintStrengthCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.GuiTintStrength = TintStrengthOrder[TintStrengthCombo.SelectedIndex];
        Persist();

        RefreshTint();
    }

    private void RefreshLabState()
    {
        LabText.Text = Settings.LabEnabled
            ? "On. The tools below read the running client."
            : "Off. Turn it on to use the tools below. Nothing here runs while it is off.";

        LabTools.Visibility = Settings.LabEnabled ? Visibility.Visible : Visibility.Collapsed;

        // The override panel follows the Lab: shown here when the Lab is on, and in its own
        // Offsets tab when it is off.
        PlaceOverridePanel();
    }

    private void Lab_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = LabToggle.IsChecked == true;

        // The moment of opting in is where the one-line caveat is said plainly, not buried
        // in the page text above.
        if (wanted && !Settings.LabEnabled)
        {
            var answer = MessageBox.Show(
                "The Lab reads the running Roblox client's memory directly.\n\n"
                + "It only reads - nothing here writes to the client or touches a game - but "
                + "reading another program's memory is not something anti-cheat is meant to "
                + "see. Use it on your own account, at your own discretion.\n\n"
                + "Turn the Lab on?",
                "Lab",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                _suppressEvents = true;
                LabToggle.IsChecked = false;
                _suppressEvents = false;

                SetStatus("Left off.");
                return;
            }
        }

        Settings.LabEnabled = wanted;
        Persist();

        // Turning the Lab off stops everything under it, including the tint writer.
        GuiTintRunner.Refresh();

        RefreshLabState();
        RefreshTint();

        SetStatus(wanted ? "Lab on." : "Lab off.");
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (!Settings.LabEnabled)
        {
            return;
        }

        InspectButton.IsEnabled = false;
        InspectorText2.Text = "Reading the client...";

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var snapshot = await LiveInspector.CaptureAsync(cancellation.Token);

            InspectorText2.Text = snapshot.Summary;

            if (!snapshot.Attached)
            {
                InspectorResult.Visibility = Visibility.Collapsed;
                return;
            }

            InspectorServices.Text = "Services: " + string.Join(", ", snapshot.TopServices);

            InspectorFrames.Items.Clear();

            foreach (var frame in snapshot.Frames)
            {
                InspectorFrames.Items.Add(FrameRow(frame));
            }

            InspectorResult.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::Inspect", ex);
            InspectorText2.Text = $"Could not read the client: {ex.Message}";
        }
        finally
        {
            InspectButton.IsEnabled = true;
        }
    }

    /// One drawable frame, with a swatch of the colour it actually holds.
    private static UIElement FrameRow(LiveInspector.GuiFrame frame)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatch = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        try
        {
            swatch.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(frame.Colour));
        }
        catch (Exception)
        {
            swatch.Background = Brushes.Transparent;
        }

        Grid.SetColumn(swatch, 0);

        var name = new TextBlock
        {
            Text = $"{frame.ClassName}  {frame.Path}",
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(name, 1);

        var meta = new TextBlock
        {
            Text = $"{frame.Colour}  {frame.Width}x{frame.Height}",
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.TryFindResource("TextTertiary") as Brush ?? Brushes.Gray
        };

        Grid.SetColumn(meta, 2);

        row.Children.Add(swatch);
        row.Children.Add(name);
        row.Children.Add(meta);

        return row;
    }
}
