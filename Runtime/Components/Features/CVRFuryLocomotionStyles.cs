using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Custom crouch/prone animations selectable from an in-game dropdown. Assign the animation
    /// clips from your crouch/prone pack (e.g. one distributed for NotAKidoS' SimpleAAS workflow)
    /// and CVRFury bakes a synced Advanced Avatar Settings dropdown ("Default" + one option per
    /// style) plus animator states inside the locomotion layer, gated on ChilloutVR's Crouching /
    /// Prone core parameters — no manual controller or SimpleAAS setup needed.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Locomotion Styles")]
    public class CVRFuryLocomotionStyles : CVRFuryComponent
    {
        public override string FeatureTitle => "Locomotion Styles";

        [Serializable]
        public class Style
        {
            [Tooltip("Label shown for this style in the in-game dropdown.")]
            public string name = "Style";

            [Tooltip("Looping humanoid animation played while crouching with this style selected. " +
                     "Leave empty to keep ChilloutVR's default crouch.")]
            public AnimationClip crouchClip;

            [Tooltip("Looping humanoid animation played while prone with this style selected. " +
                     "Leave empty to keep ChilloutVR's default prone.")]
            public AnimationClip proneClip;
        }

        [Tooltip("Where the control appears in the in-game Advanced Settings menu.")]
        public string menuPath = "";

        [Tooltip("Optional explicit synced parameter (machine) name. Blank = auto.")]
        public string parameterName = "";

        [Tooltip("Don't sync this dropdown to other players (local-only).")]
        public bool localOnly = false;

        [Tooltip("Blend time entering/leaving a custom pose, in seconds.")]
        public float transitionSeconds = 0.15f;

        [Tooltip("The selectable styles. In-game the dropdown shows \"Default\" (CVR's own " +
                 "crouch/prone) first, then one option per style.")]
        public List<Style> styles = new List<Style>();
    }
}
