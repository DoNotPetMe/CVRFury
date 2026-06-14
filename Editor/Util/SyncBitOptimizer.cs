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
    /// on/off menu toggle with a Float (a transition driver, a per-toggle 1D blend tree, or a Direct
    /// Blend Tree weight), so after conversion the CCK can still report "over the Synced Bit Limit"
    /// even though the AAS entries say Bool.
    ///
    /// This is CVRFury's CVR-native parameter compressor. Because CVR bills by type (not a flat
    /// per-parameter budget like VRChat), the win is retyping binary float "toggles" to Bool. It
    /// recognises three shapes of float-backed menu toggle and converts each to a Bool-driven On/Off
    /// layer, then retypes the parameter:
    ///   • transition-only floats (conditions with 0..1 thresholds) → retype + rewrite conditions;
    ///   • a per-toggle 1D blend tree (param, 2 clip children at min/max) → On/Off layer using the
    ///     tree's *real* off/on clips (works for any property — materials, scale, blendshapes);
    ///   • a Direct Blend Tree weight whose child clip only drives blendshapes / object-active state
    ///     → On/Off layer with a generated zeroed Off clip.
    ///
    /// Genuine radials (≠2-child or 2D blend trees), state speed/time params, and ambiguous
    /// direct-blend clips (scale/material) are reported and left as floats.
    /// </summary>
    internal static class SyncBitOptimizer
    {
        private const int BoolBits = 1, IntBits = 8, FloatBits = 32; // CVR per-type synced-bit cost (approx)

        /// <summary>
        /// Make a parameter's type compatible with how its transition conditions use it — WITHOUT
        /// breaking blend trees. VRChat declares GestureLeft/GestureRight (and others) as Int and gates
        /// on Equals/NotEqual, but ChilloutVR uses the gesture params as Float blend-tree parameters
        /// (hand-pose blends). A parameter used with Equals/NotEqual "wants" Int, but a parameter used
        /// as a blend-tree blendParameter/blendParameterY/directBlendParameter MUST be Float — Unity
        /// rejects an Int blend parameter ("uses parameter X which is not float type"). When a parameter
        /// is BOTH (the gesture params are), the blend-tree requirement wins: we leave it Float and leave
        /// its Equals/NotEqual conditions as-is. Only Equals/NotEqual params that are never used as a
        /// blend parameter are retyped to Int.
        /// </summary>
        public static void HarmonizeConditionParamTypes(AnimatorController controller, BuildLog log)
        {
            if (controller == null) return;

            var needsInt = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectEqualsParams(layer.stateMachine, needsInt);
            if (needsInt.Count == 0) return;

            // Parameters used as blend-tree inputs must remain Float, even if also Equals-gated.
            var blendParams = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectBlendParams(layer.stateMachine, blendParams);

            var ps = controller.parameters;
            int changed = 0;
            var keptFloat = new HashSet<string>();
            foreach (var p in ps)
            {
                if (!needsInt.Contains(p.name)) continue;
                if (blendParams.Contains(p.name)) { keptFloat.Add(p.name); continue; } // float blend param — leave it
                if (p.type != AnimatorControllerParameterType.Int)
                {
                    p.type = AnimatorControllerParameterType.Int;
                    changed++;
                }
            }
            if (changed > 0) controller.parameters = ps;

            // The kept-Float parameters (e.g. GestureLeft/GestureRight) are driven by CVR as floats AND
            // feed hand-pose blend trees, so they MUST stay Float — but the merged VRChat layers gate
            // transitions on them with Equals/NotEqual, which Unity only allows on Int. Left as-is those
            // transitions are invalid ("not compatible with condition type"): the gesture/weapon layer
            // can't leave its posed state, which both spams the validator and freezes the arms (the
            // "motorcycle pose"). Rewrite those conditions into Float-compatible threshold windows.
            int rewritten = 0, split = 0;
            if (keptFloat.Count > 0)
                RewriteFloatEqualityConditions(controller, keptFloat, ref rewritten, ref split);

            if (changed > 0 || keptFloat.Count > 0)
                log.Info($"Harmonised parameter types for conditions: {changed} retyped to Int to match " +
                         $"Equals/NotEqual; {keptFloat.Count} kept Float because they drive a blend tree " +
                         $"(e.g. GestureLeft/GestureRight). For those, rewrote {rewritten} transition(s)' " +
                         $"Equals/NotEqual conditions into Float threshold windows ({split} extra transition(s) " +
                         "added to express NotEqual as an OR) — fixes the 'not compatible with condition type' " +
                         "errors and the gesture-locked 'motorcycle pose'.");
        }

        /// <summary>Window half-width for turning an integer Equals/NotEqual on a Float parameter into
        /// Greater/Less bounds. Gesture values are whole numbers, so ±0.5 brackets exactly one of them.</summary>
        private const float GestureWindow = 0.5f;

        /// <summary>
        /// Rewrite every Equals/NotEqual condition on a <paramref name="floatParams"/> parameter into
        /// Float-compatible Greater/Less bounds. Equals expands in place (both bounds AND-ed onto the
        /// same transition); NotEqual is an OR, so the transition is duplicated — one copy bounded below
        /// the value, one above — preserving all other conditions and transition settings.
        /// </summary>
        private static void RewriteFloatEqualityConditions(AnimatorController controller,
                                                           HashSet<string> floatParams,
                                                           ref int rewritten, ref int split)
        {
            foreach (var layer in controller.layers)
                RewriteInMachine(layer.stateMachine, floatParams, ref rewritten, ref split);
        }

        private static void RewriteInMachine(AnimatorStateMachine sm, HashSet<string> floatParams,
                                             ref int rewritten, ref int split)
        {
            // State transitions.
            foreach (var cs in sm.states.ToArray())
            {
                var state = cs.state;
                foreach (var t in state.transitions.ToArray())
                {
                    var branches = ExpandBranches(t.conditions, floatParams, out var changed);
                    if (!changed) continue;
                    t.conditions = branches[0];
                    rewritten++;
                    for (var k = 1; k < branches.Count; k++)
                    {
                        var nt = CloneStateTransition(state, t);
                        if (nt == null) continue;
                        nt.conditions = branches[k];
                        split++;
                    }
                }
            }

            // Any-State transitions.
            foreach (var t in sm.anyStateTransitions.ToArray())
            {
                var branches = ExpandBranches(t.conditions, floatParams, out var changed);
                if (!changed) continue;
                t.conditions = branches[0];
                rewritten++;
                for (var k = 1; k < branches.Count; k++)
                {
                    var nt = CloneAnyStateTransition(sm, t);
                    if (nt == null) continue;
                    nt.conditions = branches[k];
                    split++;
                }
            }

            // Entry transitions (conditions only, no timing settings).
            foreach (var t in sm.entryTransitions.ToArray())
            {
                var branches = ExpandBranches(t.conditions, floatParams, out var changed);
                if (!changed) continue;
                t.conditions = branches[0];
                rewritten++;
                for (var k = 1; k < branches.Count; k++)
                {
                    var nt = t.destinationState != null ? sm.AddEntryTransition(t.destinationState)
                          : t.destinationStateMachine != null ? sm.AddEntryTransition(t.destinationStateMachine)
                          : null;
                    if (nt == null) continue;
                    nt.conditions = branches[k];
                    split++;
                }
            }

            foreach (var child in sm.stateMachines)
                RewriteInMachine(child.stateMachine, floatParams, ref rewritten, ref split);
        }

        /// <summary>Expand an AND-list of conditions into one or more AND-lists (OR-branches), turning
        /// integer Equals/NotEqual on a float parameter into Greater/Less bounds. A single Equals stays
        /// one branch; each NotEqual doubles the branch count (below-the-value OR above-the-value).</summary>
        private static List<AnimatorCondition[]> ExpandBranches(AnimatorCondition[] conds,
                                                                HashSet<string> floatParams, out bool changed)
        {
            changed = false;
            var branches = new List<List<AnimatorCondition>> { new List<AnimatorCondition>() };
            foreach (var c in conds)
            {
                bool target = floatParams.Contains(c.parameter);
                if (target && c.mode == AnimatorConditionMode.Equals)
                {
                    changed = true;
                    foreach (var b in branches)
                    {
                        b.Add(Cond(AnimatorConditionMode.Greater, c.parameter, c.threshold - GestureWindow));
                        b.Add(Cond(AnimatorConditionMode.Less, c.parameter, c.threshold + GestureWindow));
                    }
                }
                else if (target && c.mode == AnimatorConditionMode.NotEqual)
                {
                    changed = true;
                    var next = new List<List<AnimatorCondition>>(branches.Count * 2);
                    foreach (var b in branches)
                    {
                        var below = new List<AnimatorCondition>(b)
                            { Cond(AnimatorConditionMode.Less, c.parameter, c.threshold - GestureWindow) };
                        var above = new List<AnimatorCondition>(b)
                            { Cond(AnimatorConditionMode.Greater, c.parameter, c.threshold + GestureWindow) };
                        next.Add(below);
                        next.Add(above);
                    }
                    branches = next;
                }
                else
                {
                    foreach (var b in branches) b.Add(c);
                }
            }
            return branches.Select(b => b.ToArray()).ToList();
        }

        private static AnimatorCondition Cond(AnimatorConditionMode mode, string param, float threshold) =>
            new AnimatorCondition { mode = mode, parameter = param, threshold = threshold };

        private static AnimatorStateTransition CloneStateTransition(AnimatorState src, AnimatorStateTransition t)
        {
            AnimatorStateTransition nt;
            if (t.destinationState != null) nt = src.AddTransition(t.destinationState);
            else if (t.destinationStateMachine != null) nt = src.AddTransition(t.destinationStateMachine);
            else if (t.isExit) nt = src.AddExitTransition();
            else return null;
            CopyStateTransitionSettings(t, nt);
            return nt;
        }

        private static AnimatorStateTransition CloneAnyStateTransition(AnimatorStateMachine sm,
                                                                       AnimatorStateTransition t)
        {
            if (t.destinationState == null && t.destinationStateMachine == null) return null;
            var nt = t.destinationState != null
                ? sm.AddAnyStateTransition(t.destinationState)
                : sm.AddAnyStateTransition(t.destinationStateMachine);
            CopyStateTransitionSettings(t, nt);
            nt.canTransitionToSelf = t.canTransitionToSelf;
            return nt;
        }

        private static void CopyStateTransitionSettings(AnimatorStateTransition from, AnimatorStateTransition to)
        {
            to.hasExitTime = from.hasExitTime;
            to.exitTime = from.exitTime;
            to.hasFixedDuration = from.hasFixedDuration;
            to.duration = from.duration;
            to.offset = from.offset;
            to.interruptionSource = from.interruptionSource;
            to.orderedInterruption = from.orderedInterruption;
            to.mute = from.mute;
            to.solo = from.solo;
        }

        private static void CollectBlendParams(AnimatorStateMachine sm, HashSet<string> into)
        {
            void Walk(Motion m)
            {
                if (!(m is BlendTree tree)) return;
                if (!string.IsNullOrEmpty(tree.blendParameter)) into.Add(tree.blendParameter);
                if (!string.IsNullOrEmpty(tree.blendParameterY)) into.Add(tree.blendParameterY);
                foreach (var ch in tree.children)
                {
                    if (tree.blendType == BlendTreeType.Direct && !string.IsNullOrEmpty(ch.directBlendParameter))
                        into.Add(ch.directBlendParameter);
                    Walk(ch.motion);
                }
            }
            foreach (var cs in sm.states) Walk(cs.state.motion);
            foreach (var child in sm.stateMachines) CollectBlendParams(child.stateMachine, into);
        }

        private static void CollectEqualsParams(AnimatorStateMachine sm, HashSet<string> needsInt)
        {
            void Scan(System.Collections.Generic.IEnumerable<AnimatorTransitionBase> ts)
            {
                foreach (var t in ts)
                    foreach (var c in t.conditions)
                        if (c.mode == AnimatorConditionMode.Equals || c.mode == AnimatorConditionMode.NotEqual)
                            if (!string.IsNullOrEmpty(c.parameter)) needsInt.Add(c.parameter);
            }
            Scan(sm.anyStateTransitions);
            Scan(sm.entryTransitions);
            foreach (var cs in sm.states) Scan(cs.state.transitions);
            foreach (var child in sm.stateMachines)
            {
                Scan(sm.GetStateMachineTransitions(child.stateMachine));
                CollectEqualsParams(child.stateMachine, needsInt);
            }
        }

        public static void Run(AnimatorController controller, Predicate<string> canTouch,
                               HashSet<string> binaryToggleParams, AssetSaver assets, BuildLog log)
        {
            if (controller == null) return;

            var floatType = new HashSet<string>(controller.parameters
                .Where(p => p.type == AnimatorControllerParameterType.Float).Select(p => p.name));

            var analysis = Analyse(controller, canTouch, binaryToggleParams, floatType);

            log.Info(analysis.BeforeReport(controller, canTouch, FloatBits, IntBits, BoolBits));

            // 1) transition-only float conditions → Bool.
            if (analysis.ConditionToggles.Count > 0)
            {
                RetypeToBool(controller, analysis.ConditionToggles);
                RewriteConditions(controller, analysis.ConditionToggles);
            }

            // 2) per-toggle 1D blend trees → Bool On/Off layers (real off/on clips).
            foreach (var occ in analysis.OneDimToggles)
            {
                AddBoolToggleLayer(controller, occ.Param, occ.On, occ.Off, assets, generateOff: false);
                if (occ.SourceState != null) occ.SourceState.motion = null; // neutralise the float-driven source
            }

            // 3) Direct Blend Tree binary weights (safe clips) → Bool On/Off layers (generated off).
            foreach (var occ in analysis.DirectToggles)
                AddBoolToggleLayer(controller, occ.Param, occ.On, null, assets, generateOff: true);
            if (analysis.DirectToggleParams.Count > 0)
                PruneDirectChildren(controller, analysis.DirectToggleParams);

            var convertedNames = new HashSet<string>(analysis.ConditionToggles);
            convertedNames.UnionWith(analysis.OneDimToggles.Select(o => o.Param));
            convertedNames.UnionWith(analysis.DirectToggleParams);
            if (convertedNames.Count > 0) RetypeToBool(controller, convertedNames);

            log.Info($"Sync-bit optimiser: compressed {convertedNames.Count} float toggle(s) to Bool " +
                     $"({analysis.ConditionToggles.Count} condition, {analysis.OneDimToggles.Count} 1D-blend-tree, " +
                     $"{analysis.DirectToggles.Count} direct-blend), reclaiming ~{convertedNames.Count * (FloatBits - BoolBits)} " +
                     "synced bits. " + analysis.Skips());

            if (convertedNames.Count > 0)
                log.Info("After compression — " +
                         Analyse(controller, canTouch, binaryToggleParams, new HashSet<string>())
                             .BeforeReport(controller, canTouch, FloatBits, IntBits, BoolBits));
        }

        // ---------------------------------------------------------------- analysis

        private sealed class Occ
        {
            public string Param;
            public AnimationClip On;
            public AnimationClip Off;              // 1D only (direct generates its own)
            public AnimatorState SourceState;      // 1D only — neutralised after extraction
        }

        private sealed class Analysis
        {
            public readonly HashSet<string> ConditionToggles = new HashSet<string>();
            public readonly List<Occ> OneDimToggles = new List<Occ>();
            public readonly List<Occ> DirectToggles = new List<Occ>();
            public readonly HashSet<string> DirectToggleParams = new HashSet<string>();

            // Reasons a binary toggle param could NOT be compressed (for the report).
            public int SkipRadial, SkipMaterialScale, SkipMotion, Skip2D, SkipShared;
            public string SampleUnsafe;

            public string Skips()
            {
                if (SkipRadial + SkipMaterialScale + SkipMotion + Skip2D + SkipShared == 0)
                    return "No toggles left as float.";
                var s = $"Left as float: {SkipRadial} radial/complex-blend, {SkipMaterialScale} " +
                        $"material-swap/transform clip, {SkipShared} shared-binding, {Skip2D} 2D-blend, " +
                        $"{SkipMotion} motion-param.";
                if (SampleUnsafe != null) s += $" (e.g. binding '{SampleUnsafe}')";
                return s;
            }

            public string BeforeReport(AnimatorController c, Predicate<string> canTouch,
                                       int fBits, int iBits, int bBits)
            {
                int boolN = 0, intN = 0, floatN = 0;
                foreach (var p in c.parameters)
                {
                    if (!canTouch(p.name)) continue;
                    if (p.type == AnimatorControllerParameterType.Float) floatN++;
                    else if (p.type == AnimatorControllerParameterType.Int) intN++;
                    else boolN++;
                }
                var est = boolN * bBits + intN * iBits + floatN * fBits;
                var plan = (ConditionToggles.Count + OneDimToggles.Count + DirectToggles.Count) > 0
                    ? $" Compressible toggles found: {ConditionToggles.Count} condition, " +
                      $"{OneDimToggles.Count} 1D-blend, {DirectToggles.Count} direct-blend."
                    : "";
                return $"Synced animator parameters (non-#, non-core): {boolN} Bool, {intN} Int, {floatN} Float " +
                       $"— est. ~{est} synced bits (cap 3200).{plan}";
            }
        }

        private static Analysis Analyse(AnimatorController controller, Predicate<string> canTouch,
                                        HashSet<string> binary, HashSet<string> floatType)
        {
            var a = new Analysis();

            // --- usage roles ---
            var oneDimBlend = new HashSet<string>();   // any 1D/2D blendParameter
            var twoDimBlend = new HashSet<string>();
            var motion = new HashSet<string>();
            var conds = new Dictionary<string, List<float>>();
            // candidate occurrences keyed by param; param is only converted if it has NO blocking role.
            var oneDimCand = new Dictionary<string, Occ>();
            var directCand = new Dictionary<string, List<AnimationClip>>();
            var blocked = new HashSet<string>();       // disqualified entirely
            var sharedBinding = new HashSet<string>();  // direct-blend child shares a binding with a sibling

            void Block(string p) { if (!string.IsNullOrEmpty(p)) blocked.Add(p); }

            void WalkTree(Motion m, bool topOfState, AnimatorState state)
            {
                if (!(m is BlendTree tree)) return;

                if (tree.blendType == BlendTreeType.Simple1D)
                {
                    var p = tree.blendParameter;
                    var clips = tree.children.Select(ch => ch.motion as AnimationClip).ToArray();
                    // A clean per-toggle toggle: exactly 2 clip children, top-level motion of a state,
                    // driven by a binary menu-toggle float.
                    if (topOfState && state != null && binary.Contains(p) && canTouch(p) && floatType.Contains(p)
                        && tree.children.Length == 2 && clips.All(cl => cl != null) && !oneDimCand.ContainsKey(p))
                    {
                        var ch = tree.children;
                        var offIdx = ch[0].threshold <= ch[1].threshold ? 0 : 1;
                        oneDimCand[p] = new Occ
                        {
                            Param = p, SourceState = state,
                            Off = clips[offIdx], On = clips[1 - offIdx],
                        };
                    }
                    else
                    {
                        Block(p); // radial / complex 1D tree — keep as float
                    }
                }
                else if (tree.blendType == BlendTreeType.Direct)
                {
                    // Count how many children in THIS tree touch each binding. A toggle can only be
                    // safely lifted into an override layer if its bindings are exclusive to it — if a
                    // sibling "base" child also drives the same property, an override layer would lose
                    // the additive sum and corrupt the value.
                    var bindingCount = new Dictionary<string, int>();
                    var childKeys = new List<(string p, AnimationClip clip, List<string> keys)>();
                    foreach (var ch in tree.children)
                    {
                        var clip = ch.motion as AnimationClip;
                        var keys = BindingKeys(clip);
                        foreach (var k in keys) bindingCount[k] = bindingCount.TryGetValue(k, out var n) ? n + 1 : 1;
                        childKeys.Add((ch.directBlendParameter, clip, keys));
                    }
                    foreach (var (p, clip, keys) in childKeys)
                    {
                        if (string.IsNullOrEmpty(p) || !(binary.Contains(p) && canTouch(p) && floatType.Contains(p)))
                            continue;
                        if (keys.Any(k => bindingCount[k] > 1)) { sharedBinding.Add(p); continue; }
                        if (!directCand.TryGetValue(p, out var l)) directCand[p] = l = new List<AnimationClip>();
                        l.Add(clip);
                    }
                }
                else // 2D
                {
                    Block(tree.blendParameter);
                    twoDimBlend.Add(tree.blendParameter);
                    Block(tree.blendParameterY);
                    twoDimBlend.Add(tree.blendParameterY);
                }

                if (!string.IsNullOrEmpty(tree.blendParameter)) oneDimBlend.Add(tree.blendParameter);

                foreach (var ch in tree.children) WalkTree(ch.motion, false, null); // nested → not a clean toggle
            }

            void WalkMachine(AnimatorStateMachine sm)
            {
                foreach (var t in sm.anyStateTransitions) CollectConds(t, conds);
                foreach (var t in sm.entryTransitions) CollectConds(t, conds);
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    foreach (var t in s.transitions) CollectConds(t, conds);
                    if (s.speedParameterActive) motion.Add(s.speedParameter);
                    if (s.timeParameterActive) motion.Add(s.timeParameter);
                    if (s.mirrorParameterActive) motion.Add(s.mirrorParameter);
                    if (s.cycleOffsetParameterActive) motion.Add(s.cycleOffsetParameter);
                    WalkTree(s.motion, true, s);
                }
                foreach (var child in sm.stateMachines)
                {
                    foreach (var t in sm.GetStateMachineTransitions(child.stateMachine)) CollectConds(t, conds);
                    WalkMachine(child.stateMachine);
                }
            }

            foreach (var layer in controller.layers) WalkMachine(layer.stateMachine);

            foreach (var m in motion) Block(m);

            // --- transition-only binary float conditions ---
            foreach (var p in floatType)
            {
                if (!canTouch(p) || blocked.Contains(p)) continue;
                if (oneDimBlend.Contains(p) || directCand.ContainsKey(p) || motion.Contains(p)) continue;
                if (conds.TryGetValue(p, out var ths) && ths.Count > 0 && ths.All(t => t >= -0.001f && t <= 1.001f))
                    a.ConditionToggles.Add(p);
            }

            // --- 1D-blend-tree toggles ---
            foreach (var kv in oneDimCand)
            {
                var p = kv.Key;
                if (blocked.Contains(p) || conds.ContainsKey(p) || directCand.ContainsKey(p)) continue;
                a.OneDimToggles.Add(kv.Value);
            }

            // --- direct-blend toggles (need all children safe + a generatable off) ---
            foreach (var kv in directCand)
            {
                var p = kv.Key;
                if (blocked.Contains(p) || sharedBinding.Contains(p) || conds.ContainsKey(p) ||
                    oneDimBlend.Contains(p)) continue;
                if (kv.Value.All(cl => IsSafeToggleClip(cl, a)))
                {
                    a.DirectToggleParams.Add(p);
                    foreach (var clip in kv.Value) a.DirectToggles.Add(new Occ { Param = p, On = clip });
                }
                else
                {
                    a.SkipMaterialScale++;
                }
            }

            // --- categorise blocked binary toggles for the report ---
            foreach (var p in binary)
            {
                if (!canTouch(p) || !floatType.Contains(p)) continue;
                if (a.ConditionToggles.Contains(p) || a.OneDimToggles.Any(o => o.Param == p) ||
                    a.DirectToggleParams.Contains(p)) continue;
                if (motion.Contains(p)) a.SkipMotion++;
                else if (twoDimBlend.Contains(p)) a.Skip2D++;
                else if (sharedBinding.Contains(p)) a.SkipShared++;
                else if (directCand.ContainsKey(p)) { /* counted in SkipMaterialScale above */ }
                else a.SkipRadial++;
            }

            return a;
        }

        private static List<string> BindingKeys(AnimationClip clip)
        {
            var keys = new List<string>();
            if (clip == null) return keys;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                keys.Add(b.path + "|" + b.type + "|" + b.propertyName);
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                keys.Add(b.path + "|" + b.type + "|" + b.propertyName);
            return keys;
        }

        private static void CollectConds(AnimatorTransitionBase t, Dictionary<string, List<float>> conds)
        {
            foreach (var c in t.conditions)
            {
                if (string.IsNullOrEmpty(c.parameter)) continue;
                if (!conds.TryGetValue(c.parameter, out var l)) conds[c.parameter] = l = new List<float>();
                l.Add(c.threshold);
            }
        }

        private static bool IsSafeToggleClip(AnimationClip clip, Analysis a)
        {
            if (clip == null) return false;

            // Object-reference curves (material swaps) have no meaningful "zero", so we can't
            // synthesise an off pose for them.
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
            {
                a.SampleUnsafe = a.SampleUnsafe ?? "material/object-reference swap";
                return false;
            }

            // In a Direct Blend Tree a weight of 0 means the child contributes 0 to the sum, so the
            // correct "off" value for any FLOAT binding is exactly 0 — that holds for blendshapes,
            // material floats and AAP (animator-parameter) curves alike. The one exception is the
            // Transform: zeroing scale/position/rotation would collapse or teleport the object, so
            // those are treated as ambiguous and left as a synced float.
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (b.type == typeof(Transform))
                {
                    a.SampleUnsafe = a.SampleUnsafe ?? (b.propertyName + " (transform)");
                    return false;
                }
            return true;
        }

        // ---------------------------------------------------------------- mutation

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
            foreach (var layer in controller.layers) WalkConds(layer.stateMachine, names);
        }

        private static void WalkConds(AnimatorStateMachine sm, HashSet<string> names)
        {
            Fix(sm.anyStateTransitions, names);
            Fix(sm.entryTransitions, names);
            foreach (var cs in sm.states) Fix(cs.state.transitions, names);
            foreach (var child in sm.stateMachines)
            {
                Fix(sm.GetStateMachineTransitions(child.stateMachine), names);
                WalkConds(child.stateMachine, names);
            }
        }

        private static void Fix(IEnumerable<AnimatorTransitionBase> transitions, HashSet<string> names)
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

        private static void PruneDirectChildren(AnimatorController controller, HashSet<string> extract)
        {
            foreach (var layer in controller.layers)
                foreach (var tree in AllTrees(layer.stateMachine))
                {
                    var keep = tree.children.Where(ch =>
                        string.IsNullOrEmpty(ch.directBlendParameter) || !extract.Contains(ch.directBlendParameter))
                        .ToArray();
                    if (keep.Length != tree.children.Length) tree.children = keep;
                }
        }

        private static void AddBoolToggleLayer(AnimatorController c, string param, AnimationClip onClip,
                                               AnimationClip offClip, AssetSaver assets, bool generateOff)
        {
            if (generateOff)
            {
                offClip = MakeOffClip(onClip);
                assets.AddSubAsset(offClip, c);
            }

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

        private static AnimationClip MakeOffClip(AnimationClip on)
        {
            var off = new AnimationClip { name = (on != null ? on.name : "Toggle") + " (Off)" };
            if (on != null)
            {
                off.frameRate = on.frameRate;
                foreach (var b in AnimationUtility.GetCurveBindings(on))
                    AnimationUtility.SetEditorCurve(off, b, new AnimationCurve(new Keyframe(0f, 0f)));
            }
            return off;
        }

        private static IEnumerable<BlendTree> AllTrees(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
                foreach (var t in TreesIn(cs.state.motion)) yield return t;
            foreach (var child in sm.stateMachines)
                foreach (var t in AllTrees(child.stateMachine)) yield return t;
        }

        private static IEnumerable<BlendTree> TreesIn(Motion m)
        {
            if (!(m is BlendTree tree)) yield break;
            yield return tree;
            foreach (var ch in tree.children)
                foreach (var t in TreesIn(ch.motion)) yield return t;
        }
    }
}
