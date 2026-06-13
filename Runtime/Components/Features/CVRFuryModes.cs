using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// An exclusive multi-state control (VRCFury "Modes"). Produces a synced Advanced Avatar
    /// Setting dropdown plus an animator layer where exactly one mode is active at a time.
    /// Great for outfit variants, hair styles, or weapon selection — pick one, the rest turn off.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Modes")]
    public class CVRFuryModes : CVRFuryComponent
    {
        public override string FeatureTitle => "Modes";

        [Serializable]
        public class Mode
        {
            [Tooltip("Label shown for this option in the in-game dropdown.")]
            public string name = "Mode";

            [Tooltip("What the avatar looks like while this mode is selected.")]
            public FuryState state = new FuryState();
        }

        [Tooltip("Where the control appears in the in-game Advanced Settings menu.")]
        public string menuPath = "";

        [Tooltip("Optional explicit synced parameter (machine) name. Blank = auto.")]
        public string parameterName = "";

        [Tooltip("Index of the mode selected on a freshly loaded avatar.")]
        public int defaultMode = 0;

        public bool saved = true;
        public bool localOnly = false;

        [Tooltip("Blend time between modes in seconds (0 = instant).")]
        public float transitionSeconds = 0f;

        public List<Mode> modes = new List<Mode>();
    }
}
