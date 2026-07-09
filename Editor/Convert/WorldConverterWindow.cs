using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Tools ▸ CVRFury ▸ World Converter (Beta): scan the open VRChat world scene, see exactly what will
    /// and won't convert, then convert in place (with a save-a-copy guard). The structural layer converts
    /// today; the Udon inventory it prints is the foundation the interactivity layer builds on next.
    /// </summary>
    internal sealed class WorldConverterWindow : EditorWindow
    {
        private string _report = "";
        private bool _strip = true;
        private Vector2 _scroll;

        [MenuItem("Tools/CVRFury/World Converter (Beta)", false, 2)]
        public static void Open()
        {
            var w = GetWindow<WorldConverterWindow>("CVR World Converter");
            w.minSize = new Vector2(420, 320);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.145f, 0.135f, 0.16f));
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("✦ World Converter — VRChat scene → ChilloutVR (Beta)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Works on the OPEN scene. Converts the structural layer today — spawns/camera/respawn " +
                "(CVRWorld), mirrors, pickups, chairs — and inventories every Udon behaviour so you can see " +
                "the interactivity surface. Udon → CVR interactables/scripting is the next layer.\n\n" +
                "Save a COPY of the scene first (File ▸ Save As) and convert the copy.",
                MessageType.Info);

            if (GUILayout.Button("Scan scene (read-only)"))
            {
                try { _report = WorldConverter.Scan(); }
                catch (System.Exception ex) { _report = "Error: " + ex.Message; Debug.LogException(ex); }
            }

            _strip = EditorGUILayout.ToggleLeft(new GUIContent(
                "Strip VRChat + Udon components after converting",
                "Removes VRC.*/Udon components and missing scripts so the CCK can build the scene."), _strip);

            if (GUILayout.Button("Convert this scene"))
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (EditorUtility.DisplayDialog("CVRFury — Convert world",
                    $"Convert the open scene '{scene.name}' to a ChilloutVR world?\n\nThis edits the scene in " +
                    "place (undoable, but work on a saved COPY). The ChilloutVR CCK with world support must be " +
                    "imported.", "Convert", "Cancel"))
                {
                    try { _report = WorldConverter.Convert(_strip); }
                    catch (System.Exception ex) { _report = "Error: " + ex.Message; Debug.LogException(ex); }
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space(4);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
