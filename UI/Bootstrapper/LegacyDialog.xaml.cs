using System.Windows;
using System.Windows.Controls;

namespace JelloClient.UI.Bootstrapper;

public partial class LegacyDialog : BootstrapperWindow
{
    public LegacyDialog()
    {
        InitializeComponent();
    }

    protected override TextBlock MessageTarget => MessageText;

    protected override ProgressBar ProgressTarget => Progress;

    protected override UIElement? CancelTarget => null;
}
