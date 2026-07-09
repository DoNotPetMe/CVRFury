using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Shows only the fields relevant to the selected <see cref="FuryAction.ActionType"/>,
    /// so the toggle "action" UI stays readable instead of dumping every possible field.</summary>
    [CustomPropertyDrawer(typeof(FuryAction))]
    public class FuryActionDrawer : PropertyDrawer
    {
        private const float Line = 18f;
        private const float Pad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var r = new Rect(position.x, position.y, position.width, Line);

            var typeProp = property.FindPropertyRelative("type");
            EditorGUI.PropertyField(r, typeProp, new GUIContent("Action"));
            r.y += Line + Pad;

            var type = (FuryAction.ActionType)typeProp.enumValueIndex;
            switch (type)
            {
                case FuryAction.ActionType.ObjectToggle:
                    Field(ref r, property, "targetObject", "Object");
                    Field(ref r, property, "targetState", "Active");
                    break;
                case FuryAction.ActionType.BlendShape:
                    Field(ref r, property, "blendShapeRenderer", "Renderer");
                    BlendshapeField(ref r, property);
                    Field(ref r, property, "blendShapeValue", "Value");
                    break;
                case FuryAction.ActionType.MaterialSwap:
                    Field(ref r, property, "materialRenderer", "Renderer");
                    Field(ref r, property, "materialSlot", "Slot");
                    Field(ref r, property, "material", "Material");
                    break;
                case FuryAction.ActionType.ScaleFactor:
                    Field(ref r, property, "scaleTarget", "Target");
                    Field(ref r, property, "scaleFactor", "Factor");
                    break;
                case FuryAction.ActionType.MaterialProperty:
                    Field(ref r, property, "propertyRenderer", "Renderer");
                    Field(ref r, property, "propertyName", "Property");
                    Field(ref r, property, "propertyIsColor", "Is Color");
                    Field(ref r, property,
                        property.FindPropertyRelative("propertyIsColor").boolValue ? "propertyColor" : "propertyValue",
                        "Value");
                    break;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var type = (FuryAction.ActionType)property.FindPropertyRelative("type").enumValueIndex;
            var rows = type switch
            {
                FuryAction.ActionType.ObjectToggle => 2,
                FuryAction.ActionType.BlendShape => 3,
                FuryAction.ActionType.MaterialSwap => 3,
                FuryAction.ActionType.ScaleFactor => 2,
                FuryAction.ActionType.MaterialProperty => 4,
                _ => 0,
            };
            return (rows + 1) * (Line + Pad);
        }

        private static void Field(ref Rect r, SerializedProperty parent, string name, string label)
        {
            EditorGUI.PropertyField(r, parent.FindPropertyRelative(name), new GUIContent(label));
            r.y += Line + Pad;
        }

        /// <summary>Blendshape as a pick-from-the-mesh dropdown once a renderer is assigned (falls back to a
        /// text field when there's no mesh), so nobody has to type shapekey names by hand.</summary>
        private static void BlendshapeField(ref Rect r, SerializedProperty parent)
        {
            var shapeProp = parent.FindPropertyRelative("blendShape");
            var rendererProp = parent.FindPropertyRelative("blendShapeRenderer");
            var smr = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            var mesh = smr != null ? smr.sharedMesh : null;

            if (mesh == null || mesh.blendShapeCount == 0)
            {
                EditorGUI.PropertyField(r, shapeProp, new GUIContent("Blendshape"));
            }
            else
            {
                var names = new string[mesh.blendShapeCount];
                var current = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = mesh.GetBlendShapeName(i);
                    if (names[i] == shapeProp.stringValue) current = i;
                }
                var picked = EditorGUI.Popup(r, "Blendshape", current, names);
                if (names[picked] != shapeProp.stringValue) shapeProp.stringValue = names[picked];
            }
            r.y += Line + Pad;
        }
    }
}
