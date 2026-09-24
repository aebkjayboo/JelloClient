using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private void BuildSkyboxFaces()
    {
        foreach (var face in Skybox.Faces)
        {
            var button = new Button
            {
                Style = (Style)FindResource("FluentButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = face,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            button.Click += SkyboxFace_Click;

            SkyboxFaces.Items.Add(button);
        }
    }

    private void RefreshSkyboxState()
    {
        SkyboxText.Text = Skybox.Describe();

        ClearSkyboxButton.Visibility = Skybox.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (Button button in SkyboxFaces.Items.OfType<Button>())
        {
            var face = (Skybox.Face)button.Tag;
            bool set = Skybox.HasFace(face);

            button.Content = set ? $"{face.Label}  •  set" : $"{face.Label}  •  default";

            button.Foreground = set
                ? (Brush)FindResource("TextPrimary")
                : (Brush)FindResource("TextTertiary");
        }
    }

    private void SkyboxFace_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var face = (Skybox.Face)button.Tag;

        // A face that is already set is cleared rather than reasked for, so the six
        // buttons are both the picker and the switch.
        if (Skybox.HasFace(face))
        {
            Skybox.ClearFace(face);
            RefreshSkyboxState();

            SetStatus($"{face.Label} face back to the Roblox default.");
            return;
        }

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Choose the {face.Label.ToLowerInvariant()} face",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Skybox.SetFace(face, picker.FileName);
            RefreshSkyboxState();

            SetStatus($"{face.Label} face set. It applies the next time Roblox is staged.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::SkyboxFace", ex);
            SetStatus($"Could not use that picture: {ex.Message}");
        }
    }

    private void ChooseSkyboxAll_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose one picture for all six faces",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Skybox.SetAll(picker.FileName);
            RefreshSkyboxState();

            SetStatus($"All six faces set from {Path.GetFileName(picker.FileName)}.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ChooseSkyboxAll", ex);
            SetStatus($"Could not use that picture: {ex.Message}");
        }
    }

    private void ClearSkybox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Skybox.ClearAll();
            RefreshSkyboxState();

            SetStatus("Custom sky removed. Roblox draws its own again.");
        }
        catch (Exception ex)
        {
            Log.WriteException("SettingsWindow::ClearSkybox", ex);
            SetStatus($"Could not remove it: {ex.Message}");
        }
    }
}
