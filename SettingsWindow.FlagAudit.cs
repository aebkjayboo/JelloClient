using System.Windows;
using JelloClient.Roblox;

namespace JelloClient;

public partial class SettingsWindow
{
    /// Shows what Roblox did with the flags on the last launch.
    ///
    /// This build refuses most locally configured flags outright and says so only in its
    /// own log, so without this the editor would look like it was working while nothing it
    /// held had any effect. The banner is deliberately loud: a flag that was refused is not
    /// a flag that is set to the wrong value, it is a flag that does nothing at all.
    private void RefreshFlagAudit()
    {
        var audit = FlagAudit.Current;

        if (audit is null || audit.Refused.Count == 0)
        {
            FlagAuditBanner.Visibility = Visibility.Collapsed;
            return;
        }

        FlagAuditBanner.Visibility = Visibility.Visible;

        int mine = audit.Refused.Count(name => Settings.FastFlagValues.ContainsKey(name));

        FlagAuditHeadline.Text = mine == 0
            ? $"Roblox refused {audit.Refused.Count} flag(s) on the last launch."
            : $"Roblox refused {mine} of your flag(s) on the last launch. They are saved here, but they did nothing.";

        var named = audit.Refused
            .Where(name => Settings.FastFlagValues.ContainsKey(name))
            .Take(6)
            .ToList();

        string list = named.Count == 0
            ? string.Empty
            : string.Join(", ", named) + (mine > named.Count ? $" and {mine - named.Count} more" : string.Empty);

        string kept = audit.Accepted.Count > 0
            ? $" It did take {audit.Accepted.Count} other flag(s)."
            : string.Empty;

        FlagAuditDetail.Text =
            (list.Length > 0 ? list + ". " : string.Empty)
            + $"Roblox {audit.Version} only accepts some flags from ClientAppSettings.json and drops the rest "
            + "without any sign in Jello. Nothing here can change that."
            + kept;
    }
}
