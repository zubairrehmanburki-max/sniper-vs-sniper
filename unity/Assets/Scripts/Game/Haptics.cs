using System.Runtime.InteropServices;

namespace SniperDuel.Game
{
    /// <summary>Taptic feedback on iPhone (Plugins/iOS/Haptics.mm); silent elsewhere.</summary>
    public static class Haptics
    {
        public static bool Enabled = true;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SvsHaptic(int kind);
#else
        static void SvsHaptic(int kind) { }
#endif

        static void Play(int kind)
        {
            if (Enabled) SvsHaptic(kind);
        }

        public static void Click() => Play(0);
        /// <summary>One detent of a scope knob: a short, crisp tick.</summary>
        public static void Detent() => Play(5);
        public static void Recoil() => Play(1);
        public static void Snap() => Play(2);
        public static void Struck(bool lethal) => Play(lethal ? 3 : 4);
    }
}
