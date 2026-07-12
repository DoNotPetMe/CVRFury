using UnityEditor;

namespace CVRFury.Builder
{
    /// <summary>Project-level CVRFury preferences, stored in EditorPrefs.</summary>
    internal static class CVRFurySettings
    {
        private const string VerboseKey = "CVRFury.VerboseLogging";
        private const string GeneratedFolderKey = "CVRFury.GeneratedFolder";
        private const string CleanMissingKey = "CVRFury.CleanMissingScriptsOnBuild";
        private const string BypassKey = "CVRFury.BypassUpload";

        public static bool VerboseLogging
        {
            get => EditorPrefs.GetBool(VerboseKey, false);
            set => EditorPrefs.SetBool(VerboseKey, value);
        }

        /// <summary>Diagnostic kill-switch: when on, the CCK build hook does nothing (the avatar uploads
        /// WITHOUT the CVRFury bake — toggles/sliders won't work in that upload). Lets a failing upload be
        /// split cleanly into "CVRFury's bake" vs "something else on the pre-build event".</summary>
        public static bool BypassUpload
        {
            get => EditorPrefs.GetBool(BypassKey, false);
            set => EditorPrefs.SetBool(BypassKey, value);
        }

        /// <summary>Strip broken/missing-script components from the avatar during the bake. On by
        /// default — VRChat avatars brought into CVR are full of inert VRChat components.</summary>
        public static bool CleanMissingScriptsOnBuild
        {
            get => EditorPrefs.GetBool(CleanMissingKey, true);
            set => EditorPrefs.SetBool(CleanMissingKey, value);
        }

        /// <summary>Where transient generated assets (controllers, clips) are written during a
        /// build. Kept under Assets so Unity can include them in the bundle; cleaned afterwards.</summary>
        public static string GeneratedFolder
        {
            get => EditorPrefs.GetString(GeneratedFolderKey, "Assets/_CVRFury/Generated");
            set => EditorPrefs.SetString(GeneratedFolderKey, value);
        }
    }
}
