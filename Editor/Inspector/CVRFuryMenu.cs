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

        [MenuItem("Tools/CVRFury/Clean Missing Scripts on Selected", true)]
        private static bool ValidateCleanMissing() => Selection.activeGameObject != null;

        /// <summary>
        /// One-click removal of broken/missing-script components — the components a VRChat avatar
        /// brings into CVR that show as "The associated script can not be loaded". Prefab-aware: it
        /// offers to fix the prefab asset permanently rather than just patching the instance.
        /// </summary>
        [MenuItem("Tools/CVRFury/Clean Missing Scripts on Selected", false, 20)]
        private static void CleanMissingScripts()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;

            var found = MissingScriptCleaner.CountInHierarchy(go);
            if (found == 0)
            {
                EditorUtility.DisplayDialog("CVRFury", $"No missing scripts found on '{go.name}'.", "OK");
                return;
            }

            // If this is a prefab, the permanent fix is to clean the asset itself.
            var assetPath = ResolvePrefabAssetPath(go);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var cleanAsset = EditorUtility.DisplayDialogComplex("CVRFury",
                    $"Found {found} missing-script component(s). '{go.name}' is a prefab.\n\n" +
                    "Clean the prefab ASSET (permanent, recommended) or just this scene instance?",
                    "Clean Prefab Asset", "Cancel", "Clean Instance Only");

                if (cleanAsset == 1) return; // Cancel

                if (cleanAsset == 0)
                {
                    var root = PrefabUtility.LoadPrefabContents(assetPath);
                    var removedAsset = MissingScriptCleaner.RemoveInHierarchy(root);
                    PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    PrefabUtility.UnloadPrefabContents(root);
                    AssetDatabase.SaveAssets();
                    EditorUtility.DisplayDialog("CVRFury",
                        $"Removed {removedAsset} missing-script component(s) from the prefab asset.", "OK");
                    return;
                }
            }

            // Plain scene object, or "instance only" choice.
            Undo.RegisterFullObjectHierarchyUndo(go, "CVRFury Clean Missing Scripts");
            var removed = MissingScriptCleaner.RemoveInHierarchy(go);
            EditorUtility.SetDirty(go);
            EditorUtility.DisplayDialog("CVRFury",
                $"Removed {removed} missing-script component(s) from '{go.name}'.", "OK");
        }

        private static string ResolvePrefabAssetPath(GameObject go)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(go))
                return AssetDatabase.GetAssetPath(go);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return null;
        }

        [MenuItem("Tools/CVRFury/Auto-Clean Missing Scripts on Build", false, 101)]
        private static void ToggleAutoClean() =>
            CVRFurySettings.CleanMissingScriptsOnBuild = !CVRFurySettings.CleanMissingScriptsOnBuild;

        [MenuItem("Tools/CVRFury/Auto-Clean Missing Scripts on Build", true)]
        private static bool ToggleAutoCleanValidate()
        {
            Menu.SetChecked("Tools/CVRFury/Auto-Clean Missing Scripts on Build",
                CVRFurySettings.CleanMissingScriptsOnBuild);
            return true;
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
            sb.Append(CckProbe.ValidateDataModel());

            sb.AppendLine();
            sb.Append(CckProbe.DumpModel());

            sb.AppendLine();
            sb.AppendLine("Paste the entire output (especially the 'Raw CCK type dump') to the CVRFury maintainer.");
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

        [MenuItem("Tools/CVRFury/Bypass CVRFury At Upload (diagnostic)", false, 102)]
        private static void ToggleBypass()
        {
            CVRFurySettings.BypassUpload = !CVRFurySettings.BypassUpload;
            if (CVRFurySettings.BypassUpload)
                Debug.LogWarning("[CVRFury] Upload bypass is ON — avatars upload WITHOUT the CVRFury bake " +
                                 "(no toggles/sliders in those uploads). Use it to diagnose build failures, " +
                                 "then turn it back off.");
        }

        [MenuItem("Tools/CVRFury/Bypass CVRFury At Upload (diagnostic)", true)]
        private static bool ToggleBypassValidate()
        {
            Menu.SetChecked("Tools/CVRFury/Bypass CVRFury At Upload (diagnostic)", CVRFurySettings.BypassUpload);
            return true;
        }
    }
}
