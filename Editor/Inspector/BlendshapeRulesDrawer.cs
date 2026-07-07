using System.Linq;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Renderer + auto-detected blendshape dropdown + value, one row each, plus a
    /// one-click "make this its own toggle" shortcut.</summary>
    [CustomPropertyDrawer(typeof(CVRFuryBlendshapeRules.BlendshapeTarget))]
    public class BlendshapeTargetDrawer : PropertyDrawer
    {
        private const float Line = 18f;
        private const float Pad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var r = new Rect(position.x, position.y, position.width, Line);

            var rendererProp = property.FindPropertyRelative("renderer");
            EditorGUI.PropertyField(r, rendererProp, new GUIContent("Renderer"));
            r.y += Line + Pad;

            var renderer = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            var nameProp = property.FindPropertyRelative("blendShape");
            BlendshapeUtil.Field(r, renderer, nameProp, new GUIContent("Blendshape"));
            r.y += Line + Pad + BlendshapeUtil.ExtraHeight(renderer, nameProp, Line);

            EditorGUI.PropertyField(r, property.FindPropertyRelative("value"), new GUIContent("Value"));
            r.y += Line + Pad;

            if (GUI.Button(r, new GUIContent("Make Toggle From This Blendshape",
                    "Creates a separate CVRFury Toggle GameObject pre-filled with this exact " +
                    "renderer/blendshape/value, so it also gets its own on/off entry in the menu.")))
                MakeToggle(property, rendererProp);

            EditorGUI.EndProperty();
        }

        private static void MakeToggle(SerializedProperty property, SerializedProperty rendererProp)
        {
            var renderer = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            var shape = property.FindPropertyRelative("blendShape").stringValue;
            var value = property.FindPropertyRelative("value").floatValue;
            if (renderer == null || string.IsNullOrEmpty(shape))
            {
                EditorUtility.DisplayDialog("CVRFury", "Pick a renderer and blendshape first.", "OK");
                return;
            }

            var owner = (property.serializedObject.targetObject as Component)?.gameObject;
            if (owner == null) return;

            var go = new GameObject($"Toggle - {shape}");
            Undo.RegisterCreatedObjectUndo(go, "Make Toggle From Blendshape");
            go.transform.SetParent(owner.transform.parent, false);

            var toggle = go.AddComponent<CVRFuryToggle>();
            toggle.menuPath = shape;
            toggle.state.actions.Add(new FuryAction
            {
                type = FuryAction.ActionType.BlendShape,
                blendShapeRenderer = renderer,
                blendShape = shape,
                blendShapeValue = value,
            });

            Selection.activeGameObject = go;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var renderer = property.FindPropertyRelative("renderer").objectReferenceValue as SkinnedMeshRenderer;
            var extra = BlendshapeUtil.ExtraHeight(renderer, property.FindPropertyRelative("blendShape"), Line);
            return 4 * (Line + Pad) + extra;
        }
    }

    /// <summary>Toggle-or-Modes picker; shows only the fields relevant to the chosen kind, and
    /// offers the Modes' own option names as a dropdown instead of a raw index when possible.</summary>
    [CustomPropertyDrawer(typeof(CVRFuryBlendshapeRules.Condition))]
    public class BlendshapeConditionDrawer : PropertyDrawer
    {
        private const float Line = 18f;
        private const float Pad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var r = new Rect(position.x, position.y, position.width, Line);

            var kindProp = property.FindPropertyRelative("kind");
            EditorGUI.PropertyField(r, kindProp, new GUIContent("Condition"));
            r.y += Line + Pad;

            var kind = (CVRFuryBlendshapeRules.Condition.Kind)kindProp.enumValueIndex;
            if (kind == CVRFuryBlendshapeRules.Condition.Kind.Toggle)
            {
                EditorGUI.PropertyField(r, property.FindPropertyRelative("toggle"), new GUIContent("Toggle"));
                r.y += Line + Pad;
                EditorGUI.PropertyField(r, property.FindPropertyRelative("requiredOn"),
                    new GUIContent("Required: On"));
            }
            else
            {
                var modesProp = property.FindPropertyRelative("modes");
                EditorGUI.PropertyField(r, modesProp, new GUIContent("Modes"));
                r.y += Line + Pad;

                var indexProp = property.FindPropertyRelative("modeIndex");
                var modesComponent = modesProp.objectReferenceValue as CVRFuryModes;
                if (modesComponent != null && modesComponent.modes != null && modesComponent.modes.Count > 0)
                {
                    var names = modesComponent.modes
                        .Select((m, i) => string.IsNullOrEmpty(m.name) ? $"Mode {i}" : m.name)
                        .ToArray();
                    indexProp.intValue = EditorGUI.Popup(r, "Required Mode",
                        Mathf.Clamp(indexProp.intValue, 0, names.Length - 1), names);
                }
                else
                {
                    EditorGUI.PropertyField(r, indexProp, new GUIContent("Mode Index"));
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            2 * (Line + Pad);
    }
}
