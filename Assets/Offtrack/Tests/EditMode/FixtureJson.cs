using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Offtrack.Tests.EditMode
{
    // Fixtures contain nested [beat,lane] arrays, which JsonUtility cannot represent.
    // This small reader is test-only and deliberately accepts the standard JSON types.
    internal sealed class FixtureJson
    {
        private readonly string text;
        private int cursor;

        private FixtureJson(string text) { this.text = text; }

        public static object Parse(string text)
        {
            var reader = new FixtureJson(text);
            var result = reader.ReadValue();
            reader.SkipWhitespace();
            if (reader.cursor != text.Length) throw new FormatException("Trailing JSON data.");
            return result;
        }

        private object ReadValue()
        {
            SkipWhitespace();
            if (cursor >= text.Length) throw new FormatException("Unexpected end of JSON.");
            switch (text[cursor])
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': ReadLiteral("true"); return true;
                case 'f': ReadLiteral("false"); return false;
                case 'n': ReadLiteral("null"); return null;
                default:
                    int start = cursor;
                    while (cursor < text.Length && "-+0123456789.eE".IndexOf(text[cursor]) >= 0) cursor++;
                    if (start == cursor) throw new FormatException("Expected a JSON value.");
                    return double.Parse(text.Substring(start, cursor - start), CultureInfo.InvariantCulture);
            }
        }

        private Dictionary<string, object> ReadObject()
        {
            cursor++;
            var result = new Dictionary<string, object>();
            if (Consume('}')) return result;
            do
            {
                SkipWhitespace();
                var key = ReadString();
                Expect(':');
                result.Add(key, ReadValue());
                if (Consume('}')) return result;
                Expect(',');
            } while (true);
        }

        private List<object> ReadArray()
        {
            cursor++;
            var result = new List<object>();
            if (Consume(']')) return result;
            do
            {
                result.Add(ReadValue());
                if (Consume(']')) return result;
                Expect(',');
            } while (true);
        }

        private string ReadString()
        {
            Expect('"');
            var result = new StringBuilder();
            while (cursor < text.Length)
            {
                char current = text[cursor++];
                if (current == '"') return result.ToString();
                if (current != '\\') { result.Append(current); continue; }
                if (cursor >= text.Length) throw new FormatException("Incomplete JSON escape.");
                switch (text[cursor++])
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (cursor + 4 > text.Length) throw new FormatException("Incomplete unicode escape.");
                        result.Append((char)int.Parse(text.Substring(cursor, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        cursor += 4;
                        break;
                    default: throw new FormatException("Invalid JSON escape.");
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private void ReadLiteral(string literal)
        {
            if (cursor + literal.Length > text.Length || text.Substring(cursor, literal.Length) != literal)
                throw new FormatException("Invalid JSON literal.");
            cursor += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
        }

        private bool Consume(char value)
        {
            SkipWhitespace();
            if (cursor >= text.Length || text[cursor] != value) return false;
            cursor++;
            return true;
        }

        private void Expect(char value)
        {
            if (!Consume(value)) throw new FormatException("Expected '" + value + "'.");
        }
    }
}
