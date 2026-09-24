using System.Text;

namespace JelloClient.UI.Controls;

internal enum JsonTokenKind
{
    Punctuation,
    Key,
    String,
    Number,
    Keyword,
    Error
}

internal readonly record struct JsonToken(int Start, int Length, JsonTokenKind Kind);

internal sealed record JsonScan(IReadOnlyList<JsonToken> Tokens, string? Error, int ErrorStart, int ErrorLength);

/// A single-pass JSON lexer used to colour an editor. It never throws: anything it
/// cannot classify becomes an Error token so the editor can mark it as you type.
internal static class JsonHighlighter
{
    public static JsonScan Scan(string text)
    {
        var tokens = new List<JsonToken>();

        string? error = null;
        int errorStart = 0;
        int errorLength = 0;

        int index = 0;
        int depth = 0;
        bool expectingKey = false;

        void Fail(string message, int start, int length)
        {
            if (error is null)
            {
                error = message;
                errorStart = start;
                errorLength = Math.Max(1, length);
            }
        }

        while (index < text.Length)
        {
            char c = text[index];

            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            switch (c)
            {
                case '{':
                    depth++;
                    expectingKey = true;
                    tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                    index++;
                    continue;

                case '}':
                    depth--;
                    if (depth < 0)
                    {
                        Fail("Unexpected '}'.", index, 1);
                    }
                    expectingKey = false;
                    tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                    index++;
                    continue;

                case '[':
                    depth++;
                    expectingKey = false;
                    tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                    index++;
                    continue;

                case ']':
                    depth--;
                    if (depth < 0)
                    {
                        Fail("Unexpected ']'.", index, 1);
                    }
                    tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                    index++;
                    continue;

                case ':':
                    expectingKey = false;
                    tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                    index++;
                    continue;

                case ',':
                    expectingKey = depth > 0;
                    tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                    index++;
                    continue;

                case '"':
                {
                    int start = index;
                    index++;

                    bool closed = false;

                    while (index < text.Length)
                    {
                        if (text[index] == '\\')
                        {
                            index += 2;
                            continue;
                        }

                        if (text[index] == '"')
                        {
                            index++;
                            closed = true;
                            break;
                        }

                        index++;
                    }

                    int length = Math.Min(index, text.Length) - start;

                    if (!closed)
                    {
                        Fail("Unterminated string.", start, length);
                        tokens.Add(new JsonToken(start, length, JsonTokenKind.Error));
                    }
                    else
                    {
                        tokens.Add(new JsonToken(start, length, expectingKey ? JsonTokenKind.Key : JsonTokenKind.String));
                    }

                    continue;
                }
            }

            if (c == '-' || char.IsAsciiDigit(c))
            {
                int start = index;

                while (index < text.Length && (char.IsAsciiDigit(text[index]) || "+-.eE".Contains(text[index])))
                {
                    index++;
                }

                string raw = text[start..index];

                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    Fail($"'{raw}' is not a valid number.", start, raw.Length);
                    tokens.Add(new JsonToken(start, raw.Length, JsonTokenKind.Error));
                }
                else
                {
                    tokens.Add(new JsonToken(start, raw.Length, JsonTokenKind.Number));
                }

                continue;
            }

            if (char.IsLetter(c))
            {
                int start = index;

                while (index < text.Length && char.IsLetter(text[index]))
                {
                    index++;
                }

                string word = text[start..index];

                if (word is "true" or "false" or "null")
                {
                    tokens.Add(new JsonToken(start, word.Length, JsonTokenKind.Keyword));
                }
                else
                {
                    Fail($"'{word}' is not valid here. Strings must be quoted.", start, word.Length);
                    tokens.Add(new JsonToken(start, word.Length, JsonTokenKind.Error));
                }

                continue;
            }

            Fail($"Unexpected character '{c}'.", index, 1);
            tokens.Add(new JsonToken(index, 1, JsonTokenKind.Error));
            index++;
        }

        if (error is null && depth > 0)
        {
            error = depth == 1 ? "Missing a closing brace." : $"{depth} blocks are left open.";
            errorStart = Math.Max(0, text.Length - 1);
            errorLength = 1;
        }

        return new JsonScan(tokens, error, errorStart, errorLength);
    }

    public static string Describe(string text)
    {
        var scan = Scan(text);

        if (scan.Error is not null)
        {
            int line = 1;

            for (int i = 0; i < Math.Min(scan.ErrorStart, text.Length); i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return $"Line {line}: {scan.Error}";
        }

        return "";
    }

    public static string Pretty(string text)
    {
        var builder = new StringBuilder();
        var scan = Scan(text);

        if (scan.Error is not null)
        {
            return text;
        }

        int indent = 0;
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                builder.Append(c);

                if (c == '\\' && i + 1 < text.Length)
                {
                    builder.Append(text[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    builder.Append(c);
                    break;

                case '{' or '[':
                    indent++;
                    builder.Append(c).Append('\n').Append(' ', indent * 2);
                    break;

                case '}' or ']':
                    indent = Math.Max(0, indent - 1);
                    builder.Append('\n').Append(' ', indent * 2).Append(c);
                    break;

                case ',':
                    builder.Append(c).Append('\n').Append(' ', indent * 2);
                    break;

                case ':':
                    builder.Append(": ");
                    break;

                default:
                    if (!char.IsWhiteSpace(c))
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
