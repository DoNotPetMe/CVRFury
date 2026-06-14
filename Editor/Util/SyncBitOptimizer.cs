using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// ChilloutVR network-syncs every animator-controller parameter that isn't local (name starting
    /// with <c>#</c>) or a core parameter, and the synced-bit cost is driven by the *animator*
    /// parameter type — a Float costs far more than a Bool. VRChat avatars frequently back a simple
    /// on/off toggle with a Float parameter (for smooth blends), so after conversion the CCK can
    /// still report "over the Synced Bit Limit" even though the AAS entries say Bool.
    ///
    /// This pass:
    ///  • reports exactly where the synced bits live (Bool / Int / Float, and for floats whether they
    ///    are continuous blend-tree drivers or merely on/off transition conditions), and
    ///  • safely retypes the float parameters that are only ever used as on/off transition conditions
    ///    (thresholds within [0,1]) to Bool, rewriting their conditions — turning a 64-bit synced
    ///    float into a ~1-bit synced bool with no behavioural change.
    ///
    /// Continuous floats genuinely used by blend trees (radials, and modern "direct blend tree"
    /// toggle setups) are left untouched and reported, since collapsing those needs a structural
    /// rewrite, not a retype.
    /// </summary>
    internal static class SyncBitOptimizer
    {
        // Rough per-type synced-bit estimate, only for the human-readable report.
        private const int BoolBits = 1, IntBits = 8, FloatBits = 64;

        public static void Run(AnimatorController controller, Predicate<string> canTouch, BuildLog log)
        {
            if (controller == null) return;

            var blendParams = new HashSet<string>();   // used as a blend-tree weight (must stay float)
            var motionParams = new HashSet<string>();  // used as state speed/time/mirror/cycle (stay float)
            var conditionThresholds = new Dictionary<string, List<float>>(); // param → thresholds seen in conditions

            CollectUsage(controller, blendParams, motionParams, conditionThresholds);

            // --- decide which floats can become bools ---
            var convert = new HashSet<string>();
            foreach (var p in controller.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float) continue;
                if (!canTouch(p.name)) continue;
                if (blendParams.Contains(p.name) || motionParams.Contains(p.name)) continue;
                // Only ever used as on/off conditions, and all thresholds are within [0,1].
                if (!conditionThresholds.TryGetValue(p.name, out var ths) || ths.Count == 0) continue;
                if (ths.All(t => t >= -0.001f && t <= 1.001f))
                    convert.Add(p.name);
            }

            // --- report before ---
            log.Info(Report(controller, canTouch, blendParams, motionParams, convert));

            if (convert.Count == 0) return;

            RetypeToBool(controller, convert);
            RewriteConditions(controller, convert);

            log.Info($"Sync-bit optimiser: retyped {convert.Count} on/off float parameter(s) to Bool " +
                     $"(~{convert.Count * (FloatBits - BoolBits)} synced bits reclaimed). Continuous/blend-tree " +
                     "floats were left as-is.");
        }

        private static string Report(AnimatorController controller, Predicate<string> canTouch,
                                     HashSet<string> blendParams, HashSet<string> motionParams,
                                     HashSet<string> convert)
        {
            int boolN = 0, intN = 0, floatBlend = 0, floatCond = 0, floatOther = 0;
            foreach (var p in controller.parameters)
            {
                if (!canTouch(p.name)) continue; // skip #local and core
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger: boolN++; break;
                    case AnimatorControllerParameterType.Int: intN++; break;
                    case AnimatorControllerParameterType.Float:
                        if (blendParams.Contains(p.name) || motionParams.Contains(p.name)) floatBlend++;
                        else if (convert.Contains(p.name)) floatCond++;
                        else floatOther++;
                        break;
                }
            }

            var floatTotal = floatBlend + floatCond + floatOther;
            var estBefore = boolN * BoolBits + intN * IntBits + floatTotal * FloatBits;
            var estAfter = (boolN + floatCond) * BoolBits + intN * IntBits + (floatBlend + floatOther) * FloatBits;
            return $"Synced animator parameters (non-#, non-core): {boolN} Bool, {intN} Int, {floatTotal} Float " +
                   $"({floatBlend} blend-tree/continuous, {floatCond} on/off-convertible, {floatOther} other) " +
                   $"— est. ~{estBefore} synced bits, ~{estAfter} after retyping {floatCond} float toggle(s) to Bool. " +
                   (floatBlend + floatOther > 0
                       ? $"The remaining {floatBlend + floatOther} float(s) are continuous (radials/blend trees) " +
                         "and can only be reduced by a structural compressor or by making them local."
                       : "All convertible.");
        }

        private static void RetypeToBool(AnimatorController controller, HashSet<string> convert)
        {
            var ps = controller.parameters;
            foreach (var p in ps)
            {
                if (!convert.Contains(p.name)) continue;
                var on = p.defaultFloat >= 0.5f;
                p.type = AnimatorControllerParameterType.Bool;
                p.defaultBool = on;
            }
            controller.parameters = ps; // reassign so the change persists
        }

        private static void RewriteConditions(AnimatorController controller, HashSet<string> convert)
        {
            foreach (var layer in controller.layers)
                WalkMachine(layer.stateMachine, convert);
        }

        private static void WalkMachine(AnimatorStateMachine sm, HashSet<string> convert)
        {
            FixTransitions(sm.anyStateTransitions, convert);
            FixTransitions(sm.entryTransitions, convert);

            foreach (var cs in sm.states)
                FixTransitions(cs.state.transitions, convert);

            foreach (var child in sm.stateMachines)
            {
                FixTransitions(sm.GetStateMachineTransitions(child.stateMachine), convert);
                WalkMachine(child.stateMachine, convert);
            }
        }

        private static void FixTransitions(IEnumerable<AnimatorTransitionBase> transitions, HashSet<string> convert)
        {
            foreach (var t in transitions)
            {
                var conds = t.conditions;
                var changed = false;
                for (var i = 0; i < conds.Length; i++)
                {
                    if (!convert.Contains(conds[i].parameter)) continue;
                    // Float Greater "> threshold" → the param is on (Bool If).
                    // Float Less    "< threshold" → the param is off (Bool IfNot).
                    conds[i].mode = conds[i].mode == AnimatorConditionMode.Greater
                        ? AnimatorConditionMode.If
                        : AnimatorConditionMode.IfNot;
                    conds[i].threshold = 0f;
                    changed = true;
                }
                if (changed) t.conditions = conds;
            }
        }

        private static void CollectUsage(AnimatorController controller, HashSet<string> blendParams,
                                         HashSet<string> motionParams, Dictionary<string, List<float>> conds)
        {
            foreach (var layer in controller.layers)
                CollectMachine(layer.stateMachine, blendParams, motionParams, conds);
        }

        private static void CollectMachine(AnimatorStateMachine sm, HashSet<string> blendParams,
                                           HashSet<string> motionParams, Dictionary<string, List<float>> conds)
        {
            CollectConds(sm.anyStateTransitions, conds);
            CollectConds(sm.entryTransitions, conds);

            foreach (var cs in sm.states)
            {
                var s = cs.state;
                CollectConds(s.transitions, conds);
                if (s.speedParameterActive) motionParams.Add(s.speedParameter);
                if (s.timeParameterActive) motionParams.Add(s.timeParameter);
                if (s.mirrorParameterActive) motionParams.Add(s.mirrorParameter);
                if (s.cycleOffsetParameterActive) motionParams.Add(s.cycleOffsetParameter);
                CollectMotion(s.motion, blendParams);
            }

            foreach (var child in sm.stateMachines)
            {
                CollectConds(sm.GetStateMachineTransitions(child.stateMachine), conds);
                CollectMachine(child.stateMachine, blendParams, motionParams, conds);
            }
        }

        private static void CollectConds(IEnumerable<AnimatorTransitionBase> transitions,
                                         Dictionary<string, List<float>> conds)
        {
            foreach (var t in transitions)
                foreach (var c in t.conditions)
                {
                    if (string.IsNullOrEmpty(c.parameter)) continue;
                    if (!conds.TryGetValue(c.parameter, out var list))
                        conds[c.parameter] = list = new List<float>();
                    list.Add(c.threshold);
                }
        }

        private static void CollectMotion(Motion motion, HashSet<string> blendParams)
        {
            if (!(motion is BlendTree tree)) return;
            if (!string.IsNullOrEmpty(tree.blendParameter)) blendParams.Add(tree.blendParameter);
            if (!string.IsNullOrEmpty(tree.blendParameterY)) blendParams.Add(tree.blendParameterY);
            foreach (var ch in tree.children)
            {
                if (!string.IsNullOrEmpty(ch.directBlendParameter)) blendParams.Add(ch.directBlendParameter);
                CollectMotion(ch.motion, blendParams);
            }
        }
    }
}
