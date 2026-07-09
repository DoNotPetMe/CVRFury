using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Bakes each Blendshape Logic rule into an animator layer: an Apply state (blendshape at the rule
    /// value) entered when ALL conditions hold, and a Rest state (blendshape at its current scene value)
    /// otherwise. Conditions are built against the SAME Bool parameters the CVRFury toggles allocated for
    /// the referenced GameObjects (recorded in <see cref="BuildContext.ToggleParamByObject"/>), so the
    /// in-game logic tracks the menu toggles exactly — coat on + bra on → squish applies; anything else →
    /// back to normal.
    /// </summary>
    internal sealed class BlendshapeLogicBuilder : FeatureBuilder<CVRFuryBlendshapeLogic>
    {
        protected override void Build(BuildContext ctx, CVRFuryBlendshapeLogic f)
        {
            if (f.rules == null || f.rules.Count == 0) return;
            var controller = ctx.GetOrCreateController();
            int built = 0, ruleIdx = 0;

            foreach (var rule in f.rules)
            {
                ruleIdx++;
                if (rule == null || rule.renderer == null || string.IsNullOrEmpty(rule.blendShape)) continue;
                var conds = (rule.when ?? new List<CVRFuryBlendshapeLogic.Condition>())
                            .Where(c => c != null && c.obj != null).ToList();
                if (conds.Count == 0)
                {
                    ctx.Log.Warning($"Blendshape Logic rule {ruleIdx} ('{rule.blendShape}') has no conditions; skipped.");
                    continue;
                }

                // Resolve each condition object to the toggle parameter that drives it.
                var resolved = new List<(string param, bool mustBeOn)>();
                var unresolved = new List<string>();
                foreach (var c in conds)
                {
                    if (ctx.ToggleParamByObject.TryGetValue(c.obj, out var p)) resolved.Add((p, c.mustBeOn));
                    else unresolved.Add(c.obj.name);
                }
                if (unresolved.Count > 0)
                {
                    ctx.Log.Warning($"Blendshape Logic rule {ruleIdx}: no CVRFury Toggle drives " +
                                    $"{string.Join(", ", unresolved)} — add a Toggle for it (the rule watches " +
                                    "that toggle's menu state). Rule skipped.");
                    continue;
                }

                // Apply clip = blendshape at the rule value; Rest clip = its current scene value.
                var action = new FuryAction
                {
                    type = FuryAction.ActionType.BlendShape,
                    blendShapeRenderer = rule.renderer,
                    blendShape = rule.blendShape,
                    blendShapeValue = rule.value,
                };
                var state = new FuryState { actions = new List<FuryAction> { action } };
                var label = string.IsNullOrEmpty(rule.note) ? rule.blendShape : rule.note;
                var applyClip = ClipBuilder.Build(ctx.RootTransform, state, $"BlendLogic_{label}_Apply");
                var restClip = ClipBuilder.BuildResting(ctx.RootTransform, state, $"BlendLogic_{label}_Rest");
                ctx.Assets.Save(applyClip, applyClip.name);
                ctx.Assets.Save(restClip, restClip.name);

                AddRuleLayer(controller, $"CVRFury BlendLogic {label}", resolved, restClip, applyClip);
                built++;
            }

            if (built > 0)
                ctx.Log.Info($"Blendshape Logic: built {built} rule layer(s) on '{f.gameObject.name}'.");
        }

        /// <summary>Two-state layer: Rest → Apply when ALL conditions hold (one transition, AND of
        /// conditions); Apply → Rest when ANY condition breaks (one transition per negated condition).</summary>
        private static void AddRuleLayer(AnimatorController c, string layerName,
                                         List<(string param, bool mustBeOn)> conds,
                                         AnimationClip restClip, AnimationClip applyClip)
        {
            foreach (var (param, _) in conds) AnimatorUtil.EnsureBoolParam(c, param, false);

            var name = AnimatorUtil.UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            var rest = sm.AddState("Rest");
            rest.motion = restClip;
            rest.writeDefaultValues = false;
            var apply = sm.AddState("Apply");
            apply.motion = applyClip;
            apply.writeDefaultValues = false;
            sm.defaultState = rest;

            var toApply = rest.AddTransition(apply);
            toApply.hasExitTime = false;
            toApply.hasFixedDuration = true;
            toApply.duration = 0.1f; // small blend so the shape eases instead of popping
            foreach (var (param, mustBeOn) in conds)
                toApply.AddCondition(mustBeOn ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);

            foreach (var (param, mustBeOn) in conds)
            {
                var back = apply.AddTransition(rest);
                back.hasExitTime = false;
                back.hasFixedDuration = true;
                back.duration = 0.1f;
                back.AddCondition(mustBeOn ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0f, param);
            }
        }
    }
}
