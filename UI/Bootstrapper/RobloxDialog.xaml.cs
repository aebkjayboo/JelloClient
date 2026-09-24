using System.Windows;
using System.Windows.Controls;

namespace JelloClient.UI.Bootstrapper;

public partial class RobloxDialog : BootstrapperWindow
{
    public RobloxDialog()
    {
        InitializeComponent();
    }

    protected override TextBlock MessageTarget => MessageText;

    protected override ProgressBar ProgressTarget => Progress;

    protected override UIElement? CancelTarget => CancelButton;

    private void Cancel_Click(object sender, RoutedEventArgs e) => RaiseCancelled();
}
