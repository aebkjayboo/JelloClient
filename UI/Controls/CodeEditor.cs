using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace JelloClient.UI.Controls;

/// A RichTextBox that colours its own content. The highlighter is pluggable, so any
/// future editor gets the same behaviour by pointing Language at another scanner.
public class CodeEditor : RichTextBox
{
    public static readonly DependencyProperty ErrorTextProperty =
        DependencyProperty.Register(nameof(ErrorText), typeof(string), typeof(CodeEditor),
            new PropertyMetadata(""));

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private bool _updating;

    public CodeEditor()
    {
        AcceptsReturn = true;
        AcceptsTab = true;
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
        FontSize = 12.5;
        // soft wrap: no PageWidth means the document reflows, so no horizontal bar is needed
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        Document.LineHeight = 17;
        Document.PagePadding = new Thickness(0);

        TextChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        };

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Highlight();
        };
    }

    public string ErrorText
    {
        get => (string)GetValue(ErrorTextProperty);
        private set => SetValue(ErrorTextProperty, value);
    }

    public string Text
    {
        get => new TextRange(Document.ContentStart, Document.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n');
        set
        {
            _updating = true;

            Document.Blocks.Clear();
            Document.Blocks.Add(new Paragraph(new Run(value)) { Margin = new Thickness(0) });

            _updating = false;

            Highlight();
        }
    }

    private static Brush Themed(string key, byte red, byte green, byte blue) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush
        ?? new SolidColorBrush(Color.FromRgb(red, green, blue));

    private static Brush Colour(JsonTokenKind kind) => kind switch
    {
        JsonTokenKind.Key => Themed("CodeKey", 0x7C, 0xD0, 0xFF),
        JsonTokenKind.String => Themed("CodeString", 0xB5, 0xE8, 0x9A),
        JsonTokenKind.Number => Themed("CodeNumber", 0xF5, 0xC2, 0x7A),
        JsonTokenKind.Keyword => Themed("CodeKeyword", 0xD3, 0xA0, 0xFF),
        JsonTokenKind.Error => Themed("CodeError", 0xFF, 0x8A, 0x80),
        _ => Themed("CodeText", 0x9A, 0x9A, 0x9A)
    };

    public void Highlight()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;

        try
        {
            string text = Text;
            var scan = JsonHighlighter.Scan(text);

            ErrorText = JsonHighlighter.Describe(text);

            int caret = new TextRange(Document.ContentStart, CaretPosition).Text.Replace("\r\n", "\n").Length;

            var paragraph = new Paragraph { Margin = new Thickness(0) };

            int cursor = 0;

            foreach (var token in scan.Tokens)
            {
                if (token.Start > cursor)
                {
                    paragraph.Inlines.Add(new Run(text[cursor..token.Start]));
                }

                int end = Math.Min(token.Start + token.Length, text.Length);

                if (end > token.Start)
                {
                    var run = new Run(text[token.Start..end]) { Foreground = Colour(token.Kind) };

                    if (token.Kind == JsonTokenKind.Error)
                    {
                        run.TextDecorations = TextDecorations.Underline;
                    }

                    paragraph.Inlines.Add(run);
                }

                cursor = end;
            }

            if (cursor < text.Length)
            {
                paragraph.Inlines.Add(new Run(text[cursor..]));
            }

            Document.Blocks.Clear();
            Document.Blocks.Add(paragraph);

            RestoreCaret(caret);
        }
        finally
        {
            _updating = false;
        }
    }

    private void RestoreCaret(int offset)
    {
        var pointer = Document.ContentStart;
        int seen = 0;

        while (pointer is not null)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int length = pointer.GetTextRunLength(LogicalDirection.Forward);

                if (seen + length >= offset)
                {
                    CaretPosition = pointer.GetPositionAtOffset(offset - seen) ?? Document.ContentEnd;
                    return;
                }

                seen += length;
            }

            var next = pointer.GetNextContextPosition(LogicalDirection.Forward);

            if (next is null)
            {
                break;
            }

            pointer = next;
        }

        CaretPosition = Document.ContentEnd;
    }
}
