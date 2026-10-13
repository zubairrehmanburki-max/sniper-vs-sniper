using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// A short notice over a screen: the element bound "toast", with "toast-title" and
    /// "toast-text" inside. It hides itself after a few seconds or when tapped.
    /// </summary>
    public static class Toast
    {
        static int serial;

        public static void Show(UiScreen s, string title, string text, float seconds = 3.5f)
        {
            if (!s.Has("toast"))
            {
                Debug.Log($"[Ui] {s.Name}: {title} · {text}");
                return;
            }
            var e = s["toast"];
            s.Text("toast-title", title);
            s.Text("toast-text", text);
            s.On("toast", () => e.style.display = DisplayStyle.None);
            e.style.display = DisplayStyle.Flex;
            int stamp = ++serial;
            e.userData = stamp;
            e.schedule.Execute(() =>
            {
                if (e.userData is int n && n == stamp) e.style.display = DisplayStyle.None;
            }).ExecuteLater((long)(seconds * 1000));
        }

        public static void Hide(UiScreen s) => s.Show("toast", false);
    }
}
