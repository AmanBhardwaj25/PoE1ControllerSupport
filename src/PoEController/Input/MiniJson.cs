using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PoEController.Input
{
    /// <summary>
    /// A small JSON reader for the haptics files: objects become Dictionary&lt;string, object&gt;, arrays
    /// List&lt;object&gt;, numbers double, plus string, bool and null. Unity's JsonUtility leaves the mod's classes
    /// empty in this game (no error, nothing filled in), so the files are read with this instead. Throws
    /// FormatException with the position on a broken file.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Skip(text, ref i);
            if (i < text.Length)
            {
                throw Error(text, i, "unexpected text after the end");
            }
            return v;
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length)
            {
                throw Error(s, i, "unexpected end of file");
            }
            char c = s[i];
            switch (c)
            {
                case '{': return Obj(s, ref i);
                case '[': return Arr(s, ref i);
                case '"': return Str(s, ref i);
                case 't': return Word(s, ref i, "true", true);
                case 'f': return Word(s, ref i, "false", false);
                case 'n': return Word(s, ref i, "null", null);
            }
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                {
                    i++;
                }
                if (double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    return d;
                }
                throw Error(s, start, "bad number");
            }
            throw Error(s, i, $"unexpected '{c}'");
        }

        private static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return d;
            }
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    throw Error(s, i, "expected a \"name\"");
                }
                string key = Str(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw Error(s, i, "expected ':'");
                }
                i++;
                d[key] = Value(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return d;
                }
                throw Error(s, i, "expected ',' or '}'");
            }
        }

        private static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return l;
            }
            while (true)
            {
                l.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return l;
                }
                throw Error(s, i, "expected ',' or ']'");
            }
        }

        private static string Str(string s, ref int i)
        {
            var b = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return b.ToString();
                }
                if (c != '\\')
                {
                    b.Append(c);
                    continue;
                }
                if (i >= s.Length)
                {
                    break;
                }
                char e = s[i++];
                switch (e)
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append('\t'); break;
                    case 'r': b.Append('\r'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u))
                        {
                            throw Error(s, i, "bad \\u escape");
                        }
                        b.Append((char)u);
                        i += 4;
                        break;
                    default: b.Append(e); break;
                }
            }
            throw Error(s, i, "unterminated string");
        }

        private static object Word(string s, ref int i, string word, object value)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw Error(s, i, "unexpected word");
            }
            i += word.Length;
            return value;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static FormatException Error(string s, int i, string what)
        {
            int line = 1;
            for (int k = 0; k < i && k < s.Length; k++)
            {
                if (s[k] == '\n')
                {
                    line++;
                }
            }
            return new FormatException($"{what} on line {line}");
        }

        // ---- Typed helpers

        public static float Num(Dictionary<string, object> d, string key, float fallback) =>
            d != null && d.TryGetValue(key, out object v) && v is double n ? (float)n : fallback;

        public static string Text(Dictionary<string, object> d, string key, string fallback) =>
            d != null && d.TryGetValue(key, out object v) && v is string t ? t : fallback;

        public static Dictionary<string, object> Child(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

        public static List<object> List(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out object v) ? v as List<object> : null;
    }
}
