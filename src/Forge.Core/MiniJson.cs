using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CsmForge.Core
{
    public enum JsonKind { Null, Boolean, Number, String, Array, Object }

    /// <summary>
    /// WP-3.2: immutable parsed JSON node. Objects preserve key order and expose a parallel
    /// key/value list (net35 has no read-only dictionary interfaces); numbers keep their long
    /// value when they parse as integers so IDs survive a round trip without precision loss.
    /// </summary>
    public sealed class JsonNode
    {
        private Dictionary<string, JsonNode> members;

        public JsonKind Kind { get; internal set; }
        public bool Boolean { get; internal set; }
        public long Integer { get; internal set; }
        public double Number { get; internal set; }
        public bool HasInteger { get; internal set; }
        public string Text { get; internal set; }
        public IList<JsonNode> Items { get; internal set; }
        public IList<string> Keys { get; internal set; }
        public IList<JsonNode> Values { get; internal set; }

        internal JsonNode(JsonKind kind) { Kind = kind; }

        public bool TryGet(string key, out JsonNode value)
        {
            value = null;
            if (Kind != JsonKind.Object || key == null || members == null) return false;
            return members.TryGetValue(key, out value);
        }

        internal void AttachObject(List<string> keys, List<JsonNode> values, Dictionary<string, JsonNode> byKey)
        {
            Keys = keys; Values = values; members = byKey;
        }
    }

    /// <summary>
    /// WP-3.2: strict, bounded JSON parser (RFC 8259 subset; no comments, no trailing commas).
    /// Fail-closed by construction: depth and length limits are checked before allocation and
    /// every malformed input throws JsonParseException with the offending offset.
    /// </summary>
    public static class MiniJson
    {
        public const int DefaultMaximumLength = 262144;
        public const int DefaultMaximumDepth = 32;

        public static JsonNode Parse(string text)
        {
            return Parse(text, DefaultMaximumLength, DefaultMaximumDepth);
        }

        public static JsonNode Parse(string text, int maximumLength, int maximumDepth)
        {
            if (text == null) throw new ArgumentNullException("text");
            Check.OutOfRange(maximumLength < 1, "maximumLength");
            Check.OutOfRange(maximumDepth < 1, "maximumDepth");
            if (text.Length > maximumLength) throw new JsonParseException(0, "JSON text exceeds its length limit.");
            Parser parser = new Parser(text, maximumDepth);
            parser.SkipWhitespace();
            JsonNode value = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("Trailing characters after JSON value.");
            return value;
        }

        private sealed class Parser
        {
            private readonly string text;
            private readonly int maximumDepth;
            private int position;

            public Parser(string text, int maximumDepth) { this.text = text; this.maximumDepth = maximumDepth; }

            public bool AtEnd { get { return position >= text.Length; } }

            public JsonParseException Error(string message) { return new JsonParseException(position, message); }

            public void SkipWhitespace()
            {
                while (position < text.Length)
                {
                    char ch = text[position];
                    if (ch != ' ' && ch != '\t' && ch != '\r' && ch != '\n') break;
                    position++;
                }
            }

            public JsonNode ParseValue(int depth)
            {
                if (depth > maximumDepth) throw Error("JSON nesting depth exceeds its limit.");
                if (AtEnd) throw Error("Unexpected end of JSON text.");
                char ch = text[position];
                switch (ch)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return new JsonNode(JsonKind.String) { Text = ParseString() };
                    case 't': ExpectLiteral("true"); return new JsonNode(JsonKind.Boolean) { Boolean = true };
                    case 'f': ExpectLiteral("false"); return new JsonNode(JsonKind.Boolean) { Boolean = false };
                    case 'n': ExpectLiteral("null"); return new JsonNode(JsonKind.Null);
                    default: return ParseNumber();
                }
            }

            private void ExpectLiteral(string literal)
            {
                if (position + literal.Length > text.Length ||
                    StringComparer.Ordinal.Compare(text.Substring(position, literal.Length), literal) != 0)
                    throw Error("Invalid JSON literal.");
                position += literal.Length;
            }

            private JsonNode ParseObject(int depth)
            {
                position++; // {
                JsonNode result = new JsonNode(JsonKind.Object);
                List<string> keys = new List<string>();
                List<JsonNode> values = new List<JsonNode>();
                Dictionary<string, JsonNode> byKey = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && text[position] == '}') { position++; result.AttachObject(keys, values, byKey); return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[position] != '"') throw Error("Expected object key.");
                    string key = ParseString();
                    if (byKey.ContainsKey(key)) throw Error("Duplicate object key.");
                    SkipWhitespace();
                    if (AtEnd || text[position] != ':') throw Error("Expected ':' after object key.");
                    position++;
                    SkipWhitespace();
                    JsonNode value = ParseValue(depth + 1);
                    keys.Add(key); values.Add(value); byKey.Add(key, value);
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON object.");
                    if (text[position] == ',') { position++; continue; }
                    if (text[position] == '}') { position++; break; }
                    throw Error("Expected ',' or '}' in JSON object.");
                }
                result.AttachObject(keys, values, byKey);
                return result;
            }

            private JsonNode ParseArray(int depth)
            {
                position++; // [
                JsonNode result = new JsonNode(JsonKind.Array) { Items = new List<JsonNode>() };
                SkipWhitespace();
                if (!AtEnd && text[position] == ']') { position++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    result.Items.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON array.");
                    if (text[position] == ',') { position++; continue; }
                    if (text[position] == ']') { position++; break; }
                    throw Error("Expected ',' or ']' in JSON array.");
                }
                return result;
            }

            private string ParseString()
            {
                position++; // opening quote
                System.Text.StringBuilder result = new System.Text.StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated JSON string.");
                    char ch = text[position++];
                    if (ch == '"') return result.ToString();
                    if (ch == '\\')
                    {
                        if (AtEnd) throw Error("Unterminated JSON escape.");
                        char escape = text[position++];
                        switch (escape)
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
                                if (position + 4 > text.Length) throw Error("Incomplete unicode escape.");
                                int code = ParseHex4(position);
                                position += 4;
                                result.Append((char)code);
                                break;
                            default: throw Error("Unknown JSON escape.");
                        }
                        continue;
                    }
                    if (ch < 0x20) throw Error("Raw control character in JSON string.");
                    result.Append(ch);
                }
            }

            private int ParseHex4(int start)
            {
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char ch = text[start + i];
                    int digit;
                    if (ch >= '0' && ch <= '9') digit = ch - '0';
                    else if (ch >= 'a' && ch <= 'f') digit = ch - 'a' + 10;
                    else if (ch >= 'A' && ch <= 'F') digit = ch - 'A' + 10;
                    else throw Error("Invalid unicode escape digit.");
                    value = value * 16 + digit;
                }
                return value;
            }

            private JsonNode ParseNumber()
            {
                int start = position;
                if (!AtEnd && text[position] == '-') position++;
                if (AtEnd || text[position] < '0' || text[position] > '9') throw Error("Invalid JSON number.");
                bool integer = true;
                while (!AtEnd)
                {
                    char ch = text[position];
                    if (ch >= '0' && ch <= '9') { position++; continue; }
                    if (ch == '.' || ch == 'e' || ch == 'E' || ch == '+' || ch == '-') { integer = false; position++; continue; }
                    break;
                }
                string token = text.Substring(start, position - start);
                JsonNode node = new JsonNode(JsonKind.Number);
                if (integer)
                {
                    long parsedInteger;
                    if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInteger))
                        throw Error("Invalid JSON integer.");
                    node.HasInteger = true; node.Integer = parsedInteger; node.Number = parsedInteger;
                    return node;
                }
                double parsed;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    throw Error("Invalid JSON number.");
                node.Number = parsed;
                return node;
            }
        }
    }

    /// <summary>Carries the offset of the offending character for diagnostics.</summary>
    public class JsonParseException : Exception
    {
        public int Offset { get; private set; }
        public JsonParseException(int offset, string message)
            : base(message + " (offset " + offset.ToString(CultureInfo.InvariantCulture) + ")")
        { Offset = offset; }
    }
}
