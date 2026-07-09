using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CVRFury.Components;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Builds native in-game scale sliders: a CVRFury Slider whose two endpoints scale a target between
    /// <c>min</c>× and <c>max</c>× its authored size. Uniform (1,1,1) = a "size" slider; a single axis
    /// (e.g. 0,0,1) = a "length" slider. Works in ChilloutVR with no contacts or special shaders — drag
    /// the slider in the Advanced Settings menu and the part grows/shrinks.
    /// </summary>
    internal static class AvatarSizeSlider
    {
        /// <summary>Whole-avatar uniform size slider (kept for the one-click convenience button).</summary>
        public static string Add(GameObject avatar, float min, float max)
        {
            if (avatar == null) return "Select your avatar first.";
            var msg = AddSlider(avatar, new[] { avatar.transform }, Vector3.one, min, max, "Avatar Size");
            return msg ?? $"Added an \"Avatar Size\" slider ({min:0.##}× – {max:0.##}×), defaulting to normal size.";
        }

        /// <summary>Add (or replace) one scale slider named <paramref name="label"/> that scales ALL the given
        /// <paramref name="targets"/> equally on <paramref name="axes"/> between min× and max× — so a single
        /// slider can drive a symmetric pair (left + right). Returns an error string, or null on success.</summary>
        public static string AddSlider(GameObject avatar, IEnumerable<Transform> targets, Vector3 axes, float min, float max, string label)
        {
            if (avatar == null) return "Select your avatar first.";
            var list = targets?.Where(t => t != null).ToList() ?? new List<Transform>();
            if (list.Count == 0) return $"'{label}': no target object set.";
            if (max <= min) return $"'{label}': max must be larger than min.";
            if (axes == Vector3.zero) return $"'{label}': no axis selected.";

            // Replace a previous slider with the same label so re-running doesn't stack duplicates.
            foreach (var existing in avatar.GetComponents<CVRFurySlider>())
                if (existing.menuPath == label) Object.DestroyImmediate(existing);

            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = label;
            slider.saved = true;
            slider.localOnly = false;
            foreach (var t in list)
            {
                slider.minState.actions.Add(ScaleAction(t, axes, min));
                slider.maxState.actions.Add(ScaleAction(t, axes, max));
            }
            // Default so the part loads at its normal (1×) size: the value where min..max == 1.
            slider.defaultValue = Mathf.Clamp01((1f - min) / (max - min));

            EditorUtility.SetDirty(avatar);
            return null;
        }

        /// <summary>Add (or replace) a slider named <paramref name="label"/> that drives a material float
        /// property (e.g. a hue shift or emission strength) on <paramref name="r"/> between min and max. The
        /// slider loads at min. Returns an error string, or null on success.</summary>
        public static string AddMaterialSlider(GameObject avatar, IEnumerable<Renderer> renderers, string property, float min, float max, string label)
        {
            if (avatar == null) return "Select your avatar first.";
            var list = renderers?.Where(r => r != null).ToList() ?? new List<Renderer>();
            if (list.Count == 0) return $"'{label}': no mesh/renderer set.";
            if (string.IsNullOrEmpty(property)) return $"'{label}': no shader property set.";
            if (max <= min) return $"'{label}': max must be larger than min.";

            // Animating a property the material doesn't expose silently does nothing in game — the #1 cause
            // of "my hue slider doesn't work". Validate up front, with the locked-Poiyomi case called out.
            foreach (var r in list)
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.HasProperty(property)) continue;
                    bool locked = m.shader.name.StartsWith("Hidden/Locked") || m.shader.name.StartsWith("Locked/");
                    return locked
                        ? $"'{label}': material '{m.name}' is LOCKED (Poiyomi) and '{property}' was baked away. " +
                          "Unlock the material, right-click the property in the shader UI and set it to " +
                          "\"Animated\", re-lock, then create the slider again."
                        : $"'{label}': material '{m.name}' ({m.shader.name}) has no property '{property}' — the " +
                          "slider would do nothing. Check the exact property name in the shader.";
                }

            foreach (var existing in avatar.GetComponents<CVRFurySlider>())
                if (existing.menuPath == label) Object.DestroyImmediate(existing);

            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = label;
            slider.saved = true;
            foreach (var r in list)
            {
                slider.minState.actions.Add(MaterialAction(r, property, min));
                slider.maxState.actions.Add(MaterialAction(r, property, max));
            }
            slider.defaultValue = 0f; // load at min (e.g. no hue shift)

            EditorUtility.SetDirty(avatar);
            return null;
        }

        /// <summary>Add (or replace) a slider that drives a blendshape (0..100) on the given meshes — e.g. a
        /// body-size or expression slider straight from the mesh's shapekeys.</summary>
        public static string AddBlendshapeSlider(GameObject avatar, IEnumerable<SkinnedMeshRenderer> smrs,
                                                 string shape, float min, float max, string label)
        {
            if (avatar == null) return "Select your avatar first.";
            var list = smrs?.Where(s => s != null).ToList() ?? new List<SkinnedMeshRenderer>();
            if (list.Count == 0) return $"'{label}': no mesh set.";
            if (string.IsNullOrEmpty(shape)) return $"'{label}': pick a blendshape.";
            if (max <= min) return $"'{label}': max must be larger than min.";

            foreach (var existing in avatar.GetComponents<CVRFurySlider>())
                if (existing.menuPath == label) Object.DestroyImmediate(existing);

            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = label;
            slider.saved = true;
            foreach (var smr in list)
            {
                slider.minState.actions.Add(BlendAction(smr, shape, min));
                slider.maxState.actions.Add(BlendAction(smr, shape, max));
            }
            slider.defaultValue = 0f; // load at min
            EditorUtility.SetDirty(avatar);
            return null;
        }

        private static FuryAction BlendAction(SkinnedMeshRenderer smr, string shape, float value) => new FuryAction
        {
            type = FuryAction.ActionType.BlendShape,
            blendShapeRenderer = smr,
            blendShape = shape,
            blendShapeValue = value,
        };

        private static FuryAction MaterialAction(Renderer r, string property, float value) => new FuryAction
        {
            type = FuryAction.ActionType.MaterialProperty,
            propertyRenderer = r,
            propertyName = property,
            propertyIsColor = false,
            propertyValue = value,
        };

        private static FuryAction ScaleAction(Transform target, Vector3 axes, float factor) => new FuryAction
        {
            type = FuryAction.ActionType.ScaleFactor,
            scaleTarget = target,
            scaleFactor = factor,
            scaleAxes = axes,
        };
    }
}
