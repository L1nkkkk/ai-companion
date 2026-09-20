using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AICompanion.Preview.Transport
{
    // No dependency on Unity JsonUtility: duplicate keys and unknown properties must be rejected.
    internal static class StrictJson
    {
        internal static Dictionary<string, object> Parse(string source)
        {
            var reader = new Reader(source);
            var value = reader.Value(0) as Dictionary<string, object>;
            reader.Space();
            if (value == null || !reader.End) throw new InvalidDataException("Invalid JSON object.");
            return value;
        }
        internal static string Encode(object value)
        {
            if (value == null) return "null";
            if (value is string s)
            {
                var text = new StringBuilder("\"");
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': text.Append("\\\""); break;
                        case '\\': text.Append("\\\\"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        case '\t': text.Append("\\t"); break;
                        default: if (c < 32) text.Append("\\u" + ((int)c).ToString("x4")); else text.Append(c); break;
                    }
                }
                return text.Append('"').ToString();
            }
            if (value is bool b) return b ? "true" : "false";
            if (value is IDictionary<string, object> map)
            {
                var parts = new List<string>();
                foreach (var item in map) parts.Add(Encode(item.Key) + ":" + Encode(item.Value));
                return "{" + string.Join(",", parts) + "}";
            }
            if (value is System.Collections.IEnumerable list)
            {
                var parts = new List<string>();
                foreach (var item in list) parts.Add(Encode(item));
                return "[" + string.Join(",", parts) + "]";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _p;
            internal Reader(string text) { _text = text; }
            internal bool End => _p == _text.Length;
            internal void Space() { while (_p < _text.Length && (_text[_p] == ' ' || _text[_p] == '\r' || _text[_p] == '\n' || _text[_p] == '\t')) _p++; }
            private char Take() { if (End) throw Bad(); return _text[_p++]; }
            private void Expect(char c) { Space(); if (Take() != c) throw Bad(); }
            internal object Value(int depth)
            {
                if (depth > 12) throw Bad();
                Space(); if (End) throw Bad();
                char c = _text[_p];
                if (c == '"') return String();
                if (c == '{')
                {
                    _p++; Space(); var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (!End && _text[_p] == '}') { _p++; return map; }
                    while (true)
                    {
                        Space(); string key = String(); Expect(':');
                        if (map.ContainsKey(key) || map.Count >= 128) throw Bad();
                        map.Add(key, Value(depth + 1)); Space(); c = Take();
                        if (c == '}') return map; if (c != ',') throw Bad();
                    }
                }
                if (c == '[')
                {
                    _p++; Space(); var list = new List<object>();
                    if (!End && _text[_p] == ']') { _p++; return list; }
                    while (true)
                    {
                        if (list.Count >= 128) throw Bad();
                        list.Add(Value(depth + 1)); Space(); c = Take();
                        if (c == ']') return list; if (c != ',') throw Bad();
                    }
                }
                foreach (string word in new[] { "true", "false", "null" })
                    if (_p + word.Length <= _text.Length && string.CompareOrdinal(_text, _p, word, 0, word.Length) == 0)
                    { _p += word.Length; return word == "null" ? null : (object)(word == "true"); }
                int start = _p;
                if (_text[_p] == '-') _p++;
                if (End) throw Bad();
                if (_text[_p] == '0') _p++;
                else
                {
                    if (_text[_p] < '1' || _text[_p] > '9') throw Bad();
                    while (!End && _text[_p] >= '0' && _text[_p] <= '9') _p++;
                }
                // All protocol numeric fields are integers; fractions/exponents cannot normalize to ints.
                if (!long.TryParse(_text.Substring(start, _p - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) throw Bad();
                return n;
            }
            private string String()
            {
                if (Take() != '"') throw Bad();
                var result = new StringBuilder();
                while (true)
                {
                    char c = Take();
                    if (c == '"')
                    {
                        string value = result.ToString();
                        for (int i = 0; i < value.Length; i++)
                            if (char.IsSurrogate(value[i]))
                            { if (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i])) throw Bad(); }
                        return value;
                    }
                    if (c < 32) throw Bad();
                    if (c != '\\') { result.Append(c); continue; }
                    switch (Take())
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
                            if (_p + 4 > _text.Length || !ushort.TryParse(_text.Substring(_p, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw Bad();
                            _p += 4; result.Append((char)code); break;
                        default: throw Bad();
                    }
                }
            }
            private static InvalidDataException Bad() => new InvalidDataException("Malformed preview JSON.");
        }
    }
}
