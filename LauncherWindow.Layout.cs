using System.Windows;
using System.Windows.Controls;
using JelloClient.Services;

namespace JelloClient;

public partial class LauncherWindow
{
    internal void ApplyLayout() => ApplyLayout(AppState.Settings.LauncherLayout);

    internal void ApplyLayout(LauncherLayout layout)
    {
        ContentGrid.Children.Clear();
        ContentGrid.RowDefinitions.Clear();
        ContentGrid.ColumnDefinitions.Clear();

        foreach (var child in new FrameworkElement[] { BrandPanel, InfoPanel, LaunchButton, ActionRow, SupportButton })
        {
            child.ClearValue(Grid.RowProperty);
            child.ClearValue(Grid.ColumnProperty);
            child.ClearValue(Grid.RowSpanProperty);
        }

        if (layout == LauncherLayout.Horizontal)
        {
            BuildHorizontal();
        }
        else
        {
            BuildVertical();
        }

        Log.Write("LauncherWindow::ApplyLayout", $"Launcher laid out as {layout}");
    }

    private void BuildVertical()
    {
        Width = 420;
        Height = 580;

        ContentGrid.Margin = new Thickness(28, 0, 28, 26);
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        LogoImage.Width = 128;
        LogoImage.Height = 128;

        BrandPanel.HorizontalAlignment = HorizontalAlignment.Center;
        BrandPanel.VerticalAlignment = VerticalAlignment.Center;
        BrandPanel.Margin = new Thickness(0);

        InfoPanel.Margin = new Thickness(0, 0, 0, 16);
        ActionRow.Margin = new Thickness(0, 10, 0, 0);

        Place(BrandPanel, 0, 0);
        Place(InfoPanel, 1, 0);
        Place(LaunchButton, 2, 0);
        Place(ActionRow, 3, 0);
        Place(SupportButton, 4, 0);
    }

    private void BuildHorizontal()
    {
        Width = 680;
        Height = 340;

        ContentGrid.Margin = new Thickness(28, 0, 28, 24);
        ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        LogoImage.Width = 104;
        LogoImage.Height = 104;

        BrandPanel.HorizontalAlignment = HorizontalAlignment.Left;
        BrandPanel.VerticalAlignment = VerticalAlignment.Center;
        BrandPanel.Margin = new Thickness(0, 0, 0, 6);

        InfoPanel.Margin = new Thickness(0, 0, 0, 12);
        InfoPanel.VerticalAlignment = VerticalAlignment.Bottom;
        ActionRow.Margin = new Thickness(0, 10, 0, 0);

        Place(BrandPanel, 0, 0, rowSpan: 4);
        Place(InfoPanel, 0, 2);
        Place(LaunchButton, 1, 2);
        Place(ActionRow, 2, 2);
        Place(SupportButton, 3, 2);
    }

    private void Place(FrameworkElement child, int row, int column, int rowSpan = 1)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        Grid.SetRowSpan(child, rowSpan);

        ContentGrid.Children.Add(child);
    }
}
