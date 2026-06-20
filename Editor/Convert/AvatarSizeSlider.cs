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
            var msg = AddSlider(avatar, avatar.transform, Vector3.one, min, max, "Avatar Size");
            return msg ?? $"Added an \"Avatar Size\" slider ({min:0.##}× – {max:0.##}×), defaulting to normal size.";
        }

        /// <summary>Add (or replace) a scale slider named <paramref name="label"/> that scales
        /// <paramref name="target"/> on the given <paramref name="axes"/> between min× and max×. The slider
        /// component lives on the avatar so it shows in the menu; the animation drives the target. Returns an
        /// error string, or null on success.</summary>
        public static string AddSlider(GameObject avatar, Transform target, Vector3 axes, float min, float max, string label)
        {
            if (avatar == null) return "Select your avatar first.";
            if (target == null) return $"'{label}': no target object set.";
            if (max <= min) return $"'{label}': max must be larger than min.";
            if (axes == Vector3.zero) return $"'{label}': no axis selected.";

            // Replace a previous slider with the same label so re-running doesn't stack duplicates.
            foreach (var existing in avatar.GetComponents<CVRFurySlider>())
                if (existing.menuPath == label) Object.DestroyImmediate(existing);

            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = label;
            slider.saved = true;
            slider.localOnly = false;
            slider.minState.actions.Add(ScaleAction(target, axes, min));
            slider.maxState.actions.Add(ScaleAction(target, axes, max));
            // Default so the part loads at its normal (1×) size: the value where min..max == 1.
            slider.defaultValue = Mathf.Clamp01((1f - min) / (max - min));

            EditorUtility.SetDirty(avatar);
            return null;
        }

        /// <summary>Add (or replace) a slider named <paramref name="label"/> that drives a material float
        /// property (e.g. a hue shift or emission strength) on <paramref name="r"/> between min and max. The
        /// slider loads at min. Returns an error string, or null on success.</summary>
        public static string AddMaterialSlider(GameObject avatar, Renderer r, string property, float min, float max, string label)
        {
            if (avatar == null) return "Select your avatar first.";
            if (r == null) return $"'{label}': no mesh/renderer set.";
            if (string.IsNullOrEmpty(property)) return $"'{label}': no shader property set.";
            if (max <= min) return $"'{label}': max must be larger than min.";

            foreach (var existing in avatar.GetComponents<CVRFurySlider>())
                if (existing.menuPath == label) Object.DestroyImmediate(existing);

            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = label;
            slider.saved = true;
            slider.minState.actions.Add(MaterialAction(r, property, min));
            slider.maxState.actions.Add(MaterialAction(r, property, max));
            slider.defaultValue = 0f; // load at min (e.g. no hue shift)

            EditorUtility.SetDirty(avatar);
            return null;
        }

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
