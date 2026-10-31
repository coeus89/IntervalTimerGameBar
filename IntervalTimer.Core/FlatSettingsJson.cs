using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IntervalTimer.Core
{
    /// <summary>
    /// Reads and writes the flat primitive map <see cref="ISettingsStore"/> persists
    /// (int/long/double/bool/string). Integer values stay <see cref="long"/> so tick counts
    /// survive a round trip — a JSON number parsed as <see cref="double"/> only has 53 bits.
    /// </summary>
    public static class FlatSettingsJson
    {
        public static string Serialize(IDictionary<string, object> values)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var key in values.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (values[key] == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("\n  ");
                AppendString(sb, key);
                sb.Append(": ");
                AppendValue(sb, values[key]);
            }
            if (!first) sb.Append('\n');
            sb.Append('}');
            return sb.ToString();
        }

        public static Dictionary<string, object> Deserialize(string json)
        {
            if (json == null) throw new FormatException("Settings JSON is empty.");
            var reader = new Reader(json);
            var map = reader.ParseObject();
            reader.SkipWs();
            if (!reader.End) throw new FormatException("Unexpected trailing data in settings JSON.");
            return map;
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }
            if (value is string s)
            {
                AppendString(sb, s);
                return;
            }

            // Whole numbers (int/long, and anything else that converts cleanly) stay exact.
            long asLong;
            if (value is int || value is long || value is short || value is byte)
            {
                asLong = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                sb.Append(asLong.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is double || value is float || value is decimal)
            {
                var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            throw new InvalidOperationException("Unsupported settings value type: " + value.GetType().Name);
        }

        private static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) { _s = s; }

            public bool End => _i >= _s.Length;

            public Dictionary<string, object> ParseObject()
            {
                var map = new Dictionary<string, object>();
                SkipWs();
                Expect('{');
                SkipWs();
                if (Peek() == '}')
                {
                    _i++;
                    return map;
                }

                while (true)
                {
                    SkipWs();
                    var key = ParseString();
                    SkipWs();
                    Expect(':');
                    SkipWs();
                    var value = ParseValue();
                    if (value != null) map[key] = value;
                    SkipWs();
                    var c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _i++;
                        break;
                    }
                    throw new FormatException("Expected ',' or '}' in settings JSON.");
                }
                return map;
            }

            public void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private object ParseValue()
            {
                var c = Peek();
                if (c == '"') return ParseString();
                if (c == 't') return ExpectLiteral("true", true);
                if (c == 'f') return ExpectLiteral("false", false);
                if (c == 'n')
                {
                    ExpectLiteral("null", null);
                    return null;
                }
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                throw new FormatException("Unexpected value in settings JSON.");
            }

            private object ParseNumber()
            {
                var start = _i;
                if (Peek() == '-') _i++;
                if (!IsDigit(Peek())) throw new FormatException("Invalid number in settings JSON.");
                while (IsDigit(Peek())) _i++;

                var isFloat = false;
                if (Peek() == '.')
                {
                    isFloat = true;
                    _i++;
                    if (!IsDigit(Peek())) throw new FormatException("Invalid number in settings JSON.");
                    while (IsDigit(Peek())) _i++;
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    isFloat = true;
                    _i++;
                    if (Peek() == '+' || Peek() == '-') _i++;
                    if (!IsDigit(Peek())) throw new FormatException("Invalid number in settings JSON.");
                    while (IsDigit(Peek())) _i++;
                }

                var token = _s.Substring(start, _i - start);
                if (!isFloat && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                throw new FormatException("Invalid number in settings JSON.");
            }

            private string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_i >= _s.Length) break;
                    var esc = _s[_i++];
                    switch (esc)
                    {
                        case '"':
                        case '\\':
                        case '/': sb.Append(esc); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("Bad unicode escape in settings JSON.");
                            var hex = _s.Substring(_i, 4);
                            _i += 4;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw new FormatException("Bad unicode escape in settings JSON.");
                            sb.Append((char)code);
                            break;
                        default:
                            throw new FormatException("Bad escape in settings JSON.");
                    }
                }
                throw new FormatException("Unterminated string in settings JSON.");
            }

            private object ExpectLiteral(string literal, object value)
            {
                if (_i + literal.Length > _s.Length || string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw new FormatException("Unexpected token in settings JSON.");
                _i += literal.Length;
                return value;
            }

            private void Expect(char c)
            {
                if (Peek() != c) throw new FormatException("Expected '" + c + "' in settings JSON.");
                _i++;
            }

            private char Peek() => _i < _s.Length ? _s[_i] : '\0';

            private static bool IsDigit(char c) => c >= '0' && c <= '9';
        }
    }
}
