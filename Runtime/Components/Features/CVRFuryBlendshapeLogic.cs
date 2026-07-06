using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Rule-based blendshape automation: "when these GameObjects are enabled/disabled, set these
    /// blendshapes to these values". Blendshapes are auto-detected from the mesh and picked from a
    /// dropdown with a value slider. Conditions support multiple GameObjects at once (all must
    /// match), and each condition object must be driven by a CVRFury Toggle so the rule can key on
    /// its synced parameter in-game. The inspector can also create toggles straight from blendshapes.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Blendshape Logic")]
    public class CVRFuryBlendshapeLogic : CVRFuryComponent
    {
        public override string FeatureTitle => "Blendshape Logic";

        // Build after toggles (0) and presets (10) so their parameters exist, but before
        // Blendshape Link (50) so linked meshes can mirror the curves this feature generates.
        public override int BuildPriority => 40;

        [Serializable]
        public class Condition
        {
            [Tooltip("A GameObject that is shown/hidden by a CVRFury Toggle on this avatar.")]
            public GameObject target;

            [Tooltip("Whether the object must be active (on) or inactive (off) for the rule to apply.")]
            public bool mustBeActive = true;
        }

        [Serializable]
        public class Assignment
        {
            [Tooltip("Blendshape on the mesh (picked from the auto-detected list).")]
            public string blendshape = "";

            [Range(0f, 100f)]
            [Tooltip("Weight the blendshape is set to while the rule is active.")]
            public float value = 100f;
        }

        [Serializable]
        public class Rule
        {
            [Tooltip("A label for this rule (only shown in the inspector).")]
            public string name = "Rule";

            [Tooltip("ALL conditions must match at once for the rule to apply (multi-condition AND).")]
            public List<Condition> conditions = new List<Condition>();

            [Tooltip("Blendshape values applied while the rule is active. When it stops matching, " +
                     "the blendshapes return to their resting scene values.")]
            public List<Assignment> assignments = new List<Assignment>();
        }

        [Tooltip("Mesh whose blendshapes are driven. Auto-detected from this GameObject when empty.")]
        public SkinnedMeshRenderer mesh;

        [Tooltip("Blend time when a rule activates/deactivates, in seconds (0 = instant).")]
        public float transitionSeconds = 0.1f;

        public List<Rule> rules = new List<Rule>();
    }
}
