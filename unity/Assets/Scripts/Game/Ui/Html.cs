using System;
using System.Collections.Generic;
using System.Text;

namespace SniperDuel.Game.Ui
{
    /// <summary>One element or text run of a parsed markup file.</summary>
    public sealed class HtmlNode
    {
        public string Tag = "";
        public readonly Dictionary<string, string> Attr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<HtmlNode> Children = new List<HtmlNode>();
        /// <summary>Set for text runs (Tag is "#text").</summary>
        public string? Text;

        public bool IsText => Text != null;
        public string? Get(string name) => Attr.TryGetValue(name, out var v) ? v : null;

        public override string ToString() => IsText ? $"\"{Text}\"" : $"<{Tag}>";
    }

    /// <summary>
    /// A small, forgiving HTML parser for the screen layouts in Resources/Ui. The layouts are the
    /// mockups' own markup (divs, spans, buttons and inline SVG with inline styles), so the game's
    /// screens stay a 1:1 copy of what was signed off.
    /// </summary>
    public static class Html
    {
        static readonly HashSet<string> Void = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "img", "input", "meta", "link", "hr", "stop",
        };

        public static HtmlNode Parse(string src)
        {
            var root = new HtmlNode { Tag = "#root" };
            var stack = new List<HtmlNode> { root };
            int i = 0;
            var text = new StringBuilder();

            void FlushText()
            {
                if (text.Length == 0) return;
                string t = CollapseSpace(Decode(text.ToString()));
                text.Clear();
                if (t.Trim().Length == 0) return;
                stack[stack.Count - 1].Children.Add(new HtmlNode { Tag = "#text", Text = t });
            }

            while (i < src.Length)
            {
                char c = src[i];
                if (c != '<') { text.Append(c); i++; continue; }
                if (string.CompareOrdinal(src, i, "<!--", 0, 4) == 0)
                {
                    int end = src.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? src.Length : end + 3;
                    continue;
                }
                if (i + 1 < src.Length && src[i + 1] == '!')
                {
                    int end = src.IndexOf('>', i);
                    i = end < 0 ? src.Length : end + 1;
                    continue;
                }
                FlushText();
                if (i + 1 < src.Length && src[i + 1] == '/')
                {
                    int end = src.IndexOf('>', i);
                    string name = src.Substring(i + 2, (end < 0 ? src.Length : end) - i - 2).Trim();
                    i = end < 0 ? src.Length : end + 1;
                    for (int k = stack.Count - 1; k > 0; k--)
                    {
                        if (!string.Equals(stack[k].Tag, name, StringComparison.OrdinalIgnoreCase)) continue;
                        stack.RemoveRange(k, stack.Count - k);
                        break;
                    }
                    continue;
                }
                var node = ParseTag(src, ref i, out bool selfClosing);
                if (node == null) { text.Append('<'); continue; }
                stack[stack.Count - 1].Children.Add(node);
                if (!selfClosing && !Void.Contains(node.Tag))
                {
                    if (node.Tag == "script" || node.Tag == "style")
                    {
                        int end = src.IndexOf("</" + node.Tag, i, StringComparison.OrdinalIgnoreCase);
                        end = end < 0 ? src.Length : end;
                        node.Children.Add(new HtmlNode { Tag = "#text", Text = src.Substring(i, end - i) });
                        i = end;
                    }
                    stack.Add(node);
                }
            }
            FlushText();
            return root;
        }

        static HtmlNode? ParseTag(string s, ref int i, out bool selfClosing)
        {
            selfClosing = false;
            int j = i + 1;
            int start = j;
            while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '-' || s[j] == ':' || s[j] == '_')) j++;
            if (j == start) return null;
            var node = new HtmlNode { Tag = s.Substring(start, j - start).ToLowerInvariant() };
            while (j < s.Length)
            {
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                if (j >= s.Length) break;
                if (s[j] == '>') { j++; break; }
                if (s[j] == '/' && j + 1 < s.Length && s[j + 1] == '>') { selfClosing = true; j += 2; break; }
                int ns = j;
                while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != '=' && s[j] != '>' && s[j] != '/') j++;
                string name = s.Substring(ns, j - ns);
                if (name.Length == 0) { j++; continue; }
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                string value = "";
                if (j < s.Length && s[j] == '=')
                {
                    j++;
                    while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                    if (j < s.Length && (s[j] == '"' || s[j] == '\''))
                    {
                        char q = s[j++];
                        int vs = j;
                        while (j < s.Length && s[j] != q) j++;
                        value = s.Substring(vs, j - vs);
                        j++;
                    }
                    else
                    {
                        int vs = j;
                        while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != '>') j++;
                        value = s.Substring(vs, j - vs);
                    }
                }
                node.Attr[name] = Decode(value);
            }
            i = j;
            return node;
        }

        static string CollapseSpace(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t')
                {
                    if (!space) sb.Append(' ');
                    space = true;
                }
                else
                {
                    sb.Append(c);
                    space = false;
                }
            }
            return sb.ToString();
        }

        static readonly Dictionary<string, string> Entities = new Dictionary<string, string>
        {
            ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
            ["middot"] = "·", ["times"] = "×", ["deg"] = "°", ["minus"] = "−", ["ndash"] = "–", ["mdash"] = "—",
            ["rarr"] = "→", ["larr"] = "←", ["hellip"] = "…", ["plusmn"] = "±",
        };

        public static string Decode(string s)
        {
            if (s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '&') { sb.Append(s[i]); continue; }
                int semi = s.IndexOf(';', i);
                if (semi < 0 || semi - i > 10) { sb.Append('&'); continue; }
                string name = s.Substring(i + 1, semi - i - 1);
                if (name.StartsWith("#x", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hx))
                    sb.Append(char.ConvertFromUtf32(hx));
                else if (name.StartsWith("#") && int.TryParse(name.Substring(1), out int dec))
                    sb.Append(char.ConvertFromUtf32(dec));
                else if (Entities.TryGetValue(name, out var e))
                    sb.Append(e);
                else { sb.Append('&'); continue; }
                i = semi;
            }
            return sb.ToString();
        }
    }
}
