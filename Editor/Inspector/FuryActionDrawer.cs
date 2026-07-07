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
                    BlendShapeField(ref r, property);
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
            var height = (rows + 1) * (Line + Pad);

            if (type == FuryAction.ActionType.BlendShape)
            {
                var renderer = property.FindPropertyRelative("blendShapeRenderer").objectReferenceValue as SkinnedMeshRenderer;
                height += BlendshapeUtil.ExtraHeight(renderer, property.FindPropertyRelative("blendShape"), Line);
            }

            return height;
        }

        private static void Field(ref Rect r, SerializedProperty parent, string name, string label)
        {
            EditorGUI.PropertyField(r, parent.FindPropertyRelative(name), new GUIContent(label));
            r.y += Line + Pad;
        }

        /// <summary>Blendshape name as an auto-detected dropdown (see <see cref="BlendshapeUtil"/>)
        /// instead of a plain typed field, advancing <paramref name="r"/> by however many rows it drew.</summary>
        private static void BlendShapeField(ref Rect r, SerializedProperty property)
        {
            var renderer = property.FindPropertyRelative("blendShapeRenderer").objectReferenceValue as SkinnedMeshRenderer;
            var nameProp = property.FindPropertyRelative("blendShape");
            BlendshapeUtil.Field(r, renderer, nameProp, new GUIContent("Blendshape"));
            r.y += Line + Pad + BlendshapeUtil.ExtraHeight(renderer, nameProp, Line);
        }
    }
}
