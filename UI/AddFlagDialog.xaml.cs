using System.Text;
using System.Text.Json;
using System.Windows;

using JelloClient.Roblox;
using JelloClient.Services;

namespace JelloClient.UI;

public partial class AddFlagDialog : JelloWindow
{
    private enum InputMode
    {
        Single,
        Json,
        Base64
    }

    private InputMode _mode = InputMode.Single;

    public Dictionary<string, string> Flags { get; private set; } = new();

    public AddFlagDialog()
    {
        InitializeComponent();
    }

    protected override FrameworkElement EffectsRoot => Root;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FlagNameBox.Focus();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        _mode = ReferenceEquals(sender, JsonTabButton) ? InputMode.Json
            : ReferenceEquals(sender, Base64TabButton) ? InputMode.Base64
            : InputMode.Single;

        SingleTabButton.IsChecked = _mode == InputMode.Single;
        JsonTabButton.IsChecked = _mode == InputMode.Json;
        Base64TabButton.IsChecked = _mode == InputMode.Base64;

        SinglePanel.Visibility = _mode == InputMode.Single ? Visibility.Visible : Visibility.Collapsed;
        JsonPanel.Visibility = _mode == InputMode.Json ? Visibility.Visible : Visibility.Collapsed;
        Base64Panel.Visibility = _mode == InputMode.Base64 ? Visibility.Visible : Visibility.Collapsed;

        Validate();
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Validate();

    private void Validate()
    {
        var parsed = Parse(out string message);

        AddButton.IsEnabled = parsed is not null && parsed.Count > 0;

        ValidationText.Text = message;
        ValidationText.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Dictionary<string, string>? Parse(out string message)
    {
        message = "";

        switch (_mode)
        {
            case InputMode.Single:
                string name = FlagNameBox.Text.Trim();

                if (name.Length == 0)
                {
                    return null;
                }

                string value = FlagValueBox.Text.Trim();
                var kind = FastFlagTypes.KindOf(name);

                if (kind == FlagValueKind.Boolean
                    && !string.Equals(value, "True", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "False", StringComparison.OrdinalIgnoreCase))
                {
                    message = $"{name} is a boolean flag, so the value should be True or False.";
                }
                else if (kind == FlagValueKind.Integer && !long.TryParse(value, out _))
                {
                    message = $"{name} is a numeric flag, so the value should be a whole number.";
                }

                return new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value };

            case InputMode.Json:
                return ParseJson(JsonBox.Text, out message);

            default:
                string encoded = Base64Box.Text.Trim();

                if (encoded.Length == 0)
                {
                    return null;
                }

                try
                {
                    string json = Encoding.UTF8.GetString(System.Convert.FromBase64String(encoded));
                    return ParseJson(json, out message);
                }
                catch (FormatException)
                {
                    message = "That is not valid base64.";
                    return null;
                }
        }
    }

    private static Dictionary<string, string>? ParseJson(string text, out string message)
    {
        message = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text);

            if (parsed is null || parsed.Count == 0)
            {
                message = "That object has no flags in it.";
                return null;
            }

            var flags = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in parsed)
            {
                flags[pair.Key] = pair.Value.ValueKind == JsonValueKind.String
                    ? pair.Value.GetString() ?? ""
                    : pair.Value.ToString();
            }

            message = $"{flags.Count} flag(s) ready to add.";

            return flags;
        }
        catch (JsonException ex)
        {
            message = $"Invalid JSON: {ex.Message}";
            return null;
        }
    }

    private void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a flag file",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        try
        {
            JsonBox.Text = File.ReadAllText(picker.FileName);
        }
        catch (Exception ex)
        {
            Log.WriteException("AddFlagDialog::ImportFile", ex);
            ValidationText.Text = $"Could not read that file: {ex.Message}";
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var parsed = Parse(out _);

        if (parsed is null || parsed.Count == 0)
        {
            return;
        }

        Flags = parsed;
        DialogResult = true;

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
