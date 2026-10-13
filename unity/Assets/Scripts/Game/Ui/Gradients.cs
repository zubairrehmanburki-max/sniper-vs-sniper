using System;
using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// CSS linear-gradient() and radial-gradient() baked into small textures, cached by their text.
    /// UI Toolkit draws them stretched over the element.
    /// </summary>
    public static class Gradients
    {
        const int Size = 128;
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        struct Stop
        {
            public Color Color;
            public float At;
        }

        public static Texture2D Texture(string css)
        {
            if (Cache.TryGetValue(css, out var t) && t != null) return t;
            int open = css.IndexOf('('), close = css.LastIndexOf(')');
            var args = SplitArgs(css.Substring(open + 1, close - open - 1));
            bool radial = css.StartsWith("radial", StringComparison.Ordinal);

            float angle = 180;
            Vector2 centre = new Vector2(0.5f, 0.5f);
            bool closestSide = false;
            int first = 0;
            if (args.Count > 0 && !Css.TryColor(Css.Split(args[0])[0], out _))
            {
                string a = args[0].Trim();
                first = 1;
                if (!radial)
                {
                    if (a.EndsWith("deg")) angle = Css.Num(a.Substring(0, a.Length - 3), 180);
                    else if (a == "to right") angle = 90;
                    else if (a == "to left") angle = 270;
                    else if (a == "to top") angle = 0;
                }
                else
                {
                    closestSide = a.Contains("closest-side");
                    int at = a.IndexOf(" at ", StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        var p = Css.Split(a.Substring(at + 4));
                        centre = new Vector2(Css.Num(p[0].TrimEnd('%'), 50) / 100f, Css.Num((p.Length > 1 ? p[1] : p[0]).TrimEnd('%'), 50) / 100f);
                    }
                }
            }
            var stops = new List<Stop>();
            for (int i = first; i < args.Count; i++)
            {
                var p = Css.Split(args[i]);
                if (!Css.TryColor(p[0], out var c)) continue;
                float at = p.Length > 1 ? Css.Num(p[1].TrimEnd('%'), 0) / 100f : float.NaN;
                stops.Add(new Stop { Color = c, At = at });
            }
            if (stops.Count == 0) stops.Add(new Stop { Color = Color.clear, At = 0 });
            // Unpositioned stops spread evenly between their neighbours, as in CSS.
            if (float.IsNaN(stops[0].At)) stops[0] = new Stop { Color = stops[0].Color, At = 0 };
            if (float.IsNaN(stops[stops.Count - 1].At)) stops[stops.Count - 1] = new Stop { Color = stops[stops.Count - 1].Color, At = 1 };
            for (int i = 1; i < stops.Count - 1; i++)
            {
                if (!float.IsNaN(stops[i].At)) continue;
                int j = i;
                while (float.IsNaN(stops[j].At)) j++;
                for (int k = i; k < j; k++)
                    stops[k] = new Stop { Color = stops[k].Color, At = Mathf.Lerp(stops[i - 1].At, stops[j].At, (k - i + 1f) / (j - i + 1f)) };
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "gradient" };
            var px = new Color[Size * Size];
            float rad = angle * Mathf.Deg2Rad;
            // CSS angle 0 points up, 90 right; texture v grows upwards.
            var dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            float half = 0.5f * (Mathf.Abs(dir.x) + Mathf.Abs(dir.y));
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                    float t01;
                    if (!radial)
                        t01 = 0.5f + (Vector2.Dot(new Vector2(u - 0.5f, v - 0.5f), dir)) / (2 * half);
                    else
                    {
                        // CSS y runs down; textures run up.
                        float dx = u - centre.x, dy = (1 - v) - centre.y;
                        float rx = closestSide ? Mathf.Min(centre.x, 1 - centre.x) : Mathf.Max(centre.x, 1 - centre.x) * 1.4142f;
                        float ry = closestSide ? Mathf.Min(centre.y, 1 - centre.y) : Mathf.Max(centre.y, 1 - centre.y) * 1.4142f;
                        t01 = Mathf.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
                    }
                    px[y * Size + x] = Sample(stops, Mathf.Clamp01(t01));
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            Cache[css] = tex;
            return tex;
        }

        static Color Sample(List<Stop> stops, float t)
        {
            if (t <= stops[0].At) return stops[0].Color;
            for (int i = 1; i < stops.Count; i++)
            {
                if (t > stops[i].At) continue;
                float span = stops[i].At - stops[i - 1].At;
                return Color.Lerp(stops[i - 1].Color, stops[i].Color, span <= 0 ? 1 : (t - stops[i - 1].At) / span);
            }
            return stops[stops.Count - 1].Color;
        }

        static List<string> SplitArgs(string s)
        {
            var list = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                char c = i < s.Length ? s[i] : ',';
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    list.Add(s.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            return list;
        }
    }
}
