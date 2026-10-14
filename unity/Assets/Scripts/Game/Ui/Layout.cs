using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// One screen built from a layout in Resources/Ui. Elements marked data-bind="name" can be
    /// changed from code; data-on="name" makes an element a button that fires that action;
    /// data-hold="name" reports press and release (hold breath, dial repeat).
    /// </summary>
    public sealed class UiScreen
    {
        public readonly string Name;
        public readonly VisualElement Root;
        readonly Dictionary<string, VisualElement> binds = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, Action> actions = new Dictionary<string, Action>();
        readonly Dictionary<string, Action<bool>> holds = new Dictionary<string, Action<bool>>();

        public UiScreen(string name, VisualElement root)
        {
            Name = name;
            Root = root;
        }

        internal void Bind(string id, VisualElement e) => binds[id] = e;

        public bool Has(string id) => binds.ContainsKey(id);

        public VisualElement this[string id] =>
            binds.TryGetValue(id, out var e) ? e : throw new KeyNotFoundException($"{Name}: no element data-bind=\"{id}\"");

        /// <summary>Sets the text of a bound element (its first text run).</summary>
        public void Text(string id, string text)
        {
            if (!binds.TryGetValue(id, out var e)) return;
            var label = e as Label ?? e.Q<Label>();
            if (label != null) label.text = text;
        }

        public void Show(string id, bool visible)
        {
            if (binds.TryGetValue(id, out var e)) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Color(string id, Color c)
        {
            if (!binds.TryGetValue(id, out var e)) return;
            e.style.color = c;
            foreach (var l in e.Query<Label>().ToList()) l.style.color = c;
        }

        /// <summary>Sets a bound element's width as a fraction of its parent (progress bars).</summary>
        public void Fill(string id, float fraction)
        {
            if (binds.TryGetValue(id, out var e)) e.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100);
        }

        public void Image(string id, Texture? tex)
        {
            if (!binds.TryGetValue(id, out var e)) return;
            e.style.backgroundImage = tex is RenderTexture rt ? Background.FromRenderTexture(rt)
                : tex is Texture2D t2 ? new StyleBackground(t2) : new StyleBackground(StyleKeyword.None);
        }

        public void On(string action, Action handler) => actions[action] = handler;

        public void OnHold(string action, Action<bool> handler) => holds[action] = handler;

        internal void Fire(string action)
        {
            if (actions.TryGetValue(action, out var a)) a();
            else Debug.Log($"[Ui] {Name}: '{action}' has no handler");
        }

        internal void Hold(string action, bool down)
        {
            if (holds.TryGetValue(action, out var a)) a(down);
        }
    }

    /// <summary>Builds UI Toolkit trees from the mockup markup (see Html and Css).</summary>
    public static class Layout
    {
        /// <summary>Class on every pressable element, so a look-drag can tell them from the background.</summary>
        public const string ActionClass = "act";

        static readonly HashSet<string> Inline = new HashSet<string> { "span", "b", "strong", "em", "i", "a", "small", "#text" };

        public static UiScreen Load(string name)
        {
            var asset = Resources.Load<TextAsset>("Ui/" + name);
            if (asset == null) throw new FileNotFoundException("Missing layout Resources/Ui/" + name);
            return Build(name, Html.Parse(asset.text));
        }

        public static UiScreen Build(string name, HtmlNode doc)
        {
            var top = Find(doc, n => n.Tag == "div") ?? throw new InvalidDataException(name + ": no root element");
            var root = new VisualElement { name = name };
            var screen = new UiScreen(name, root);
            var ctx = new TextContext { Width = 932 };
            Element(screen, top, root, ctx, isRoot: true);
            // The mockups are drawn at 932 × 430 (an iPhone in landscape); the root fills the panel instead.
            var r = root.Children().First();
            r.style.position = Position.Absolute;
            r.style.left = 0; r.style.top = 0; r.style.right = 0; r.style.bottom = 0;
            r.style.width = StyleKeyword.Auto; r.style.height = StyleKeyword.Auto;
            // The 3D scene renders behind every screen.
            r.style.backgroundColor = UnityEngine.Color.clear;
            r.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            return screen;
        }

        static HtmlNode? Find(HtmlNode n, Func<HtmlNode, bool> pred)
        {
            if (pred(n)) return n;
            foreach (var c in n.Children)
            {
                var f = Find(c, pred);
                if (f != null) return f;
            }
            return null;
        }

        static void Element(UiScreen screen, HtmlNode node, VisualElement parent, TextContext inherited, bool isRoot = false)
        {
            if (node.IsText)
            {
                var label = new Label(inherited.Uppercase ? node.Text!.ToUpperInvariant() : node.Text!.Trim());
                label.pickingMode = PickingMode.Ignore;
                ZeroBox(label);
                label.style.flexShrink = 0;
                parent.Add(label);
                return;
            }
            if (node.Tag == "br")
            {
                parent.Add(new VisualElement { name = "br", pickingMode = PickingMode.Ignore });
                return;
            }
            if (node.Tag == "script" || node.Tag == "style" || node.Tag == "title") return;
            if (node.Tag == "svg")
            {
                parent.Add(Svg(node, inherited));
                return;
            }

            var ctx = inherited.Clone();
            var e = new VisualElement { name = node.Get("data-bind") ?? node.Tag };
            e.style.flexShrink = 0;
            var decl = Css.ParseDeclarations(node.Get("style"));
            if (node.Tag == "button" || node.Tag == "nav")
                e.style.unityTextAlign = TextAnchor.MiddleCenter;
            var h = Css.Apply(e, decl, ctx);

            // CSS width/height are content-box unless box-sizing says otherwise; UI Toolkit's are border-box.
            bool borderBox = decl.TryGetValue("box-sizing", out var bs) && bs == "border-box";
            float padX = h.Padding[1] + h.Padding[3], padY = h.Padding[0] + h.Padding[2];
            float bordX = e.style.borderLeftWidth.value + e.style.borderRightWidth.value;
            float bordY = e.style.borderTopWidth.value + e.style.borderBottomWidth.value;
            if (!borderBox && h.Width.HasValue) e.style.width = h.Width.Value + padX + bordX;
            if (!borderBox && h.Height.HasValue) e.style.height = h.Height.Value + padY + bordY;
            if (h.Width.HasValue) ctx.Width = borderBox ? h.Width.Value - padX - bordX : h.Width.Value;
            else if (!h.Absolute && inherited.Width.HasValue) ctx.Width = inherited.Width.Value - padX - bordX;
            else ctx.Width = null;

            if (h.Background != null) Css.ApplyBackground(e, h.Background);
            if (h.BorderRadiusHalf)
            {
                float r = h.Width.HasValue && h.Height.HasValue ? Mathf.Min(h.Width.Value, h.Height.Value) / 2 : 9999;
                e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r; e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
            }
            if (h.ObjectFit == "cover") e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            if (h.ClipPath != null) ClipCorner(e, h.ClipPath);
            if (node.Tag == "button" && !h.Flex && !h.Grid)
            {
                e.style.justifyContent = Justify.Center;
                e.style.alignItems = decl.TryGetValue("text-align", out var ta) && ta == "left" ? Align.FlexStart : Align.Center;
            }

            string? img = node.Get("data-src");
            if (node.Tag == "img")
            {
                if (img != null && !img.StartsWith("rt:", StringComparison.Ordinal))
                {
                    var tex = Resources.Load<Texture2D>(img);
                    if (tex != null) e.style.backgroundImage = tex;
                }
                e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                if (node.Get("data-bind") == null && img != null) screen.Bind(img, e);
            }

            parent.Add(e);
            foreach (var c in node.Children) Element(screen, c, e, ctx);

            // Layout direction: flex defaults to a row in CSS, a column in UI Toolkit.
            bool hasBr = node.Children.Any(c => c.Tag == "br");
            if (h.Grid && h.GridColumns > 0) ApplyGrid(e, h, ctx);
            else if (h.Flex)
            {
                if (!h.DirectionSet) e.style.flexDirection = FlexDirection.Row;
                ApplyGap(e, h);
            }
            else if (!hasBr && node.Children.Count > 1 && node.Children.All(c => Inline.Contains(c.Tag) && !Css.ParseDeclarations(c.Get("style")).ContainsKey("display")))
            {
                // Inline runs ("MARKSMAN II · <span>1,384</span>") sit on one line.
                e.style.flexDirection = FlexDirection.Row;
                e.style.flexWrap = Wrap.Wrap;
                if (!decl.ContainsKey("align-items")) e.style.alignItems = Align.FlexEnd;
                if (decl.TryGetValue("text-align", out var ta2))
                    e.style.justifyContent = ta2 == "center" ? Justify.Center : ta2 == "right" ? Justify.FlexEnd : Justify.FlexStart;
            }
            else if (h.Grid) ApplyGap(e, h);

            // Hooks.
            string? bind = node.Get("data-bind");
            if (bind != null) screen.Bind(bind, e);
            string? on = node.Get("data-on");
            if (on != null)
            {
                e.pickingMode = PickingMode.Position;
                e.RegisterCallback<ClickEvent>(_ =>
                {
                    // A click, or a chime to confirm or go back; rifle controls make their own sound.
                    Sound.UiTap(on);
                    screen.Fire(on);
                });
            }
            string? hold = node.Get("data-hold");
            if (hold != null)
            {
                e.pickingMode = PickingMode.Position;
                e.RegisterCallback<PointerDownEvent>(_ => screen.Hold(hold, true), TrickleDown.TrickleDown);
                e.RegisterCallback<PointerUpEvent>(_ => screen.Hold(hold, false));
                e.RegisterCallback<PointerLeaveEvent>(_ => screen.Hold(hold, false));
            }
            if (node.Tag == "button" || on != null || hold != null)
            {
                e.AddToClassList(ActionClass);
                PressFeedback(e);
            }
            else if (!isRoot && h.Background == null && !(decl.ContainsKey("border") || decl.Keys.Any(k => k.StartsWith("border-", StringComparison.Ordinal) && k != "border-radius")))
                e.pickingMode = PickingMode.Ignore;
            if (h.Hidden) e.style.display = DisplayStyle.None;
            if (node.Get("data-hidden") != null) e.style.display = DisplayStyle.None;
        }

        static void ZeroBox(VisualElement e)
        {
            e.style.marginLeft = 0; e.style.marginRight = 0; e.style.marginTop = 0; e.style.marginBottom = 0;
            e.style.paddingLeft = 0; e.style.paddingRight = 0; e.style.paddingTop = 0; e.style.paddingBottom = 0;
        }

        static IEnumerable<VisualElement> Flow(VisualElement e) =>
            e.Children().Where(c => c.style.position.value != Position.Absolute && c.name != "br");

        static void ApplyGap(VisualElement e, LayoutHints h)
        {
            bool row = e.style.flexDirection.value == FlexDirection.Row || e.style.flexDirection.value == FlexDirection.RowReverse;
            float gap = row ? h.ColumnGap : h.RowGap;
            if (gap <= 0) return;
            bool first = true;
            foreach (var c in Flow(e))
            {
                if (!first)
                {
                    if (row) c.style.marginLeft = c.style.marginLeft.value.value + gap;
                    else c.style.marginTop = c.style.marginTop.value.value + gap;
                }
                first = false;
            }
        }

        static void ApplyGrid(VisualElement e, LayoutHints h, TextContext ctx)
        {
            int n = h.GridColumns;
            e.style.flexDirection = FlexDirection.Row;
            e.style.flexWrap = Wrap.Wrap;
            e.style.alignItems = Align.FlexStart;
            int i = 0;
            foreach (var c in Flow(e))
            {
                float? track = h.GridTracks.Length == n ? h.GridTracks[i % n] : null;
                if (track.HasValue) c.style.width = track.Value;
                else if (ctx.Width.HasValue)
                {
                    // Flexible tracks share what the fixed ones and the gaps leave.
                    float fixedW = 0;
                    int flex = 0;
                    foreach (var t in h.GridTracks) { if (t.HasValue) fixedW += t.Value; else flex++; }
                    if (h.GridTracks.Length != n) { fixedW = 0; flex = n; }
                    c.style.width = (ctx.Width.Value - (n - 1) * h.ColumnGap - fixedW) / Mathf.Max(1, flex);
                }
                else c.style.width = Length.Percent(100f / n - 2);
                if (i % n != 0) c.style.marginLeft = h.ColumnGap;
                if (i >= n) c.style.marginTop = h.RowGap;
                i++;
            }
        }

        static void PressFeedback(VisualElement e)
        {
            e.RegisterCallback<PointerDownEvent>(_ => e.style.scale = new Scale(new Vector2(0.96f, 0.96f)), TrickleDown.TrickleDown);
            e.RegisterCallback<PointerUpEvent>(_ => e.style.scale = new Scale(Vector2.one));
            e.RegisterCallback<PointerLeaveEvent>(_ => e.style.scale = new Scale(Vector2.one));
        }

        /// <summary>
        /// The mockups cut one corner off the big buttons with clip-path. UI Toolkit can't clip to a
        /// polygon, so the cut is drawn over the corner in the colour behind the button.
        /// </summary>
        static void ClipCorner(VisualElement e, string clip)
        {
            if (!clip.Contains("100% 7") && !clip.Contains("94% 100%") && !clip.Contains("93% 100%")) return;
            var cut = new VisualElement { name = "corner-cut", pickingMode = PickingMode.Ignore };
            cut.style.position = Position.Absolute;
            cut.style.right = 0;
            cut.style.bottom = 0;
            cut.style.width = 18;
            cut.style.height = 18;
            cut.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.fillColor = new Color(0.03f, 0.04f, 0.035f, 1f);
                p.BeginPath();
                p.MoveTo(new Vector2(18, 0));
                p.LineTo(new Vector2(18, 18));
                p.LineTo(new Vector2(0, 18));
                p.ClosePath();
                p.Fill();
            };
            e.Add(cut);
        }

        // ---- SVG ----

        static VisualElement Svg(HtmlNode node, TextContext ctx)
        {
            var e = new VisualElement { name = node.Get("data-bind") ?? "svg", pickingMode = PickingMode.Ignore };
            e.style.flexShrink = 0;
            var decl = Css.ParseDeclarations(node.Get("style"));
            var h = Css.Apply(e, decl, ctx.Clone());
            float w = Css.Num(node.Get("width") ?? "", h.Width ?? 24), hh = Css.Num(node.Get("height") ?? "", h.Height ?? 24);
            if (!decl.ContainsKey("width")) e.style.width = w;
            if (!decl.ContainsKey("height")) e.style.height = hh;

            // <text> isn't supported by the SVG importer: draw those as labels over the image.
            var texts = new List<(HtmlNode node, Vector2 offset)>();
            CollectText(node, Vector2.zero, texts);
            var vb = (node.Get("viewBox") ?? $"0 0 {w} {hh}").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => Css.Num(v, 0)).ToArray();
            float sx = vb.Length == 4 && vb[2] > 0 ? w / vb[2] : 1, sy = vb.Length == 4 && vb[3] > 0 ? hh / vb[3] : 1;

            try
            {
                var sb = new StringBuilder();
                Serialize(node, sb, skipText: true);
                // The vector image is cropped to its geometry; a near-invisible rect over the whole
                // viewBox keeps the drawing where the viewBox puts it (a lone minus sign stays a line).
                string svg = sb.ToString();
                int open = svg.IndexOf('>');
                if (open > 0 && vb.Length == 4)
                {
                    string frame = FormattableString.Invariant($"<rect x=\"{vb[0]}\" y=\"{vb[1]}\" width=\"{vb[2]}\" height=\"{vb[3]}\" fill=\"#000000\" fill-opacity=\"0.004\" stroke=\"none\"/>");
                    svg = svg.Insert(open + 1, frame);
                }
                using var reader = new StringReader(svg);
                var scene = SVGParser.ImportSVG(reader, ViewportOptions.PreserveViewport, 0, 1, (int)w, (int)hh);
                var image = VectorUtils.BuildVectorImage(scene);
                e.style.backgroundImage = Background.FromVectorImage(image);
                e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ui] SVG failed: {ex.Message}");
            }

            foreach (var (t, offset) in texts)
            {
                var label = new Label(t.Children.FirstOrDefault(c => c.IsText)?.Text ?? "") { pickingMode = PickingMode.Ignore };
                ZeroBox(label);
                float size = Css.Num(t.Get("font-size") ?? "12", 12) * sy;
                float x = (Css.Num(t.Get("x") ?? "0", 0) + offset.x - (vb.Length == 4 ? vb[0] : 0)) * sx;
                float y = (Css.Num(t.Get("y") ?? "0", 0) + offset.y - (vb.Length == 4 ? vb[1] : 0)) * sy;
                label.style.position = Position.Absolute;
                label.style.fontSize = size;
                string family = t.Get("font-family") ?? "";
                var font = Fonts.Get(family.Contains("Mono") ? "Mono" : family.Contains("Condensed") ? "Condensed" : "Barlow", (int)Css.Num(t.Get("font-weight") ?? "400", 400), false);
                if (font != null) label.style.unityFontDefinition = FontDefinition.FromFont(font);
                if (Css.TryColor(t.Get("fill") ?? "#EDEFE8", out var col)) label.style.color = col;
                // SVG y is the baseline; put the label's box so its baseline lands there.
                label.style.top = y - size * 0.82f;
                string anchor = t.Get("text-anchor") ?? "start";
                if (anchor == "middle") { label.style.left = x - 200; label.style.width = 400; label.style.unityTextAlign = TextAnchor.UpperCenter; }
                else if (anchor == "end") { label.style.left = x - 400; label.style.width = 400; label.style.unityTextAlign = TextAnchor.UpperRight; }
                else label.style.left = x;
                if (t.Get("data-bind") != null) label.name = t.Get("data-bind");
                e.Add(label);
            }
            return e;
        }

        static void CollectText(HtmlNode n, Vector2 offset, List<(HtmlNode, Vector2)> into)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "text") { into.Add((c, offset)); continue; }
                var o = offset;
                string? tr = c.Get("transform");
                if (tr != null && tr.StartsWith("translate(", StringComparison.Ordinal))
                {
                    var xy = tr.Substring(10, tr.IndexOf(')') - 10).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    o += new Vector2(Css.Num(xy[0], 0), xy.Length > 1 ? Css.Num(xy[1], 0) : 0);
                }
                CollectText(c, o, into);
            }
        }

        public static void Serialize(HtmlNode n, StringBuilder sb, bool skipText)
        {
            if (n.IsText) { sb.Append(System.Security.SecurityElement.Escape(n.Text)); return; }
            if (skipText && n.Tag == "text") return;
            sb.Append('<').Append(n.Tag);
            if (n.Tag == "svg" && !n.Attr.ContainsKey("xmlns")) sb.Append(" xmlns=\"http://www.w3.org/2000/svg\"");
            foreach (var kv in n.Attr)
            {
                if (n.Tag == "svg" && (kv.Key == "style" || kv.Key.StartsWith("aria", StringComparison.Ordinal) || kv.Key == "role")) continue;
                if (kv.Key.StartsWith("data-", StringComparison.Ordinal)) continue;
                // The importer reads presentation attributes; style="filter: …" etc. would be ignored or fail.
                if (kv.Key == "style") continue;
                sb.Append(' ').Append(kv.Key).Append("=\"").Append(System.Security.SecurityElement.Escape(kv.Value)).Append('"');
            }
            sb.Append('>');
            foreach (var c in n.Children) Serialize(c, sb, skipText);
            sb.Append("</").Append(n.Tag).Append('>');
        }
    }
}
