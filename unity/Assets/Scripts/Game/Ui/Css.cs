using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>Inherited text state while a layout is built (CSS inheritance done by hand).</summary>
    public sealed class TextContext
    {
        public float FontSize = 16;
        public string Family = "Barlow";
        public int Weight = 400;
        public bool Italic;
        public bool Uppercase;
        /// <summary>Content width in px when known, for resolving grids and percentages.</summary>
        public float? Width;

        public TextContext Clone() => (TextContext)MemberwiseClone();
    }

    /// <summary>What a style declaration asks of layout beyond what IStyle can hold.</summary>
    public sealed class LayoutHints
    {
        public bool Flex;
        public bool Grid;
        public int GridColumns;
        /// <summary>Each column's fixed width in px, or null for a flexible (fr / minmax) track.</summary>
        public float?[] GridTracks = System.Array.Empty<float?>();
        public float RowGap;
        public float ColumnGap;
        public bool DirectionSet;
        public bool Hidden;
        public bool Absolute;
        public float? Width;
        public float? Height;
        public float[] Padding = new float[4];
        public string? Background;
        public string? ClipPath;
        public string? BoxShadow;
        public string? Filter;
        public string? ObjectFit;
        public bool BorderRadiusHalf;
    }

    /// <summary>
    /// Applies inline CSS (the subset the mockups use) to UI Toolkit styles. Anything UI Toolkit
    /// can't express directly (gradients, grids, gaps, em units) is converted here.
    /// </summary>
    public static class Css
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Dictionary<string, string> ParseDeclarations(string? css)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(css)) return d;
            int depth = 0, start = 0;
            for (int i = 0; i <= css!.Length; i++)
            {
                char c = i < css.Length ? css[i] : ';';
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ';' && depth == 0)
                {
                    string decl = css.Substring(start, i - start);
                    int colon = decl.IndexOf(':');
                    if (colon > 0) d[decl.Substring(0, colon).Trim()] = decl.Substring(colon + 1).Trim();
                    start = i + 1;
                }
            }
            return d;
        }

        /// <summary>Applies the declarations to an element; updates the inherited text context in place.</summary>
        public static LayoutHints Apply(VisualElement e, Dictionary<string, string> d, TextContext ctx)
        {
            var h = new LayoutHints();
            var s = e.style;

            // Text first: em units below depend on the font size.
            if (d.TryGetValue("font-size", out var fs)) ctx.FontSize = Px(fs, ctx.FontSize);
            if (d.TryGetValue("font-family", out var ff)) ctx.Family = ff.Contains("Mono") ? "Mono" : ff.Contains("Condensed") ? "Condensed" : "Barlow";
            if (d.TryGetValue("font-weight", out var fw)) ctx.Weight = fw == "bold" ? 700 : fw == "normal" ? 400 : (int)Num(fw, 400);
            if (d.TryGetValue("font-style", out var fst)) ctx.Italic = fst == "italic";
            if (d.TryGetValue("text-transform", out var tt)) ctx.Uppercase = tt == "uppercase";
            if (d.ContainsKey("font-size") || d.ContainsKey("font-family") || d.ContainsKey("font-weight") || d.ContainsKey("font-style"))
            {
                s.fontSize = ctx.FontSize;
                var font = Fonts.Get(ctx.Family, ctx.Weight, ctx.Italic);
                if (font != null) s.unityFontDefinition = FontDefinition.FromFont(font);
            }
            if (d.TryGetValue("letter-spacing", out var ls)) s.letterSpacing = ls.EndsWith("em") ? Num(ls.Substring(0, ls.Length - 2), 0) * ctx.FontSize : Px(ls, ctx.FontSize);
            if (d.TryGetValue("color", out var col) && TryColor(col, out var cc)) s.color = cc;
            if (d.TryGetValue("text-align", out var ta))
                s.unityTextAlign = ta == "center" ? TextAnchor.MiddleCenter : ta == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            if (d.TryGetValue("text-shadow", out var tsh)) s.textShadow = TextShadowOf(tsh);
            if (d.TryGetValue("white-space", out var ws)) s.whiteSpace = ws == "nowrap" ? WhiteSpace.NoWrap : WhiteSpace.Normal;
            // UI Toolkit leaves letter-spacing out of the measured width, so spaced-out labels wrap
            // a word early; they're single-line headings and tags, so keep them on one line.
            else if (d.ContainsKey("letter-spacing")) s.whiteSpace = WhiteSpace.NoWrap;

            // Box.
            if (d.TryGetValue("display", out var disp))
            {
                h.Flex = disp.Contains("flex");
                h.Grid = disp == "grid";
                h.Hidden = disp == "none";
                if (h.Hidden) s.display = DisplayStyle.None;
            }
            if (d.TryGetValue("position", out var pos))
            {
                h.Absolute = pos == "absolute" || pos == "fixed";
                s.position = h.Absolute ? Position.Absolute : Position.Relative;
            }
            if (d.TryGetValue("inset", out var inset))
            {
                var v = Split(inset);
                Side(s, "top", v[0], ctx);
                Side(s, "right", v.Length > 1 ? v[1] : v[0], ctx);
                Side(s, "bottom", v.Length > 2 ? v[2] : v[0], ctx);
                Side(s, "left", v.Length > 3 ? v[3] : v.Length > 1 ? v[1] : v[0], ctx);
            }
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                if (d.TryGetValue(side, out var sv)) Side(s, side, sv, ctx);

            if (d.TryGetValue("width", out var w)) { s.width = Len(w, ctx); if (IsPx(w)) h.Width = Px(w, ctx.FontSize); }
            if (d.TryGetValue("height", out var hh)) { s.height = Len(hh, ctx); if (IsPx(hh)) h.Height = Px(hh, ctx.FontSize); }
            if (d.TryGetValue("min-width", out var mnw)) s.minWidth = Len(mnw, ctx);
            if (d.TryGetValue("min-height", out var mnh)) s.minHeight = Len(mnh, ctx);
            if (d.TryGetValue("max-width", out var mxw)) s.maxWidth = Len(mxw, ctx);
            if (d.TryGetValue("max-height", out var mxh)) s.maxHeight = Len(mxh, ctx);

            if (d.TryGetValue("padding", out var pad)) Box(pad, ctx, out h.Padding);
            foreach (var (k, i) in new[] { ("padding-top", 0), ("padding-right", 1), ("padding-bottom", 2), ("padding-left", 3) })
                if (d.TryGetValue(k, out var pv)) h.Padding[i] = Px(pv, ctx.FontSize);
            s.paddingTop = h.Padding[0]; s.paddingRight = h.Padding[1]; s.paddingBottom = h.Padding[2]; s.paddingLeft = h.Padding[3];
            if (d.TryGetValue("margin", out var mar))
            {
                Box(mar, ctx, out var m);
                s.marginTop = m[0]; s.marginRight = m[1]; s.marginBottom = m[2]; s.marginLeft = m[3];
            }
            if (d.TryGetValue("margin-top", out var mt)) s.marginTop = Px(mt, ctx.FontSize);
            if (d.TryGetValue("margin-right", out var mr)) s.marginRight = Px(mr, ctx.FontSize);
            if (d.TryGetValue("margin-bottom", out var mb)) s.marginBottom = Px(mb, ctx.FontSize);
            if (d.TryGetValue("margin-left", out var ml)) s.marginLeft = ml.EndsWith("%") ? Length.Percent(Num(ml.TrimEnd('%'), 0)) : (StyleLength)Px(ml, ctx.FontSize);

            if (d.TryGetValue("border", out var b)) Border(s, b, "all");
            foreach (var bs in new[] { "top", "right", "bottom", "left" })
                if (d.TryGetValue("border-" + bs, out var bv)) Border(s, bv, bs);
            if (d.TryGetValue("border-radius", out var br))
            {
                if (br.EndsWith("%")) h.BorderRadiusHalf = true;
                else
                {
                    float r = Px(br, ctx.FontSize);
                    s.borderTopLeftRadius = r; s.borderTopRightRadius = r; s.borderBottomLeftRadius = r; s.borderBottomRightRadius = r;
                }
            }

            // Flex.
            if (d.TryGetValue("flex-direction", out var fd))
            {
                h.DirectionSet = true;
                s.flexDirection = fd == "row" ? FlexDirection.Row : fd == "row-reverse" ? FlexDirection.RowReverse : fd == "column-reverse" ? FlexDirection.ColumnReverse : FlexDirection.Column;
            }
            if (d.TryGetValue("flex-wrap", out var fwr)) s.flexWrap = fwr == "wrap" ? Wrap.Wrap : Wrap.NoWrap;
            if (d.TryGetValue("align-items", out var ai)) s.alignItems = Align(ai);
            if (d.TryGetValue("align-self", out var aself)) s.alignSelf = Align(aself);
            if (d.TryGetValue("justify-content", out var jc)) s.justifyContent = Justify(jc);
            if (d.TryGetValue("place-items", out var pi) && pi == "center") { s.alignItems = UnityEngine.UIElements.Align.Center; s.justifyContent = UnityEngine.UIElements.Justify.Center; }
            if (d.TryGetValue("flex", out var fl))
            {
                if (fl == "none") { s.flexGrow = 0; s.flexShrink = 0; }
                else
                {
                    var parts = Split(fl);
                    s.flexGrow = Num(parts[0], 0);
                    s.flexShrink = parts.Length > 1 ? Num(parts[1], 1) : 1;
                    s.flexBasis = parts.Length > 2 ? Len(parts[2], ctx) : (StyleLength)0;
                }
            }
            if (d.TryGetValue("flex-grow", out var fg)) s.flexGrow = Num(fg, 0);
            if (d.TryGetValue("flex-shrink", out var fsh)) s.flexShrink = Num(fsh, 1);
            if (d.TryGetValue("gap", out var gap))
            {
                var g = Split(gap);
                h.RowGap = Px(g[0], ctx.FontSize);
                h.ColumnGap = g.Length > 1 ? Px(g[1], ctx.FontSize) : h.RowGap;
            }
            if (d.TryGetValue("grid-template-columns", out var gtc))
            {
                // Tracks at the top level: "54px repeat(6, minmax(0, 1fr))" is one fixed and six flexible.
                var tracks = new System.Collections.Generic.List<float?>();
                int depth = 0, start = 0;
                gtc = gtc.Trim() + " ";
                for (int i = 0; i < gtc.Length; i++)
                {
                    char ch = gtc[i];
                    if (ch == '(') depth++;
                    else if (ch == ')') depth--;
                    else if (ch == ' ' && depth == 0)
                    {
                        var tok = gtc.Substring(start, i - start).Trim();
                        start = i + 1;
                        if (tok.Length == 0) continue;
                        if (tok.StartsWith("repeat(", StringComparison.Ordinal))
                        {
                            int comma = tok.IndexOf(',');
                            int count = (int)Num(tok.Substring(7, comma - 7), 1);
                            var inner = tok.Substring(comma + 1, tok.Length - comma - 2).Trim();
                            float? tw = inner.EndsWith("px", StringComparison.Ordinal) ? Px(inner, ctx.FontSize) : (float?)null;
                            for (int k = 0; k < count; k++) tracks.Add(tw);
                        }
                        else tracks.Add(tok.EndsWith("px", StringComparison.Ordinal) ? Px(tok, ctx.FontSize) : (float?)null);
                    }
                }
                h.GridTracks = tracks.ToArray();
                h.GridColumns = tracks.Count;
            }

            // Paint.
            if (d.TryGetValue("background", out var bg)) h.Background = bg;
            if (d.TryGetValue("background-color", out var bgc)) h.Background = bgc;
            if (d.TryGetValue("opacity", out var op)) s.opacity = Num(op, 1);
            if (d.TryGetValue("overflow", out var ov)) s.overflow = ov == "hidden" ? Overflow.Hidden : Overflow.Visible;
            if (d.TryGetValue("transform", out var tr)) Transform(s, tr);
            if (d.TryGetValue("clip-path", out var cp)) h.ClipPath = cp;
            if (d.TryGetValue("box-shadow", out var bsh)) h.BoxShadow = bsh;
            if (d.TryGetValue("filter", out var flt)) h.Filter = flt;
            if (d.TryGetValue("object-fit", out var of)) h.ObjectFit = of;
            return h;
        }

        public static void ApplyBackground(VisualElement e, string bg)
        {
            bg = bg.Trim();
            if (bg == "none" || bg == "transparent") { e.style.backgroundColor = Color.clear; return; }
            if (bg.StartsWith("linear-gradient", StringComparison.Ordinal) || bg.StartsWith("radial-gradient", StringComparison.Ordinal))
            {
                e.style.backgroundImage = new StyleBackground(Gradients.Texture(bg));
                e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                return;
            }
            if (TryColor(bg, out var c)) e.style.backgroundColor = c;
        }

        static void Side(IStyle s, string side, string v, TextContext ctx)
        {
            var len = Len(v, ctx);
            float extra = CalcPx(v);
            switch (side)
            {
                case "left": s.left = len; if (extra != 0) s.marginLeft = extra; break;
                case "right": s.right = len; if (extra != 0) s.marginRight = extra; break;
                case "top": s.top = len; if (extra != 0) s.marginTop = extra; break;
                default: s.bottom = len; if (extra != 0) s.marginBottom = extra; break;
            }
        }

        /// <summary>The px part of calc(P% ± Npx), applied as a margin (UI Toolkit has no calc()).</summary>
        static float CalcPx(string v)
        {
            if (!v.StartsWith("calc(", StringComparison.Ordinal)) return 0;
            int minus = v.IndexOf(" - ", StringComparison.Ordinal), plus = v.IndexOf(" + ", StringComparison.Ordinal);
            int at = minus >= 0 ? minus : plus;
            if (at < 0) return 0;
            float px = Num(v.Substring(at + 3).TrimEnd(')').Replace("px", ""), 0);
            return minus >= 0 ? -px : px;
        }

        public static StyleLength Len(string v, TextContext ctx)
        {
            v = v.Trim();
            if (v == "auto") return new StyleLength(StyleKeyword.Auto);
            if (v.StartsWith("calc(", StringComparison.Ordinal))
            {
                int pct = v.IndexOf('%');
                return pct > 0 ? Length.Percent(Num(v.Substring(5, pct - 5), 0)) : new StyleLength(0f);
            }
            if (v.EndsWith("%")) return Length.Percent(Num(v.TrimEnd('%'), 0));
            return Px(v, ctx.FontSize);
        }

        static bool IsPx(string v) => v.EndsWith("px") || v == "0";

        public static float Px(string v, float fontSize)
        {
            v = v.Trim();
            if (v.EndsWith("px")) return Num(v.Substring(0, v.Length - 2), 0);
            if (v.EndsWith("em")) return Num(v.Substring(0, v.Length - 2), 0) * fontSize;
            return Num(v, 0);
        }

        public static float Num(string v, float fallback) =>
            float.TryParse(v.Trim(), NumberStyles.Float, Inv, out var f) ? f : fallback;

        /// <summary>Splits on spaces outside parentheses.</summary>
        public static string[] Split(string v)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            v = v.Trim();
            for (int i = 0; i <= v.Length; i++)
            {
                char c = i < v.Length ? v[i] : ' ';
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ' ' && depth == 0)
                {
                    if (i > start) parts.Add(v.Substring(start, i - start));
                    start = i + 1;
                }
            }
            return parts.Count == 0 ? new[] { "0" } : parts.ToArray();
        }

        static void Box(string v, TextContext ctx, out float[] trbl)
        {
            var p = Split(v);
            float a = Px(p[0], ctx.FontSize);
            float b = p.Length > 1 ? Px(p[1], ctx.FontSize) : a;
            float c = p.Length > 2 ? Px(p[2], ctx.FontSize) : a;
            float d = p.Length > 3 ? Px(p[3], ctx.FontSize) : b;
            trbl = new[] { a, b, c, d };
        }

        static void Border(IStyle s, string v, string side)
        {
            float width = 0;
            Color color = Color.clear;
            if (v != "none")
            {
                foreach (var p in Split(v))
                {
                    if (p.EndsWith("px")) width = Num(p.Substring(0, p.Length - 2), 0);
                    else if (TryColor(p, out var c)) color = c;
                }
                if (width == 0 && color.a > 0) width = 1;
            }
            if (side == "all" || side == "top") { s.borderTopWidth = width; s.borderTopColor = color; }
            if (side == "all" || side == "right") { s.borderRightWidth = width; s.borderRightColor = color; }
            if (side == "all" || side == "bottom") { s.borderBottomWidth = width; s.borderBottomColor = color; }
            if (side == "all" || side == "left") { s.borderLeftWidth = width; s.borderLeftColor = color; }
        }

        static Align Align(string v) => v switch
        {
            "center" => UnityEngine.UIElements.Align.Center,
            "flex-end" or "end" or "baseline" => UnityEngine.UIElements.Align.FlexEnd,
            "stretch" => UnityEngine.UIElements.Align.Stretch,
            _ => UnityEngine.UIElements.Align.FlexStart,
        };

        static Justify Justify(string v) => v switch
        {
            "center" => UnityEngine.UIElements.Justify.Center,
            "flex-end" or "end" => UnityEngine.UIElements.Justify.FlexEnd,
            "space-between" => UnityEngine.UIElements.Justify.SpaceBetween,
            "space-around" => UnityEngine.UIElements.Justify.SpaceAround,
            "space-evenly" => UnityEngine.UIElements.Justify.SpaceEvenly,
            _ => UnityEngine.UIElements.Justify.FlexStart,
        };

        static void Transform(IStyle s, string v)
        {
            float tx = 0, ty = 0, sx = 1, sy = 1;
            bool translate = false, scale = false;
            foreach (var part in Css.Split(v))
            {
                int open = part.IndexOf('(');
                if (open < 0) continue;
                string fn = part.Substring(0, open), arg = part.Substring(open + 1).TrimEnd(')');
                var args = arg.Split(',');
                switch (fn)
                {
                    case "rotate": s.rotate = new Rotate(Angle.Degrees(Num(arg.Replace("deg", ""), 0))); break;
                    case "translate": translate = true; Translate(args[0], ref tx); if (args.Length > 1) Translate(args[1], ref ty); break;
                    case "translateX": translate = true; Translate(arg, ref tx); break;
                    case "translateY": translate = true; Translate(arg, ref ty); break;
                    case "scale": scale = true; sx = Num(args[0], 1); sy = args.Length > 1 ? Num(args[1], 1) : sx; break;
                    case "scaleX": scale = true; sx = Num(arg, 1); break;
                    case "scaleY": scale = true; sy = Num(arg, 1); break;
                }
            }
            if (translate) s.translate = new Translate(new Length(Math.Abs(tx) > 1000 ? (tx - Math.Sign(tx) * 10000) : tx, Math.Abs(tx) > 1000 ? LengthUnit.Percent : LengthUnit.Pixel),
                                                      new Length(Math.Abs(ty) > 1000 ? (ty - Math.Sign(ty) * 10000) : ty, Math.Abs(ty) > 1000 ? LengthUnit.Percent : LengthUnit.Pixel));
            if (scale) s.scale = new Scale(new Vector2(sx, sy));
        }

        /// <summary>Parses one translate argument; percentages are tagged by an offset of ±10000.</summary>
        static void Translate(string a, ref float into)
        {
            a = a.Trim();
            if (a.EndsWith("%")) { float p = Num(a.TrimEnd('%'), 0); into = p + (p < 0 ? -10000 : 10000); }
            else into = Px(a, 16);
        }

        static TextShadow TextShadowOf(string v)
        {
            if (v == "none") return new TextShadow();
            var p = Split(v);
            var px = new List<float>();
            Color c = new Color(0, 0, 0, 0.6f);
            foreach (var part in p)
            {
                if (TryColor(part, out var cc)) c = cc;
                else px.Add(Px(part, 16));
            }
            return new TextShadow
            {
                offset = new Vector2(px.Count > 0 ? px[0] : 0, px.Count > 1 ? px[1] : 0),
                blurRadius = px.Count > 2 ? px[2] : 0,
                color = c,
            };
        }

        public static bool TryColor(string v, out Color c)
        {
            v = v.Trim();
            c = Color.clear;
            if (v.StartsWith("#"))
            {
                string hex = v.Substring(1);
                if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
                if (hex.Length != 6 && hex.Length != 8) return false;
                if (!uint.TryParse(hex, NumberStyles.HexNumber, Inv, out uint n)) return false;
                if (hex.Length == 6) n = (n << 8) | 0xff;
                c = new Color(((n >> 24) & 0xff) / 255f, ((n >> 16) & 0xff) / 255f, ((n >> 8) & 0xff) / 255f, (n & 0xff) / 255f);
                return true;
            }
            if (v.StartsWith("rgb", StringComparison.Ordinal))
            {
                int o = v.IndexOf('('), e = v.IndexOf(')');
                if (o < 0 || e < 0) return false;
                var p = v.Substring(o + 1, e - o - 1).Split(',');
                if (p.Length < 3) return false;
                c = new Color(Num(p[0], 0) / 255f, Num(p[1], 0) / 255f, Num(p[2], 0) / 255f, p.Length > 3 ? Num(p[3], 1) : 1);
                return true;
            }
            switch (v)
            {
                case "white": c = Color.white; return true;
                case "black": c = Color.black; return true;
                case "transparent": c = Color.clear; return true;
                case "none": c = Color.clear; return true;
            }
            return false;
        }
    }
}
