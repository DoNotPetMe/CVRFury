using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Turns "type the exact blendshape name" into "pick it from a dropdown" wherever a
    /// CVRFury component needs a blendshape name. Reads the names straight off the renderer's
    /// <see cref="Mesh"/>, so there is nothing to get wrong (typos, case, trailing spaces).
    /// Falls back to a plain text field when no mesh is assigned yet, or the mesh has no
    /// blendshapes, so nothing is ever blocked on this being perfect.
    /// </summary>
    internal static class BlendshapeUtil
    {
        public static string[] GetBlendShapeNames(SkinnedMeshRenderer smr)
        {
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null || mesh.blendShapeCount == 0) return System.Array.Empty<string>();
            var names = new string[mesh.blendShapeCount];
            for (var i = 0; i < names.Length; i++) names[i] = mesh.GetBlendShapeName(i);
            return names;
        }

        /// <summary>
        /// Draw a single-line blendshape field: a dropdown populated from <paramref name="renderer"/>'s
        /// mesh when one is assigned and has blendshapes, otherwise a plain text field (so a not-yet-
        /// assigned renderer, or a name typed before the renderer was set, never gets silently wiped).
        /// </summary>
        public static void Field(Rect rect, SkinnedMeshRenderer renderer, SerializedProperty nameProp,
                                 GUIContent label)
        {
            var names = GetBlendShapeNames(renderer);
            if (names.Length == 0)
            {
                var hint = renderer != null ? label.text + " (mesh has no blendshapes — typed)" : label.text;
                EditorGUI.PropertyField(rect, nameProp, new GUIContent(hint, label.tooltip));
                return;
            }

            var current = nameProp.stringValue;
            var idx = System.Array.IndexOf(names, current);

            // Always keep a "Custom…" slot at the end so an existing/typo'd/renamed value is never
            // silently discarded — picking it drops back to a manual text field for one edit.
            var options = names.Append("Custom…").ToArray();
            var displaySelection = idx >= 0 ? idx : options.Length - 1;

            EditorGUI.BeginChangeCheck();
            var chosen = EditorGUI.Popup(rect, label.text, displaySelection, options);
            if (EditorGUI.EndChangeCheck())
            {
                if (chosen < names.Length) nameProp.stringValue = names[chosen];
                // If "Custom…" was chosen, leave the existing string alone — the next redraw (once
                // idx < 0 again) will show the popup on "Custom…" and the value stays user-editable
                // via the fallback text field below.
            }

            if (idx < 0 && !string.IsNullOrEmpty(current))
            {
                var textRect = new Rect(rect.x, rect.y + rect.height + 2f, rect.width, rect.height);
                EditorGUI.PropertyField(textRect, nameProp, new GUIContent("  ↳ Custom name"));
            }
        }

        /// <summary>Extra row height needed under <see cref="Field"/> for its "custom name" fallback
        /// line, or 0 if the current value is a recognised blendshape (no extra row shown).</summary>
        public static float ExtraHeight(SkinnedMeshRenderer renderer, SerializedProperty nameProp, float lineHeight)
        {
            var names = GetBlendShapeNames(renderer);
            if (names.Length == 0) return 0f; // falls back to a single plain field, no extra row
            var current = nameProp.stringValue;
            return System.Array.IndexOf(names, current) < 0 && !string.IsNullOrEmpty(current) ? lineHeight + 2f : 0f;
        }
    }
}
