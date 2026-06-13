using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Top-level menu commands. The main one is a non-destructive test bake: it clones
    /// the selected avatar, runs the full CVRFury pipeline on the clone, and leaves it in the
    /// scene so you can inspect/animate the result without uploading.</summary>
    internal static class CVRFuryMenu
    {
        [MenuItem("Tools/CVRFury/Test Bake Selected Avatar (clone)", true)]
        private static bool ValidateTestBake() =>
            Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponentInChildren<CVRFuryComponent>(true) != null;

        [MenuItem("Tools/CVRFury/Test Bake Selected Avatar (clone)")]
        private static void TestBake()
        {
            var src = Selection.activeGameObject;
            if (src == null) return;

            var clone = Object.Instantiate(src);
            clone.name = src.name + " (CVRFury Bake)";
            Undo.RegisterCreatedObjectUndo(clone, "CVRFury Test Bake");

            var did = CVRFuryBuilder.Run(clone, BuildTrigger.ManualBake);
            if (!did)
            {
                Object.DestroyImmediate(clone);
                EditorUtility.DisplayDialog("CVRFury",
                    "Nothing to bake — the selection has no CVRFury components, or no CVRAvatar / CCK present.",
                    "OK");
                return;
            }

            src.SetActive(false); // hide the source so you only see the baked result
            Selection.activeGameObject = clone;
        }

        [MenuItem("Tools/CVRFury/Verbose Logging", false, 100)]
        private static void ToggleVerbose() => CVRFurySettings.VerboseLogging = !CVRFurySettings.VerboseLogging;

        [MenuItem("Tools/CVRFury/Verbose Logging", true)]
        private static bool ToggleVerboseValidate()
        {
            Menu.SetChecked("Tools/CVRFury/Verbose Logging", CVRFurySettings.VerboseLogging);
            return true;
        }
    }
}
