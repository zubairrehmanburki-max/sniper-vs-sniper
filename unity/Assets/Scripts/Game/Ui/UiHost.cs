using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The one UI Toolkit panel. Layouts are drawn at the mockups' 932 × 430 and scaled to the
    /// screen; the root is inset by the device safe area so nothing sits under the notch.
    /// </summary>
    public sealed class UiHost : MonoBehaviour
    {
        public const float RefWidth = 932, RefHeight = 430;

        UIDocument doc = null!;
        VisualElement safe = null!;
        readonly Dictionary<string, UiScreen> screens = new Dictionary<string, UiScreen>();
        Rect lastSafe;

        public UiScreen? Current { get; private set; }
        public VisualElement Overlay { get; private set; } = null!;

        public static UiHost Create()
        {
            var go = new GameObject("Ui");
            go.SetActive(false);
            var host = go.AddComponent<UiHost>();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int((int)RefWidth, (int)RefHeight);
            settings.screenMatchMode = PanelScreenMatchMode.Expand;
            settings.clearColor = false;
            settings.sortingOrder = 10;
            host.doc = go.AddComponent<UIDocument>();
            host.doc.panelSettings = settings;
            go.SetActive(true);
            host.Init();
            DontDestroyOnLoad(go);
            return host;
        }

        void Init()
        {
            var root = doc.rootVisualElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            safe = new VisualElement { name = "safe-area", pickingMode = PickingMode.Ignore };
            safe.style.position = Position.Absolute;
            safe.style.left = 0; safe.style.right = 0; safe.style.top = 0; safe.style.bottom = 0;
            root.Add(safe);
            Overlay = new VisualElement { name = "overlay", pickingMode = PickingMode.Ignore };
            Overlay.style.position = Position.Absolute;
            Overlay.style.left = 0; Overlay.style.right = 0; Overlay.style.top = 0; Overlay.style.bottom = 0;
            root.Add(Overlay);
            ApplySafeArea();
        }

        void Update()
        {
            if (Screen.safeArea != lastSafe) ApplySafeArea();
        }

        void ApplySafeArea()
        {
            lastSafe = Screen.safeArea;
            if (safe == null || Screen.width == 0) return;
            float scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
            var sa = Screen.safeArea;
            // The mockups already keep a 20 px margin; only the part of the inset beyond that counts.
            float left = Mathf.Max(0, sa.xMin / scale - 12), right = Mathf.Max(0, (Screen.width - sa.xMax) / scale - 12);
            safe.style.left = left;
            safe.style.right = right;
        }

        /// <summary>Raised once per screen when its layout is first built, for screen-specific setup.</summary>
        public event System.Action<UiScreen>? Created;

        public UiScreen Get(string name)
        {
            if (screens.TryGetValue(name, out var s)) return s;
            s = Layout.Load(name);
            s.Root.style.display = DisplayStyle.None;
            safe.Add(s.Root);
            screens[name] = s;
            Created?.Invoke(s);
            return s;
        }

        public UiScreen Show(string name)
        {
            var s = Get(name);
            if (Current != null && Current != s) Current.Root.style.display = DisplayStyle.None;
            s.Root.style.display = DisplayStyle.Flex;
            Current = s;
            return s;
        }

        public void HideAll()
        {
            if (Current != null) Current.Root.style.display = DisplayStyle.None;
            Current = null;
        }
    }
}
