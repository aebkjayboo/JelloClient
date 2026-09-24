using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace JelloClient.UI.Controls;

/// A two field hours and minutes picker for an interval. Each field steps with its own
/// pair of chevrons and wraps at its own bounds, so the whole thing stays inside
/// [MinimumMinutes, MaximumMinutes].
public class IntervalPicker : Control
{
    public const int MinuteStep = 15;

    public static readonly DependencyProperty MinutesProperty = DependencyProperty.Register(
        nameof(Minutes),
        typeof(int),
        typeof(IntervalPicker),
        new FrameworkPropertyMetadata(720, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMinutesChanged));

    public static readonly DependencyProperty MinimumMinutesProperty = DependencyProperty.Register(
        nameof(MinimumMinutes), typeof(int), typeof(IntervalPicker), new PropertyMetadata(15));

    public static readonly DependencyProperty MaximumMinutesProperty = DependencyProperty.Register(
        nameof(MaximumMinutes), typeof(int), typeof(IntervalPicker), new PropertyMetadata(7 * 24 * 60));

    public static readonly DependencyProperty HoursTextProperty = DependencyProperty.Register(
        nameof(HoursText), typeof(string), typeof(IntervalPicker), new PropertyMetadata("12"));

    public static readonly DependencyProperty MinutesTextProperty = DependencyProperty.Register(
        nameof(MinutesText), typeof(string), typeof(IntervalPicker), new PropertyMetadata("00"));

    public int Minutes
    {
        get => (int)GetValue(MinutesProperty);
        set => SetValue(MinutesProperty, value);
    }

    public int MinimumMinutes
    {
        get => (int)GetValue(MinimumMinutesProperty);
        set => SetValue(MinimumMinutesProperty, value);
    }

    public int MaximumMinutes
    {
        get => (int)GetValue(MaximumMinutesProperty);
        set => SetValue(MaximumMinutesProperty, value);
    }

    public string HoursText
    {
        get => (string)GetValue(HoursTextProperty);
        private set => SetValue(HoursTextProperty, value);
    }

    public string MinutesText
    {
        get => (string)GetValue(MinutesTextProperty);
        private set => SetValue(MinutesTextProperty, value);
    }

    public event EventHandler? ValueChanged;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        Hook("PART_HoursUp", 60);
        Hook("PART_HoursDown", -60);
        Hook("PART_MinutesUp", MinuteStep);
        Hook("PART_MinutesDown", -MinuteStep);

        Refresh();
    }

    private void Hook(string part, int delta)
    {
        if (GetTemplateChild(part) is RepeatButton button)
        {
            button.Click += (_, _) => Step(delta);
        }
    }

    private void Step(int delta)
    {
        int stepped = Minutes + delta;

        Minutes = Math.Clamp(stepped, MinimumMinutes, MaximumMinutes);
    }

    private static void OnMinutesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var picker = (IntervalPicker)sender;

        picker.Refresh();
        picker.ValueChanged?.Invoke(picker, EventArgs.Empty);
    }

    private void Refresh()
    {
        int minutes = Math.Clamp(Minutes, MinimumMinutes, MaximumMinutes);

        HoursText = (minutes / 60).ToString();
        MinutesText = (minutes % 60).ToString("00");
    }
}
