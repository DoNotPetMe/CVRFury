using UnityEditor;

namespace CVRFury.Builder
{
    /// <summary>Project-level CVRFury preferences, stored in EditorPrefs.</summary>
    internal static class CVRFurySettings
    {
        private const string VerboseKey = "CVRFury.VerboseLogging";
        private const string GeneratedFolderKey = "CVRFury.GeneratedFolder";

        public static bool VerboseLogging
        {
            get => EditorPrefs.GetBool(VerboseKey, false);
            set => EditorPrefs.SetBool(VerboseKey, value);
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
