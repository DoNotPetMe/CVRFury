using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Inspector for Blendshape Logic: auto-detects the mesh, shows the rules (with blendshapes
    /// picked from a dropdown + a value slider via <see cref="BlendshapeAssignmentDrawer"/>), and
    /// offers one-click "create a toggle from a blendshape".
    /// </summary>
    [CustomEditor(typeof(CVRFuryBlendshapeLogic))]
    public class BlendshapeLogicEditor : CVRFuryComponentEditor
    {
        private int _createShapeIndex;

        public override void OnInspectorGUI()
        {
            var logic = (CVRFuryBlendshapeLogic)target;
            DrawBanner(logic.FeatureTitle);

            serializedObject.Update();

            var meshProp = serializedObject.FindProperty("mesh");
            if (meshProp.objectReferenceValue == null)
                meshProp.objectReferenceValue = logic.GetComponent<SkinnedMeshRenderer>()
                    ?? logic.GetComponentInChildren<SkinnedMeshRenderer>(true);
            EditorGUILayout.PropertyField(meshProp);

            var names = ShapeNames(meshProp.objectReferenceValue as SkinnedMeshRenderer);
            if (names.Length == 0)
                EditorGUILayout.HelpBox("Assign a Skinned Mesh Renderer whose mesh has blendshapes " +
                                        "(auto-detected from this GameObject when possible).",
                                        MessageType.Info);
            else
                EditorGUILayout.LabelField($"Detected {names.Length} blendshape(s).", EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionSeconds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rules"), true);
            serializedObject.ApplyModifiedProperties();

            DrawCreateToggleSection(logic, names);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Each rule: when ALL its condition objects match (on/off), the blendshapes are set " +
                "to the chosen values; otherwise they return to their resting scene values. " +
                "Condition objects must be shown/hidden by a CVRFury Toggle on this avatar.",
                MessageType.None);
        }

        private void DrawCreateToggleSection(CVRFuryBlendshapeLogic logic, string[] names)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Create a toggle from a blendshape", EditorStyles.boldLabel);
            if (names.Length == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                _createShapeIndex = EditorGUILayout.Popup(
                    Mathf.Clamp(_createShapeIndex, 0, names.Length - 1), names);
                if (GUILayout.Button("Add Toggle", GUILayout.Width(90)))
                {
                    var shape = names[_createShapeIndex];
                    var toggle = Undo.AddComponent<CVRFuryToggle>(logic.gameObject);
                    toggle.menuPath = shape;
                    toggle.state.actions.Add(new FuryAction
                    {
                        type = FuryAction.ActionType.BlendShape,
                        blendShapeRenderer = logic.mesh,
                        blendShape = shape,
                        blendShapeValue = 100f,
                    });
                    EditorUtility.SetDirty(toggle);
                }
            }
        }

        internal static string[] ShapeNames(SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.sharedMesh == null) return new string[0];
            var names = new string[smr.sharedMesh.blendShapeCount];
            for (var i = 0; i < names.Length; i++)
                names[i] = smr.sharedMesh.GetBlendShapeName(i);
            return names;
        }
    }

    /// <summary>Draws one blendshape assignment as a dropdown of the mesh's detected blendshapes
    /// plus a 0–100 value slider (falls back to a text field when no mesh is set).</summary>
    [CustomPropertyDrawer(typeof(CVRFuryBlendshapeLogic.Assignment))]
    public class BlendshapeAssignmentDrawer : PropertyDrawer
    {
        private const float Line = 18f;
        private const float Pad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var shapeProp = property.FindPropertyRelative("blendshape");
            var valueProp = property.FindPropertyRelative("value");
            var logic = property.serializedObject.targetObject as CVRFuryBlendshapeLogic;
            var names = BlendshapeLogicEditor.ShapeNames(logic != null ? logic.mesh : null);

            var r = new Rect(position.x, position.y, position.width, Line);
            if (names.Length == 0)
            {
                EditorGUI.PropertyField(r, shapeProp, new GUIContent("Blendshape"));
            }
            else
            {
                var idx = System.Array.IndexOf(names, shapeProp.stringValue);
                if (idx < 0)
                {
                    // Keep an unknown/unset value visible without destroying it.
                    var display = new string[names.Length + 1];
                    display[0] = string.IsNullOrEmpty(shapeProp.stringValue)
                        ? "(choose blendshape)" : $"(missing) {shapeProp.stringValue}";
                    names.CopyTo(display, 1);
                    var picked = EditorGUI.Popup(r, "Blendshape", 0, display);
                    if (picked > 0) shapeProp.stringValue = names[picked - 1];
                }
                else
                {
                    var picked = EditorGUI.Popup(r, "Blendshape", idx, names);
                    shapeProp.stringValue = names[picked];
                }
            }

            r.y += Line + Pad;
            EditorGUI.Slider(r, valueProp, 0f, 100f, new GUIContent("Value"));

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            2 * (Line + Pad);
    }
}
