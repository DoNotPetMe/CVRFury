using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// A menu toggle. Produces a synced Advanced Avatar Setting (a GameObject Toggle,
    /// so it appears in ChilloutVR's in-game Advanced Settings menu) plus an animator
    /// layer that drives the configured <see cref="FuryState"/> when the toggle is on.
    ///
    /// Unlike a bare CCK GameObject Toggle, a CVRFury toggle can animate <i>anything</i>
    /// (objects, blendshapes, materials, scale, shader properties) because CVRFury builds
    /// the animation clip itself and binds it to the synced parameter.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Toggle")]
    public class CVRFuryToggle : CVRFuryComponent
    {
        public override string FeatureTitle => "Toggle";

        [Tooltip("Where the control appears in the in-game Advanced Settings menu, e.g. \"Clothing/Hat\". " +
                 "Slashes create submenus.")]
        public string menuPath = "";

        [Tooltip("Optional. The synced parameter (machine) name. Leave blank to auto-generate a unique one.")]
        public string parameterName = "";

        [Tooltip("Whether the toggle starts on for a freshly loaded avatar.")]
        public bool defaultOn = false;

        [Tooltip("Persist the toggle's value between sessions / avatar reloads.")]
        public bool saved = true;

        [Tooltip("Don't sync this toggle to other players (local-only).")]
        public bool localOnly = false;

        [Tooltip("Build this as a momentary push button instead of a sticky toggle.")]
        public bool momentary = false;

        [Tooltip("Smoothly animate between off and on over this many seconds (0 = instant).")]
        public float transitionSeconds = 0f;

        [Tooltip("What happens when the toggle is ON. (OFF is the resting avatar.)")]
        public FuryState state = new FuryState();
    }
}
