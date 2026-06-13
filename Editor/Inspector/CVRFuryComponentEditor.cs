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
                EditorGUILayout.PropertyField(prop, true);
            }
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "CVRFury features are applied automatically when you upload through the ChilloutVR " +
                "CCK. Use Tools ▸ CVRFury ▸ Test Bake to preview the result without uploading.",
                MessageType.None);
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
