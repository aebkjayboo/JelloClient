using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient;

public partial class SettingsWindow
{
    private static readonly int[] MatchmakerBudgets = { 3, 5, 8, 12, 20 };

    private static readonly string[] FillLabels = { "Prefer fuller", "Prefer emptier" };

    private void BuildMatchmakerLists()
    {
        foreach (int budget in MatchmakerBudgets)
        {
            MatchmakerBudgetCombo.Items.Add(new ComboBoxItem { Content = $"{budget} servers" });
        }

        foreach (string label in FillLabels)
        {
            MatchmakerFillCombo.Items.Add(new ComboBoxItem { Content = label });
        }
    }

    private DispatcherTimer? _matchmakerStatus;

    private void LoadMatchmakerState()
    {
        MatchmakerToggle.IsChecked = Settings.MatchmakerEnabled;

        int budget = Array.IndexOf(MatchmakerBudgets, Settings.MatchmakerBudget);
        MatchmakerBudgetCombo.SelectedIndex = budget < 0 ? 1 : budget;

        MatchmakerFillCombo.SelectedIndex = Settings.MatchmakerPreferEmptier ? 1 : 0;

        RefreshMatchmakerState();

        // The matchmaker runs on its own at launch, and Roblox's refusal window ticks
        // down on its own too, so the card keeps itself current rather than making
        // anyone press anything to find out where it stands.
        _matchmakerStatus ??= new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            (_, _) => RefreshMatchmakerState(),
            Dispatcher);

        _matchmakerStatus.Start();

        Closed += (_, _) => _matchmakerStatus?.Stop();
    }

    private void RefreshMatchmakerState()
    {
        MatchmakerText.Text = Matchmaker.Describe();

        MatchmakerBudgetText.Text =
            $"Each one costs a lookup, and Roblox stops answering after a few. "
          + $"{Settings.MatchmakerBudget} adds about {Settings.MatchmakerBudget * 2} seconds to a launch.";
    }

    private void Matchmaker_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        bool wanted = MatchmakerToggle.IsChecked == true;

        // Turning it on is the moment the person is agreeing to Jello reading their login,
        // so that is where it is said plainly rather than only in the blurb above.
        if (wanted && !Settings.MatchmakerEnabled)
        {
            var answer = MessageBox.Show(
                "To look a server up, Jello has to send your Roblox login with the request, "
                + "the same way the website does when you click a server.\n\n"
                + "It is read from the Roblox cookie store on this machine when a launch needs it, "
                + "sent only to roblox.com over an encrypted connection, and never written to disk "
                + "or shown anywhere in Jello.\n\n"
                + "Roblox also treats each lookup as a join request and refuses them after a few, "
                + "so Jello checks a small number and then launches normally.\n\n"
                + "Turn server matchmaking on?",
                "Server matchmaking",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                _suppressEvents = true;
                MatchmakerToggle.IsChecked = false;
                _suppressEvents = false;

                SetStatus("Left off.");
                return;
            }
        }

        Settings.MatchmakerEnabled = wanted;
        Persist();

        if (!wanted)
        {
            RobloxSession.Forget();
        }

        RefreshMatchmakerState();

        SetStatus(wanted
            ? "Jello will check a few servers before each launch and join the fastest."
            : "Roblox will pick the server, as it normally does.");
    }

    private void MatchmakerBudget_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || MatchmakerBudgetCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.MatchmakerBudget = MatchmakerBudgets[MatchmakerBudgetCombo.SelectedIndex];
        Persist();

        RefreshMatchmakerState();
    }

    private void MatchmakerFill_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || MatchmakerFillCombo.SelectedIndex < 0)
        {
            return;
        }

        Settings.MatchmakerPreferEmptier = MatchmakerFillCombo.SelectedIndex == 1;
        Persist();

        SetStatus(Settings.MatchmakerPreferEmptier
            ? "Emptier servers will be checked first."
            : "Fuller servers will be checked first.");
    }

}
