using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// JSON serialization and parsing. A generic ObjektRT stdlib module.
///
/// Values map to the language's object-handle model: JSON objects become
/// <c>Dict</c> handles, JSON arrays become <c>List</c> handles, and scalars
/// become string / int / double / bool / null. Round-trips through
/// <see cref="Serialize"/> and <see cref="Parse"/>.
/// </summary>
[ClassBinding("Json")]
public static class Json
{
    /// <summary>Serializes a value (Dict/List handle or scalar) to a JSON string.</summary>
    public static string Serialize(object value) => WriteValue(value);

    /// <summary>Parses a JSON string into a Dict/List handle or scalar value.</summary>
    public static object Parse(string json)
    {
        int pos = 0;
        var result = ReadValue(json, ref pos);
        return result ?? "";
    }

    // ── Serialization ───────────────────────────────────────────────

    private static string WriteValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return Quote(s);
            case bool b:
                return b ? "true" : "false";
            case int i:
                return i.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            case long l:
                return l.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            case double d:
                return d.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            case float f:
                return f.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            case global::System.Collections.Generic.Dictionary<object, object> dict:
            {
                var parts = new global::System.Collections.Generic.List<string>();
                foreach (var kv in dict)
                    parts.Add($"{Quote(kv.Key?.ToString() ?? "")}:{WriteValue(kv.Value)}");
                return "{" + string.Join(",", parts) + "}";
            }
            case global::System.Collections.Generic.List<object> list:
            {
                var parts = new global::System.Collections.Generic.List<string>();
                foreach (var item in list)
                    parts.Add(WriteValue(item));
                return "[" + string.Join(",", parts) + "]";
            }
            default:
                return Quote(value.ToString() ?? "");
        }
    }

    private static string Quote(string s)
    {
        var sb = new global::System.Text.StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ── Parsing ─────────────────────────────────────────────────────

    private static object? ReadValue(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length) return null;
        char c = json[pos];
        switch (c)
        {
            case '{': return ReadObject(json, ref pos);
            case '[': return ReadArray(json, ref pos);
            case '"': return ReadString(json, ref pos);
            case 't': Expect(json, ref pos, "true"); return true;
            case 'f': Expect(json, ref pos, "false"); return false;
            case 'n': Expect(json, ref pos, "null"); return null;
            default: return ReadNumber(json, ref pos);
        }
    }

    private static object ReadObject(string json, ref int pos)
    {
        pos++; // '{'
        var dict = new global::System.Collections.Generic.Dictionary<object, object>();
        SkipWhitespace(json, ref pos);
        if (pos < json.Length && json[pos] == '}') { pos++; return dict; }
        while (pos < json.Length)
        {
            SkipWhitespace(json, ref pos);
            string key = ReadString(json, ref pos);
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ':') pos++;
            var value = ReadValue(json, ref pos);
            dict[key] = value ?? "";
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',') { pos++; continue; }
            if (pos < json.Length && json[pos] == '}') { pos++; break; }
        }
        return dict;
    }

    private static object ReadArray(string json, ref int pos)
    {
        pos++; // '['
        var list = new global::System.Collections.Generic.List<object>();
        SkipWhitespace(json, ref pos);
        if (pos < json.Length && json[pos] == ']') { pos++; return list; }
        while (pos < json.Length)
        {
            var value = ReadValue(json, ref pos);
            list.Add(value ?? "");
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',') { pos++; continue; }
            if (pos < json.Length && json[pos] == ']') { pos++; break; }
        }
        return list;
    }

    private static string ReadString(string json, ref int pos)
    {
        pos++; // '"'
        var sb = new global::System.Text.StringBuilder();
        while (pos < json.Length)
        {
            char c = json[pos++];
            if (c == '"') break;
            if (c == '\\' && pos < json.Length)
            {
                char e = json[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object ReadNumber(string json, ref int pos)
    {
        int start = pos;
        while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] is '-' or '+' or '.' or 'e' or 'E'))
            pos++;
        var text = json.Substring(start, pos - start);
        if (int.TryParse(text, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var i))
            return i;
        if (double.TryParse(text, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return 0;
    }

    private static void Expect(string json, ref int pos, string word)
    {
        if (pos + word.Length <= json.Length && json.Substring(pos, word.Length) == word)
            pos += word.Length;
    }

    private static void SkipWhitespace(string json, ref int pos)
    {
        while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
    }
}
