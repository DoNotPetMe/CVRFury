using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Drives blendshape values from the on/off state of one or more existing CVRFury
    /// Toggle/Modes controls, without you having to hand-build a toggle/slider for every shape.
    /// Typical use: a mesh that has no GameObject of its own to hide it (a "shrink to zero"
    /// blendshape, e.g. pasties), or a blendshape that should only apply once several other
    /// toggles are all on together (e.g. a clipping fix that only matters when two specific
    /// outfit pieces are both worn).
    ///
    /// Each <see cref="Rule"/> is "when these Toggle/Modes condition(s) all hold at once, set
    /// these blendshape(s) to these values." Rules are evaluated top-to-bottom; the first one
    /// whose conditions are all satisfied wins. When no rule matches, every blendshape a rule
    /// could have touched reverts to whatever value it has in the scene right now (its
    /// "resting" value) — same convention every other CVRFury feature uses.
    ///
    /// Conditions reference an existing CVRFury Toggle or CVRFury Modes component elsewhere on
    /// the avatar directly, so this feature reacts to exactly the same in-game parameter that
    /// control already drives — there's nothing to keep in sync by hand. A condition can't be
    /// built against anything that isn't itself a CVRFury Toggle/Modes (e.g. a GameObject you
    /// toggle by some other means entirely) — the Build Log will say so per rule if that happens.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Blendshape Rules")]
    public class CVRFuryBlendshapeRules : CVRFuryComponent
    {
        public override string FeatureTitle => "Blendshape Rules";

        // Must run after Toggle/Modes (priority 0), which record the parameters this feature
        // looks up, but before Blendshape Link (priority 50) so a live-mirrored link also sees
        // these curves.
        public override int BuildPriority => 20;

        [Serializable]
        public class Condition
        {
            public enum Kind { Toggle, Modes }

            [Tooltip("Whether this condition checks a CVRFury Toggle or a CVRFury Modes option.")]
            public Kind kind = Kind.Toggle;

            [Tooltip("A CVRFury Toggle component elsewhere on this avatar.")]
            public CVRFuryToggle toggle;

            [Tooltip("True = rule requires this Toggle to be ON. False = requires it OFF.")]
            public bool requiredOn = true;

            [Tooltip("A CVRFury Modes component elsewhere on this avatar.")]
            public CVRFuryModes modes;

            [Tooltip("Which mode option (by position in that component's Modes list) this condition requires.")]
            public int modeIndex = 0;
        }

        [Serializable]
        public class BlendshapeTarget
        {
            [Tooltip("Mesh carrying the blendshape.")]
            public SkinnedMeshRenderer renderer;

            [Tooltip("Blendshape name. Pick one from the dropdown — it's read straight off the " +
                     "renderer's mesh, no typing needed.")]
            public string blendShape = "";

            [Range(0f, 100f)] public float value = 100f;
        }

        [Serializable]
        public class Rule
        {
            [Tooltip("Just a label for your own reference in this list; not shown anywhere in-game.")]
            public string name = "Rule";

            [Tooltip("ALL of these must hold at once for this rule to apply.")]
            public List<Condition> conditions = new List<Condition>();

            [Tooltip("Blendshapes to set while this rule's conditions hold.")]
            public List<BlendshapeTarget> targets = new List<BlendshapeTarget>();
        }

        public List<Rule> rules = new List<Rule>();
    }
}
