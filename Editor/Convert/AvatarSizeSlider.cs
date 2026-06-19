using UnityEngine;
using UnityEditor;
using CVRFury.Components;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Adds a native in-game avatar-size slider: a CVRFury Slider on the avatar root whose two endpoints
    /// scale the avatar between <c>min</c>× and <c>max</c>× its authored size. Because it's a normal AAS
    /// slider + scale animation, it works in ChilloutVR with no contacts or special shaders — drag the
    /// slider in the Advanced Settings menu and the avatar grows/shrinks.
    /// </summary>
    internal static class AvatarSizeSlider
    {
        public static string Add(GameObject avatar, float min, float max)
        {
            if (avatar == null) return "Select your avatar first.";
            if (max <= min) return "Max size must be larger than min size.";

            var root = avatar.transform;

            // Remove a previous CVRFury size slider so re-running doesn't stack duplicates.
            foreach (var existing in avatar.GetComponents<CVRFurySlider>())
                if (existing.menuPath == "Avatar Size") Object.DestroyImmediate(existing);

            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = "Avatar Size";
            slider.saved = true;
            slider.localOnly = false;

            slider.minState.actions.Add(ScaleAction(root, min));
            slider.maxState.actions.Add(ScaleAction(root, max));

            // Default the slider so the avatar loads at its normal (1×) size: value where min..max == 1.
            slider.defaultValue = Mathf.Clamp01((1f - min) / (max - min));

            EditorUtility.SetDirty(avatar);
            return $"Added an \"Avatar Size\" slider ({min:0.##}× – {max:0.##}×), defaulting to normal size. " +
                   "It scales the whole avatar at runtime via the Advanced Settings menu — no contacts needed. " +
                   "Test-bake or upload to use it.";
        }

        private static FuryAction ScaleAction(Transform root, float factor) => new FuryAction
        {
            type = FuryAction.ActionType.ScaleFactor,
            scaleTarget = root,
            scaleFactor = factor,
        };
    }
}
