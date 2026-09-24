using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JelloClient;

public partial class SettingsWindow
{
    /// The flag grid, the history list and the JSON editor all scroll internally, which
    /// otherwise traps the wheel and stops the page behind them from moving. Once the
    /// inner control is at its own edge the wheel is handed back to the page.
    private void HookWheelForwarding()
    {
        foreach (var element in new FrameworkElement[] { FlagGrid, HistoryList, FastFlagsBox })
        {
            element.PreviewMouseWheel += ForwardWheelAtEdges;
        }
    }

    private static void ForwardWheelAtEdges(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var scroll = FindScrollViewer(element);

        bool atTop = scroll is null || scroll.VerticalOffset <= 0.001;
        bool atBottom = scroll is null || scroll.VerticalOffset >= scroll.ScrollableHeight - 0.001;

        if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom))
        {
            return;
        }

        e.Handled = true;

        if (VisualTreeHelper.GetParent(element) is not UIElement parent)
        {
            return;
        }

        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = element
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
