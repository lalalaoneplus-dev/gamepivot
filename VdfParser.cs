using System.Text;

namespace GamePivot;

internal sealed class VdfObject : Dictionary<string, object>
{
    public VdfObject() : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    public string GetString(string key, string fallback = "")
        => TryGetValue(key, out var value) && value is string text ? text : fallback;

    public VdfObject? GetObject(string key)
        => TryGetValue(key, out var value) ? value as VdfObject : null;
}

internal static class VdfParser
{
    public static VdfObject Parse(string text)
    {
        var tokenizer = new Tokenizer(text);
        return ParseObject(tokenizer, false);
    }

    private static VdfObject ParseObject(Tokenizer tokenizer, bool stopAtBrace)
    {
        var result = new VdfObject();

        while (tokenizer.TryRead(out var key))
        {
            if (key == "}")
            {
                if (!stopAtBrace)
                {
                    throw new FormatException("Unexpected closing brace in VDF.");
                }

                return result;
            }

            if (!tokenizer.TryRead(out var value))
            {
                throw new FormatException($"Missing value for VDF key '{key}'.");
            }

            result[key] = value == "{"
                ? ParseObject(tokenizer, true)
                : value;
        }

        if (stopAtBrace)
        {
            throw new FormatException("Unclosed VDF object.");
        }

        return result;
    }

    private sealed class Tokenizer(string text)
    {
        private int _position;

        public bool TryRead(out string token)
        {
            SkipTrivia();
            if (_position >= text.Length)
            {
                token = string.Empty;
                return false;
            }

            var current = text[_position];
            if (current is '{' or '}')
            {
                _position++;
                token = current.ToString();
                return true;
            }

            if (current == '"')
            {
                token = ReadQuoted();
                return true;
            }

            var start = _position;
            while (_position < text.Length &&
                   !char.IsWhiteSpace(text[_position]) &&
                   text[_position] is not '{' and not '}')
            {
                _position++;
            }

            token = text[start.._position];
            return token.Length > 0;
        }

        private string ReadQuoted()
        {
            _position++;
            var builder = new StringBuilder();

            while (_position < text.Length)
            {
                var current = text[_position++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current == '\\' && _position < text.Length)
                {
                    var next = text[_position++];
                    if (next is '\\' or '"')
                    {
                        builder.Append(next);
                    }
                    else
                    {
                        builder.Append('\\');
                        builder.Append(next);
                    }

                    continue;
                }

                builder.Append(current);
            }

            throw new FormatException("Unclosed quoted string in VDF.");
        }

        private void SkipTrivia()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]))
                {
                    _position++;
                    continue;
                }

                if (text[_position] == '/' &&
                    _position + 1 < text.Length &&
                    text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < text.Length && text[_position] is not '\r' and not '\n')
                    {
                        _position++;
                    }

                    continue;
                }

                break;
            }
        }
    }
}
