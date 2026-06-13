using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// A continuous radial control (VRCFury "puppet" / slider). Produces a synced Advanced Avatar
    /// Setting slider plus a 1D blend-tree animator layer that interpolates between a 0% state and
    /// a 100% state. Typical use: a blendshape size slider, a material fade, or a continuous scale.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Slider")]
    public class CVRFurySlider : CVRFuryComponent
    {
        public override string FeatureTitle => "Slider";

        [Tooltip("Where the control appears in the in-game Advanced Settings menu.")]
        public string menuPath = "";

        [Tooltip("Optional explicit synced parameter (machine) name. Blank = auto.")]
        public string parameterName = "";

        [Range(0f, 1f)] public float defaultValue = 0f;

        public bool saved = true;
        public bool localOnly = false;

        [Tooltip("Avatar appearance at slider value 0. If empty, the current scene state is used.")]
        public FuryState minState = new FuryState();

        [Tooltip("Avatar appearance at slider value 1 (fully on).")]
        public FuryState maxState = new FuryState();
    }
}
