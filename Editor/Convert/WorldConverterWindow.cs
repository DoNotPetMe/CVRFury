using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Tools ▸ CVRFury ▸ World Converter: VRChat scene → ChilloutVR world.
    ///
    /// The workflow mirrors what made the avatar side dependable: one primary button that is safe by
    /// construction — "Convert &amp; Verify" duplicates the scene asset, opens the COPY, converts it there,
    /// and pre-flights the result — plus manual steps for people who want control, a scan that turns the Udon
    /// inventory into a migration plan with per-item CVR recipes, and a pre-flight that catches the classic
    /// world-breakers (no floor under spawns, spawns below respawn height, VRChat leftovers, dark lighting)
    /// before an upload is wasted. The original scene is never modified by the one-click path.
    /// </summary>
    internal sealed class WorldConverterWindow : EditorWindow
    {
        private string _report = "";
        private bool _strip = true;
        private bool _rebuildToggles = true;
        private Vector2 _scroll;

        [MenuItem("Tools/CVRFury/World Converter (Beta)", false, 2)]
        public static void Open()
        {
            var w = GetWindow<WorldConverterWindow>("CVR World Converter");
            w.minSize = new Vector2(460, 420);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.145f, 0.135f, 0.16f));
            DrawBanner();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Box("One-click: duplicates the open scene, converts the COPY to a ChilloutVR world (spawns/camera/" +
                "respawn → CVRWorld; mirrors, pickups, chairs, synced objects, video players, portals → their " +
                "CVR components), strips the VRChat/Udon layer, and pre-flights the result. Your original " +
                "scene is untouched.");

            _strip = EditorGUILayout.ToggleLeft(new GUIContent(
                "Strip VRChat + Udon components after converting",
                "Their scripts don't exist in CVR, so leftovers break the CCK build. Recommended ON."), _strip);
            _rebuildToggles = EditorGUILayout.ToggleLeft(new GUIContent(
                "Rebuild toggle-style Udon buttons as CVRInteractables",
                "Reads each recognised button's target objects out of its Udon variables and wires a " +
                "ready-made CVRInteractable (Set GameObject Active) on the same object. Runs BEFORE the " +
                "strip, so the data is read while it still exists."), _rebuildToggles);

            if (GUILayout.Button("✨ Convert & Verify  (works on a copy)", GUILayout.Height(30)))
                RunSafe(ConvertAndVerify);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("… or step by step on the OPEN scene:", EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan (read-only migration plan)"))
                    RunSafe(WorldConverter.Scan);
                if (GUILayout.Button("Pre-flight check"))
                    RunSafe(() => WorldPreflight.Report(out _));
            }
            if (GUILayout.Button("Convert this scene in place"))
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (EditorUtility.DisplayDialog("CVRFury — Convert world",
                    $"Convert the open scene '{scene.name}' to a ChilloutVR world IN PLACE?\n\nUse Convert & " +
                    "Verify above if you want the automatic scene copy instead.", "Convert", "Cancel"))
                    RunSafe(() =>
                    {
                        var result = WorldConverter.Convert(_strip, _rebuildToggles);
                        EditorSceneManager.MarkSceneDirty(scene);
                        return result;
                    });
            }

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                        EditorGUIUtility.systemCopyBuffer = _report;
                    if (GUILayout.Button("Save .md", EditorStyles.miniButton, GUILayout.Width(70)))
                        SaveReport();
                }
                EditorGUILayout.TextArea(_report, GUILayout.MinHeight(140));
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>The safe path: copy the scene asset, open the copy, convert there, pre-flight, save.</summary>
        private string ConvertAndVerify()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
                return "Save the scene first (File ▸ Save) so there is an asset to copy.";
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
                return "Couldn't save the open scene — save manually, then retry.";

            var dir = System.IO.Path.GetDirectoryName(scene.path)?.Replace('\\', '/');
            var copyPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{scene.name} (CVR).unity");
            if (!AssetDatabase.CopyAsset(scene.path, copyPath))
                return "Couldn't duplicate the scene asset.";

            var copy = EditorSceneManager.OpenScene(copyPath, OpenSceneMode.Single);
            var log = WorldConverter.Convert(_strip, _rebuildToggles);
            EditorSceneManager.MarkSceneDirty(copy);
            EditorSceneManager.SaveScene(copy);
            var pre = WorldPreflight.Report(out var ok);

            return $"Converted a COPY: {copyPath}\n(original untouched: {scene.path})\n\n{log}\n\n{pre}" +
                   (ok ? "\n\nUpload this scene through the CCK's world flow." : "");
        }

        private void SaveReport()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var dir = string.IsNullOrEmpty(scene.path) ? "Assets" : System.IO.Path.GetDirectoryName(scene.path);
            var path = EditorUtility.SaveFilePanel("Save migration report", dir, $"{scene.name} CVR migration", "md");
            if (string.IsNullOrEmpty(path)) return;
            System.IO.File.WriteAllText(path,
                $"# {scene.name} — VRChat → ChilloutVR migration report\n\n```\n{_report}\n```\n");
            AssetDatabase.Refresh();
        }

        private void RunSafe(System.Func<string> op)
        {
            try { _report = op(); }
            catch (System.Exception ex) { _report = "Error: " + ex.Message; Debug.LogException(ex); }
            Repaint();
        }

        // --- chrome (matches the main CVRFury window) ---------------------------------------------

        private void DrawBanner()
        {
            var rect = new Rect(0, 0, position.width, 44);
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.09f, 0.20f));
            EditorGUI.DrawRect(new Rect(0, 43, position.width, 2), new Color(0.45f, 0.30f, 0.55f));
            var title = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.86f, 0.74f, 0.95f) }, fontSize = 16,
                alignment = TextAnchor.LowerLeft, padding = new RectOffset(12, 0, 0, 2),
            };
            GUI.Label(new Rect(0, 2, position.width, 24), "✦ World Converter", title);
            var sub = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.62f, 0.54f, 0.70f) },
                alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 0, 0, 0),
            };
            GUI.Label(new Rect(0, 24, position.width, 18), "VRChat scene → ChilloutVR world", sub);
            var ver = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.65f, 0.55f, 0.72f) },
                alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 12, 0, 0),
            };
            GUI.Label(rect, $"v{CckNames.CvrFuryVersion}", ver);
            GUILayout.Space(48);
        }

        private static void Box(string text)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, fontSize = 11,
                normal = { textColor = new Color(0.82f, 0.80f, 0.88f) }, padding = new RectOffset(10, 8, 6, 6),
            };
            var content = new GUIContent(text);
            var rect = GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.195f, 0.165f, 0.235f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), new Color(0.52f, 0.37f, 0.62f));
            GUI.Label(rect, content, style);
            EditorGUILayout.Space(2);
        }
    }
}
