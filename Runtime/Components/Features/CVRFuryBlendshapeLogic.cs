using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Conditional blendshapes, made simple: "WHEN these objects are on/off → SET this blendshape to this
    /// value." The classic use is clothing clipping fixes — e.g. a shrink blendshape that must apply only
    /// when the coat AND the bra are both enabled — or blendshape-only items like pasties that have no
    /// GameObject to toggle. Each rule bakes into an animator layer whose conditions watch the SAME menu
    /// toggles that drive those objects, so the logic follows the toggles automatically in game. When a
    /// rule's conditions stop being true, the blendshape returns to its current scene value.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Blendshape Logic")]
    public class CVRFuryBlendshapeLogic : CVRFuryComponent
    {
        public override string FeatureTitle => "Blendshape Logic";

        // Build AFTER the toggles so their animator parameters exist and are recorded.
        public override int BuildPriority => 10;

        [Serializable]
        public class Condition
        {
            [Tooltip("A GameObject that has a CVRFury Toggle driving it (e.g. the coat).")]
            public GameObject obj;

            [Tooltip("ON = the rule needs this object's toggle enabled; OFF = it needs it disabled.")]
            public bool mustBeOn = true;
        }

        [Serializable]
        public class Rule
        {
            [Tooltip("Optional note to yourself, e.g. \"squish breasts when coat over bra\".")]
            public string note = "";

            [Tooltip("ALL of these must match at the same time for the blendshape to apply.")]
            public List<Condition> when = new List<Condition> { new Condition() };

            [Tooltip("The mesh carrying the blendshape.")]
            public SkinnedMeshRenderer renderer;

            [Tooltip("The blendshape to drive while the conditions hold.")]
            public string blendShape = "";

            [Range(0f, 100f)]
            [Tooltip("The value the blendshape is set to while the conditions hold (returns to the scene value otherwise).")]
            public float value = 100f;
        }

        public List<Rule> rules = new List<Rule> { new Rule() };
    }
}
