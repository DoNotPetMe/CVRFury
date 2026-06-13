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

        [MenuItem("Tools/CVRFury/Diagnose CCK Integration", false, 50)]
        private static void DiagnoseCck()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== CVRFury ▸ CCK Integration Diagnosis ===");

            var avatarType = Reflect.FindType(CckNames.AvatarType);
            sb.AppendLine($"CVRAvatar type ('{CckNames.AvatarType}'): {(avatarType != null ? "FOUND (" + avatarType.FullName + ")" : "NOT FOUND")}");

            var buildType = Reflect.FindType(CckNames.BuildUtilityType);
            sb.AppendLine($"Build utility ('{CckNames.BuildUtilityType}'): {(buildType != null ? "FOUND" : "NOT FOUND — will broad-scan")}");

            sb.AppendLine();
            sb.AppendLine("Pre-bundle events discovered (static UnityEvent<GameObject> on CCK types):");
            var events = CckProbe.Discover(broadScan: true);
            if (events.Count == 0)
            {
                sb.AppendLine("  (none) — CVRFury cannot hook this CCK version automatically.");
                sb.AppendLine("  Please share this output so the hook names can be updated.");
            }
            else
            {
                foreach (var e in events)
                {
                    var role = e.IsAvatar ? "AVATAR" : e.IsProp ? "PROP" : "unclassified";
                    sb.AppendLine($"  [{role}] {e.TypeName}.{e.MemberName}  (instance: {(e.EventInstance != null ? "ok" : "null")})");
                }
            }

            sb.AppendLine();
            sb.AppendLine("If an event is mis-classified or missing, paste this to the CVRFury maintainer.");
            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("CVRFury", "CCK diagnosis written to the Console.", "OK");
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
