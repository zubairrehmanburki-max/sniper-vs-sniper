#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace SniperDuel.EditorTools
{
    /// <summary>
    /// Info.plist keys Unity doesn't expose. Without the export-compliance answer App Store Connect
    /// holds every upload as "Missing Compliance" and TestFlight never offers it to testers.
    /// </summary>
    public static class IosPlist
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            // The game only uses HTTPS from the OS, which is exempt.
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
