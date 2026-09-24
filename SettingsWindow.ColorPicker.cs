using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace JelloClient;

public partial class SettingsWindow
{
    private const double SvW = 200;
    private const double SvH = 150;
    private const double HueW = 200;

    private double _hue;
    private double _sat;
    private double _val;

    private void TintSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        var colour = ParseColour(TintColourBox.Text) ?? Color.FromRgb(0x3B, 0x2A, 0x6B);
        (_hue, _sat, _val) = RgbToHsv(colour.R, colour.G, colour.B);

        Render(false);
        ColorPickerPopup.IsOpen = true;
    }

    private void Sv_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SvCanvas.CaptureMouse();
        SvFrom(e);
    }

    private void Sv_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && SvCanvas.IsMouseCaptured)
        {
            SvFrom(e);
        }
    }

    private void Sv_MouseUp(object sender, MouseButtonEventArgs e)
    {
        SvCanvas.ReleaseMouseCapture();
        Render(true);
    }

    private void SvFrom(MouseEventArgs e)
    {
        var p = e.GetPosition(SvCanvas);
        double w = SvCanvas.ActualWidth > 0 ? SvCanvas.ActualWidth : SvW;
        double h = SvCanvas.ActualHeight > 0 ? SvCanvas.ActualHeight : SvH;

        _sat = Clamp01(p.X / w);
        _val = 1 - Clamp01(p.Y / h);

        Render(false);
    }

    private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueCanvas.CaptureMouse();
        HueFrom(e);
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && HueCanvas.IsMouseCaptured)
        {
            HueFrom(e);
        }
    }

    private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
    {
        HueCanvas.ReleaseMouseCapture();
        Render(true);
    }

    private void HueFrom(MouseEventArgs e)
    {
        var p = e.GetPosition(HueCanvas);
        double w = HueCanvas.ActualWidth > 0 ? HueCanvas.ActualWidth : HueW;

        _hue = Clamp01(p.X / w) * 360;

        Render(false);
    }

    /// The hex box inside the popup, for typing an exact value. Programmatic writes are
    /// suppressed, so this only fires on real typing; it moves the thumbs to match.
    private void PickerHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (ParseColour(PickerHexBox.Text) is not { } colour)
        {
            return;
        }

        (_hue, _sat, _val) = RgbToHsv(colour.R, colour.G, colour.B);

        // Don't rewrite the box the person is typing in; move everything else.
        Render(true, keepHexText: true);
    }

    private void ColorPickerDone_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ColorPickerPopup.IsOpen = false;

    private void ColorPicker_Closed(object sender, EventArgs e)
    {
        Persist();
        RefreshTint();
    }

    /// Paints the current HSV everywhere at once: the thumbs, the saturation square's hue,
    /// the preview, the main swatch, both hex boxes, and the live setting the tint runner
    /// reads. Persisting is left to mouse-up and close so a drag does not hammer the disk.
    private void Render(bool persist, bool keepHexText = false)
    {
        Canvas.SetLeft(SvThumb, _sat * SvW - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _val) * SvH - SvThumb.Height / 2);

        Canvas.SetLeft(HueThumb, _hue / 360 * HueW - HueThumb.Width / 2);
        Canvas.SetTop(HueThumb, 0);

        SvHueRect.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));

        var colour = HsvToRgb(_hue, _sat, _val);
        var brush = new SolidColorBrush(colour);
        string hex = ToHex(colour);

        PickerPreview.Background = brush;
        TintSwatch.Background = brush;

        _suppressEvents = true;
        TintColourBox.Text = hex;
        if (!keepHexText)
        {
            PickerHexBox.Text = hex;
        }
        _suppressEvents = false;

        Settings.GuiTintColour = hex;

        if (persist)
        {
            Persist();
            RefreshTint();
        }
    }

    private static Color? ParseColour(string? text)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString((text ?? "").Trim());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double d = max - min;

        double h = 0;

        if (d != 0)
        {
            if (max == rf)
            {
                h = (gf - bf) / d % 6;
            }
            else if (max == gf)
            {
                h = (bf - rf) / d + 2;
            }
            else
            {
                h = (rf - gf) / d + 4;
            }

            h *= 60;

            if (h < 0)
            {
                h += 360;
            }
        }

        double s = max == 0 ? 0 : d / max;

        return (h, s, max);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;

        double r = 0, g = 0, b = 0;

        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
