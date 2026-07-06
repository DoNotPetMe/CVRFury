using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// A synced Advanced Avatar Settings dropdown of outfit/configuration presets built from your
    /// existing CVRFury Toggles. Selecting a preset equips its toggles and turns off everything any
    /// other preset references — true "equip one, the rest turn off" behaviour. Option 0 ("Custom")
    /// animates nothing, leaving the individual toggles in manual control.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Presets")]
    public class CVRFuryPresets : CVRFuryComponent
    {
        public override string FeatureTitle => "Presets";

        // Build after individual toggles so the preset layer sits above them in the animator
        // and wins over them while a preset is selected.
        public override int BuildPriority => 10;

        [Serializable]
        public class Preset
        {
            [Tooltip("Label shown for this preset in the in-game dropdown.")]
            public string name = "Preset";

            [Tooltip("CVRFury Toggles this preset equips (their ON state is applied). Anything " +
                     "referenced by any other preset but not listed here is forced off.")]
            public List<CVRFuryToggle> toggles = new List<CVRFuryToggle>();

            [Tooltip("Extra actions applied while this preset is selected (optional).")]
            public FuryState state = new FuryState();
        }

        [Tooltip("Where the control appears in the in-game Advanced Settings menu.")]
        public string menuPath = "";

        [Tooltip("Optional explicit synced parameter (machine) name. Blank = auto.")]
        public string parameterName = "";

        [Tooltip("Don't sync this dropdown to other players (local-only).")]
        public bool localOnly = false;

        [Tooltip("Blend time when switching presets, in seconds (0 = instant).")]
        public float transitionSeconds = 0f;

        [Tooltip("The presets. In-game the dropdown shows \"Custom\" (manual toggles) first, " +
                 "then one option per preset.")]
        public List<Preset> presets = new List<Preset>();
    }
}
