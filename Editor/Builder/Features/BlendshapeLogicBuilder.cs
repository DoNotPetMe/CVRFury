using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class BlendshapeLogicBuilder : FeatureBuilder<CVRFuryBlendshapeLogic>
    {
        protected override void Build(BuildContext ctx, CVRFuryBlendshapeLogic f)
        {
            var mesh = f.mesh != null ? f.mesh
                : f.GetComponent<SkinnedMeshRenderer>() ?? f.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (mesh == null || mesh.sharedMesh == null)
            {
                ctx.Log.Warning($"Blendshape Logic on '{f.gameObject.name}' has no mesh with " +
                                "blendshapes; skipped.");
                return;
            }

            var rules = (f.rules ?? new List<CVRFuryBlendshapeLogic.Rule>())
                .Where(r => r != null && r.assignments != null && r.assignments.Count > 0).ToList();
            if (rules.Count == 0)
            {
                ctx.Log.Warning($"Blendshape Logic on '{f.gameObject.name}' has no rules with " +
                                "assignments; skipped.");
                return;
            }

            var controller = ctx.GetOrCreateController();
            // Toggles build first (priority 0 < 40) and register their allocated parameter, and
            // components aren't stripped until every builder has run — so both are available here.
            var toggles = ctx.AvatarRoot.GetComponentsInChildren<CVRFuryToggle>(true);

            var built = 0;
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var label = string.IsNullOrEmpty(rule.name) ? $"Rule {i + 1}" : rule.name;

                if (rule.conditions == null || rule.conditions.Count == 0)
                {
                    ctx.Log.Warning($"Blendshape Logic rule '{label}' has no conditions; skipped.");
                    continue;
                }

                var gates = new List<(string param, bool expected)>();
                var ok = true;
                foreach (var cond in rule.conditions)
                {
                    if (!TryResolveCondition(ctx, toggles, cond, out var gate))
                    {
                        ctx.Log.Warning($"Blendshape Logic rule '{label}': condition object " +
                                        $"'{(cond?.target != null ? cond.target.name : "(none)")}' isn't " +
                                        "shown/hidden by any CVRFury Toggle on this avatar, so the rule " +
                                        "can't be keyed to a synced parameter; rule skipped.");
                        ok = false;
                        break;
                    }
                    gates.Add(gate);
                }
                if (!ok) continue;

                var state = ToFuryState(mesh, rule, ctx.Log, label);
                if (state.IsEmpty)
                {
                    ctx.Log.Warning($"Blendshape Logic rule '{label}' has no valid blendshape " +
                                    "assignments; skipped.");
                    continue;
                }

                var onClip = ClipBuilder.Build(ctx.RootTransform, state, $"BlendshapeLogic_{label}_On");
                var offClip = ClipBuilder.BuildResting(ctx.RootTransform, state, $"BlendshapeLogic_{label}_Off");
                ctx.Assets.Save(onClip, onClip.name);
                ctx.Assets.Save(offClip, offClip.name);

                AnimatorUtil.AddMultiConditionBoolLayer(controller,
                    $"CVRFury Blendshape Logic {label}", gates, offClip, onClip, f.transitionSeconds);
                built++;
            }

            if (built > 0)
                ctx.Log.Info($"Blendshape Logic on '{f.gameObject.name}' built {built} rule layer(s).");
        }

        /// <summary>Map a condition GameObject to the Bool parameter of the CVRFury Toggle that
        /// shows/hides it, and the parameter value the condition requires.</summary>
        private static bool TryResolveCondition(BuildContext ctx, CVRFuryToggle[] toggles,
                                                CVRFuryBlendshapeLogic.Condition cond,
                                                out (string param, bool expected) gate)
        {
            gate = default;
            if (cond?.target == null) return false;

            foreach (var t in toggles)
            {
                if (t.state?.actions == null) continue;
                foreach (var a in t.state.actions)
                {
                    if (a.type != FuryAction.ActionType.ObjectToggle || a.targetObject != cond.target)
                        continue;
                    if (!ctx.FeatureParams.TryGetValue(t, out var param)) continue;
                    // Toggle ON drives the object to a.targetState; the condition asks for
                    // mustBeActive — so the parameter must equal (targetState == mustBeActive).
                    gate = (param, a.targetState == cond.mustBeActive);
                    return true;
                }
            }
            return false;
        }

        private static FuryState ToFuryState(SkinnedMeshRenderer mesh, CVRFuryBlendshapeLogic.Rule rule,
                                             BuildLog log, string label)
        {
            var state = new FuryState();
            foreach (var a in rule.assignments)
            {
                if (a == null || string.IsNullOrEmpty(a.blendshape)) continue;
                if (mesh.sharedMesh.GetBlendShapeIndex(a.blendshape) < 0)
                {
                    log.Warning($"Blendshape Logic rule '{label}': blendshape '{a.blendshape}' not " +
                                $"found on '{mesh.name}'; assignment skipped.");
                    continue;
                }
                state.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.BlendShape,
                    blendShapeRenderer = mesh,
                    blendShape = a.blendshape,
                    blendShapeValue = a.value,
                });
            }
            return state;
        }
    }
}
