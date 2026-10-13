#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace SniperDuel.EditorTools
{
    /// <summary>
    /// Info.plist keys Unity doesn't expose. Without the export-compliance answer App Store Connect
    /// holds every upload as "Missing Compliance" and TestFlight never offers it to testers. The build
    /// number is left to Xcode so each upload gets a new one.
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
            // Unity writes the build number as a literal, and game-ci doesn't pass our environment into
            // its container, so every upload was build 1. Let the archive step's
            // CURRENT_PROJECT_VERSION (the workflow run number) fill it in instead.
            plist.root.SetString("CFBundleVersion", "$(CURRENT_PROJECT_VERSION)");
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
