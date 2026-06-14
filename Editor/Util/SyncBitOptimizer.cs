using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// ChilloutVR network-syncs every animator-controller parameter that isn't local (name starting
    /// with <c>#</c>) or a core parameter, and the synced-bit cost is driven by the *animator*
    /// parameter type — a Float costs ~32 bits, a Bool ~1. VRChat avatars frequently back a simple
    /// on/off toggle with a Float (a transition driver, or — on modern avatars — a single Direct
    /// Blend Tree weight per toggle), so after conversion the CCK can still report "over the Synced
    /// Bit Limit" even though the AAS entries say Bool.
    ///
    /// This is CVRFury's CVR-native parameter compressor. Because CVR bills by type (not by a flat
    /// per-parameter budget like VRChat), the win is retyping binary float "toggles" to Bool:
    ///  1. floats used only as on/off transition conditions → retype to Bool, rewrite conditions;
    ///  2. floats used only as Direct Blend Tree weights for an on/off menu toggle → lift each child
    ///     out of the blend tree into its own Bool-driven On/Off layer (a generated Off clip zeroes
    ///     the animated blendshape / active-state bindings), then retype the parameter to Bool.
    ///
    /// Genuinely-continuous floats (radial puppets, 1D/2D blend parameters, state speed/time params)
    /// are reported and left untouched.
    /// </summary>
    internal static class SyncBitOptimizer
    {
        private const int BoolBits = 1, IntBits = 8, FloatBits = 32; // CVR per-type synced-bit cost (approx)

        public static void Run(AnimatorController controller, Predicate<string> canTouch,
                               HashSet<string> binaryToggleParams, AssetSaver assets, BuildLog log)
        {
            if (controller == null) return;

            var usage = Usage.Collect(controller);

            // --- 1. transition-only binary floats → Bool ---
            var retype = new HashSet<string>();
            foreach (var p in controller.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float || !canTouch(p.name)) continue;
                if (usage.OneDimBlend.Contains(p.name) || usage.DirectBlend.ContainsKey(p.name) ||
                    usage.Motion.Contains(p.name)) continue;
                if (usage.ConditionThresholds.TryGetValue(p.name, out var ths) && ths.Count > 0 &&
                    ths.All(t => t >= -0.001f && t <= 1.001f))
                    retype.Add(p.name);
            }

            // --- 2. direct-blend-tree binary toggles → Bool-driven On/Off layers ---
            var extractable = new List<string>();
            foreach (var p in controller.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float || !canTouch(p.name)) continue;
                if (!binaryToggleParams.Contains(p.name)) continue;        // only menu toggles are binary
                if (!usage.DirectBlend.ContainsKey(p.name)) continue;       // must be a blend-tree weight
                if (usage.OneDimBlend.Contains(p.name) || usage.Motion.Contains(p.name)) continue;
                if (usage.ConditionThresholds.ContainsKey(p.name)) continue; // also a condition — leave it
                if (usage.DirectBlend[p.name].All(IsSafeToggleClip))         // every child is a safe on-clip
                    extractable.Add(p.name);
            }

            log.Info(Report(controller, canTouch, usage, retype.Count, extractable.Count));

            if (retype.Count > 0)
            {
                RetypeToBool(controller, retype);
                RewriteConditions(controller, retype);
                log.Info($"Sync-bit optimiser: retyped {retype.Count} on/off float condition(s) to Bool.");
            }

            if (extractable.Count > 0)
            {
                var set = new HashSet<string>(extractable);
                RetypeToBool(controller, set);
                var layers = ExtractDirectBlendToggles(controller, set, assets);
                log.Info($"Sync-bit optimiser: lifted {extractable.Count} blend-tree toggle(s) into Bool layers " +
                         $"({layers} layer(s) added). Each saved ~{FloatBits - BoolBits} synced bits. NOTE: the " +
                         "generated Off states zero blendshape / object-active bindings; if a toggle drove scale, " +
                         "position or a material value, set its Off animation in the controller manually.");
            }

            if (retype.Count + extractable.Count > 0)
                log.Info("Sync-bit optimiser: " + Report(controller,
                    canTouch, Usage.Collect(controller), 0, 0));
        }

        // ---------------------------------------------------------------- reporting

        private static string Report(AnimatorController controller, Predicate<string> canTouch,
                                     Usage usage, int willRetype, int willExtract)
        {
            int boolN = 0, intN = 0, floatBlend = 0, floatCond = 0, floatOther = 0;
            foreach (var p in controller.parameters)
            {
                if (!canTouch(p.name)) continue;
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger: boolN++; break;
                    case AnimatorControllerParameterType.Int: intN++; break;
                    case AnimatorControllerParameterType.Float:
                        if (usage.OneDimBlend.Contains(p.name) || usage.Motion.Contains(p.name)) floatBlend++;
                        else if (usage.DirectBlend.ContainsKey(p.name)) floatBlend++;
                        else if (usage.ConditionThresholds.ContainsKey(p.name)) floatCond++;
                        else floatOther++;
                        break;
                }
            }
            var floatTotal = floatBlend + floatCond + floatOther;
            var est = boolN * BoolBits + intN * IntBits + floatTotal * FloatBits;
            var note = (willRetype + willExtract) > 0
                ? $" Will compress {willRetype} condition float(s) + {willExtract} blend-tree toggle(s) to Bool."
                : "";
            return $"Synced animator parameters (non-#, non-core): {boolN} Bool, {intN} Int, {floatTotal} Float " +
                   $"({floatBlend} blend-tree/continuous, {floatCond} condition, {floatOther} other) " +
                   $"— est. ~{est} synced bits (cap 3200).{note}";
        }

        // ---------------------------------------------------------------- retype + conditions

        private static void RetypeToBool(AnimatorController controller, HashSet<string> names)
        {
            var ps = controller.parameters;
            foreach (var p in ps)
            {
                if (!names.Contains(p.name)) continue;
                p.defaultBool = p.defaultFloat >= 0.5f;
                p.type = AnimatorControllerParameterType.Bool;
            }
            controller.parameters = ps;
        }

        private static void RewriteConditions(AnimatorController controller, HashSet<string> names)
        {
            foreach (var layer in controller.layers)
                WalkMachine(layer.stateMachine, names);
        }

        private static void WalkMachine(AnimatorStateMachine sm, HashSet<string> names)
        {
            FixTransitions(sm.anyStateTransitions, names);
            FixTransitions(sm.entryTransitions, names);
            foreach (var cs in sm.states) FixTransitions(cs.state.transitions, names);
            foreach (var child in sm.stateMachines)
            {
                FixTransitions(sm.GetStateMachineTransitions(child.stateMachine), names);
                WalkMachine(child.stateMachine, names);
            }
        }

        private static void FixTransitions(IEnumerable<AnimatorTransitionBase> transitions, HashSet<string> names)
        {
            foreach (var t in transitions)
            {
                var conds = t.conditions;
                var changed = false;
                for (var i = 0; i < conds.Length; i++)
                {
                    if (!names.Contains(conds[i].parameter)) continue;
                    conds[i].mode = conds[i].mode == AnimatorConditionMode.Greater
                        ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                    conds[i].threshold = 0f;
                    changed = true;
                }
                if (changed) t.conditions = conds;
            }
        }

        // ---------------------------------------------------------------- blend-tree extraction

        /// <summary>An on-clip is safe to auto-generate an Off for when it only animates blendshapes
        /// and GameObject active-state (Off = 0/inactive). Material/transform toggles are ambiguous.</summary>
        private static bool IsSafeToggleClip(AnimationClip clip)
        {
            if (clip == null) return false;
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0) return false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (!(b.propertyName.StartsWith("blendShape.") || b.propertyName == "m_IsActive"))
                    return false;
            return true;
        }

        private static int ExtractDirectBlendToggles(AnimatorController controller, HashSet<string> extract,
                                                     AssetSaver assets)
        {
            // Gather (param, onClip) tasks while removing the children from their blend trees.
            var tasks = new List<(string param, AnimationClip on)>();
            foreach (var layer in controller.layers)
                foreach (var tree in BlendTreesIn(layer.stateMachine))
                    PruneTree(tree, extract, tasks);

            var layersAdded = 0;
            foreach (var (param, on) in tasks)
            {
                AddBoolToggleLayer(controller, param, on, assets);
                layersAdded++;
            }
            return layersAdded;
        }

        private static void PruneTree(BlendTree tree, HashSet<string> extract,
                                      List<(string, AnimationClip)> tasks)
        {
            var keep = new List<ChildMotion>();
            foreach (var ch in tree.children)
            {
                if (ch.motion is BlendTree nested) PruneTree(nested, extract, tasks);

                if (!string.IsNullOrEmpty(ch.directBlendParameter) &&
                    extract.Contains(ch.directBlendParameter) && ch.motion is AnimationClip clip)
                {
                    tasks.Add((ch.directBlendParameter, clip));
                    continue; // drop from the tree
                }
                keep.Add(ch);
            }
            if (keep.Count != tree.children.Length) tree.children = keep.ToArray();
        }

        private static void AddBoolToggleLayer(AnimatorController c, string param, AnimationClip onClip,
                                               AssetSaver assets)
        {
            var offClip = MakeOffClip(onClip);
            assets.AddSubAsset(offClip, c);

            var name = AnimatorUtil.UniqueLayerName(c, "CVRFury Toggle: " + param.TrimStart('#'));
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            var off = sm.AddState("Off");
            off.motion = offClip;
            off.writeDefaultValues = false;
            var on = sm.AddState("On");
            on.motion = onClip;
            on.writeDefaultValues = false;
            sm.defaultState = off;

            var toOn = off.AddTransition(on);
            toOn.hasExitTime = false; toOn.duration = 0f; toOn.canTransitionToSelf = false;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, param);

            var toOff = on.AddTransition(off);
            toOff.hasExitTime = false; toOff.duration = 0f; toOff.canTransitionToSelf = false;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, param);
        }

        /// <summary>Clone an on-clip with every (safe) binding driven to 0 — the toggle's "off" pose,
        /// matching a direct-blend-tree weight of 0.</summary>
        private static AnimationClip MakeOffClip(AnimationClip on)
        {
            var off = new AnimationClip { name = on.name + " (Off)", frameRate = on.frameRate };
            foreach (var b in AnimationUtility.GetCurveBindings(on))
                AnimationUtility.SetEditorCurve(off, b, new AnimationCurve(new Keyframe(0f, 0f)));
            return off;
        }

        private static IEnumerable<BlendTree> BlendTreesIn(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
                if (cs.state.motion is BlendTree t)
                    yield return t;
            foreach (var child in sm.stateMachines)
                foreach (var t in BlendTreesIn(child.stateMachine))
                    yield return t;
        }

        // ---------------------------------------------------------------- usage collection

        private sealed class Usage
        {
            public readonly HashSet<string> OneDimBlend = new HashSet<string>();   // blendParameter / Y
            public readonly Dictionary<string, List<AnimationClip>> DirectBlend =  // directBlendParameter → on-clips
                new Dictionary<string, List<AnimationClip>>();
            public readonly HashSet<string> Motion = new HashSet<string>();        // state speed/time/mirror/cycle
            public readonly Dictionary<string, List<float>> ConditionThresholds =
                new Dictionary<string, List<float>>();

            public static Usage Collect(AnimatorController controller)
            {
                var u = new Usage();
                foreach (var layer in controller.layers)
                    u.Machine(layer.stateMachine);
                return u;
            }

            private void Machine(AnimatorStateMachine sm)
            {
                Conds(sm.anyStateTransitions);
                Conds(sm.entryTransitions);
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    Conds(s.transitions);
                    if (s.speedParameterActive) Motion.Add(s.speedParameter);
                    if (s.timeParameterActive) Motion.Add(s.timeParameter);
                    if (s.mirrorParameterActive) Motion.Add(s.mirrorParameter);
                    if (s.cycleOffsetParameterActive) Motion.Add(s.cycleOffsetParameter);
                    Tree(s.motion);
                }
                foreach (var child in sm.stateMachines)
                {
                    Conds(sm.GetStateMachineTransitions(child.stateMachine));
                    Machine(child.stateMachine);
                }
            }

            private void Conds(IEnumerable<AnimatorTransitionBase> ts)
            {
                foreach (var t in ts)
                    foreach (var c in t.conditions)
                    {
                        if (string.IsNullOrEmpty(c.parameter)) continue;
                        if (!ConditionThresholds.TryGetValue(c.parameter, out var l))
                            ConditionThresholds[c.parameter] = l = new List<float>();
                        l.Add(c.threshold);
                    }
            }

            private void Tree(Motion motion)
            {
                if (!(motion is BlendTree tree)) return;
                if (!string.IsNullOrEmpty(tree.blendParameter)) OneDimBlend.Add(tree.blendParameter);
                if (!string.IsNullOrEmpty(tree.blendParameterY)) OneDimBlend.Add(tree.blendParameterY);
                foreach (var ch in tree.children)
                {
                    if (!string.IsNullOrEmpty(ch.directBlendParameter))
                    {
                        if (!DirectBlend.TryGetValue(ch.directBlendParameter, out var l))
                            DirectBlend[ch.directBlendParameter] = l = new List<AnimationClip>();
                        l.Add(ch.motion as AnimationClip); // null if nested tree → IsSafeToggleClip rejects
                    }
                    Tree(ch.motion);
                }
            }
        }
    }
}
