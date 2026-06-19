using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Shared inspector for every CVRFury feature: a titled banner plus the feature's
    /// own fields. Applies to all subclasses via <c>editorForChildClasses</c>.</summary>
    [CustomEditor(typeof(CVRFuryComponent), editorForChildClasses: true)]
    [CanEditMultipleObjects]
    public class CVRFuryComponentEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var feature = (CVRFuryComponent)target;

            DrawBanner(feature.FeatureTitle);

            serializedObject.Update();
            var prop = serializedObject.GetIterator();
            var enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script" || prop.name == "schemaVersion") continue;
                if (prop.name == "menuPath") { DrawMenuPathField(prop, feature); continue; }
                EditorGUILayout.PropertyField(prop, true);
            }
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "CVRFury features are applied automatically when you upload through the ChilloutVR " +
                "CCK. Use Tools ▸ CVRFury ▸ Test Bake to preview the result without uploading.",
                MessageType.None);
        }

        /// <summary>Menu Path is just the display label in CVR's flat Advanced Settings list. Draw it with a
        /// dropdown that fills in the object name or a label already used elsewhere on the avatar.</summary>
        private void DrawMenuPathField(SerializedProperty prop, CVRFuryComponent feature)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(prop, new GUIContent("Menu Label", prop.tooltip));
                if (GUILayout.Button(new GUIContent("▾", "Pick a label"), EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    var menu = new GenericMenu();
                    var objName = feature.gameObject.name;
                    menu.AddItem(new GUIContent($"Use object name (\"{objName}\")"), false, () => SetMenuPath(""));
                    var labels = CollectExistingLabels(feature);
                    if (labels.Count > 0) menu.AddSeparator("");
                    foreach (var l in labels)
                    {
                        var captured = l;
                        menu.AddItem(new GUIContent("Used elsewhere/" + l), false, () => SetMenuPath(captured));
                    }
                    menu.ShowAsContext();
                }
            }
        }

        private void SetMenuPath(string value)
        {
            foreach (var t in targets)
            {
                Undo.RecordObject(t, "Set menu label");
                var f = t.GetType().GetField("menuPath", BindingFlags.Public | BindingFlags.Instance);
                f?.SetValue(t, value);
                EditorUtility.SetDirty(t);
            }
        }

        private static List<string> CollectExistingLabels(CVRFuryComponent self)
        {
            var root = self.transform.root.gameObject;
            var labels = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in root.GetComponentsInChildren<CVRFuryComponent>(true))
            {
                if (c == self) continue;
                var f = c.GetType().GetField("menuPath", BindingFlags.Public | BindingFlags.Instance);
                if (f?.GetValue(c) is string s && !string.IsNullOrWhiteSpace(s)) labels.Add(s);
            }
            return labels.ToList();
        }

        private static void DrawBanner(string title)
        {
            var rect = EditorGUILayout.GetControlRect(false, 28);
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.10f, 0.22f));
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.86f, 0.74f, 0.95f) },
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0),
            };
            GUI.Label(rect, $"CVRFury — {title}", style);
            EditorGUILayout.Space(2);
        }
    }
}
