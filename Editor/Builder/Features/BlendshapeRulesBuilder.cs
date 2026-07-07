using System.Collections.Generic;
using CVRFury.Components;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class BlendshapeRulesBuilder : FeatureBuilder<CVRFuryBlendshapeRules>
    {
        protected override void Build(BuildContext ctx, CVRFuryBlendshapeRules f)
        {
            if (f.rules == null || f.rules.Count == 0) return;

            var controller = ctx.GetOrCreateController();

            // Group every rule's targets by (renderer, blendshape) — one animator layer per
            // unique shape, since only one clip can drive a given blendshape at a time.
            var byTarget = new Dictionary<(SkinnedMeshRenderer renderer, string shape),
                List<(CVRFuryBlendshapeRules.Rule rule, float value)>>();

            foreach (var rule in f.rules)
            {
                if (rule?.targets == null) continue;
                foreach (var t in rule.targets)
                {
                    if (t.renderer == null || string.IsNullOrEmpty(t.blendShape)) continue;
                    var key = (t.renderer, t.blendShape);
                    if (!byTarget.TryGetValue(key, out var list))
                    {
                        list = new List<(CVRFuryBlendshapeRules.Rule, float)>();
                        byTarget[key] = list;
                    }
                    list.Add((rule, t.value));
                }
            }

            if (byTarget.Count == 0)
            {
                ctx.Log.Warning($"Blendshape Rules '{f.gameObject.name}' has no valid targets " +
                                "(every rule needs a renderer + blendshape); skipped.");
                return;
            }

            int builtLayers = 0, skippedRules = 0;

            foreach (var kv in byTarget)
            {
                var (renderer, shape) = kv.Key;
                var displayName = $"{renderer.name}.{shape}";

                FuryState SingleAction(float value) => new FuryState
                {
                    actions = new List<FuryAction>
                    {
                        new FuryAction
                        {
                            type = FuryAction.ActionType.BlendShape,
                            blendShapeRenderer = renderer,
                            blendShape = shape,
                            blendShapeValue = value,
                        },
                    },
                };

                var entries = new List<(string label, List<AnimatorCondition> conditions, AnimationClip clip)>();

                foreach (var (rule, value) in kv.Value)
                {
                    var label = string.IsNullOrEmpty(rule.name) ? "Rule" : rule.name;

                    if (rule.conditions == null || rule.conditions.Count == 0)
                    {
                        ctx.Log.Warning($"Blendshape Rules: rule '{label}' (target '{displayName}') has " +
                                        "no conditions; skipped. Add at least one Toggle/Modes condition.");
                        skippedRules++;
                        continue;
                    }

                    var allConditions = new List<AnimatorCondition>();
                    var resolvable = true;

                    foreach (var cond in rule.conditions)
                    {
                        var resolved = ResolveCondition(ctx, label, cond, out var error);
                        if (resolved == null)
                        {
                            ctx.Log.Warning($"Blendshape Rules: rule '{label}' — {error}");
                            resolvable = false;
                            break;
                        }
                        allConditions.AddRange(resolved);
                    }

                    if (!resolvable)
                    {
                        skippedRules++;
                        continue;
                    }

                    var clip = ClipBuilder.Build(ctx.RootTransform, SingleAction(value), $"{displayName}_{label}");
                    ctx.Assets.Save(clip, clip.name);
                    entries.Add((label, allConditions, clip));
                }

                if (entries.Count == 0) continue; // nothing usable for this shape

                // The renderer's CURRENT scene weight is what the shape reverts to when no rule
                // matches — same "resting" convention CVRFury uses for every other toggle/mode/slider.
                var restClip = ClipBuilder.BuildResting(ctx.RootTransform, SingleAction(0f), $"{displayName}_Rest");
                ctx.Assets.Save(restClip, restClip.name);

                AnimatorUtil.AddConditionalStatesLayer(controller, $"CVRFury Blendshape {displayName}",
                    restClip, entries);
                builtLayers++;
            }

            ctx.Log.Info($"Blendshape Rules '{f.gameObject.name}': built {builtLayers} blendshape layer(s)" +
                         (skippedRules > 0
                             ? $"; {skippedRules} rule(s) skipped — see warnings above."
                             : "."));
        }

        /// <summary>
        /// AND-condition list for one Toggle/Modes reference, reusing whatever parameter that
        /// component already recorded when it built its own layer (<see cref="BuildContext.RecordParam"/>).
        /// Returns null (with <paramref name="error"/> set) if the reference is empty or hasn't
        /// produced a parameter — e.g. it's a plain GameObject with no CVRFury control on it.
        /// </summary>
        private static List<AnimatorCondition> ResolveCondition(BuildContext ctx, string ruleLabel,
            CVRFuryBlendshapeRules.Condition cond, out string error)
        {
            error = null;

            if (cond.kind == CVRFuryBlendshapeRules.Condition.Kind.Toggle)
            {
                if (cond.toggle == null)
                {
                    error = "has a condition with no Toggle assigned.";
                    return null;
                }
                if (!ctx.TryGetRecordedParam(cond.toggle, out var param))
                {
                    error = $"'{cond.toggle.gameObject.name}' Toggle hasn't produced a parameter " +
                            "(check the Build Log for that Toggle — it may be disabled or failed to build).";
                    return null;
                }
                return new List<AnimatorCondition>
                {
                    new AnimatorCondition
                    {
                        mode = cond.requiredOn ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                        parameter = param,
                    },
                };
            }

            if (cond.modes == null)
            {
                error = "has a condition with no Modes component assigned.";
                return null;
            }
            if (!ctx.TryGetRecordedParam(cond.modes, out var modeParam))
            {
                error = $"'{cond.modes.gameObject.name}' Modes hasn't produced a parameter " +
                        "(check the Build Log for that Modes component).";
                return null;
            }

            var count = cond.modes.modes?.Count ?? 0;
            if (count == 0)
            {
                error = $"'{cond.modes.gameObject.name}' Modes has no mode options to reference.";
                return null;
            }

            var i = Mathf.Clamp(cond.modeIndex, 0, count - 1);
            var result = new List<AnimatorCondition>();
            // Same float-window convention ModesBuilder itself builds its states with (see
            // AnimatorUtil.AddModesLayer): [i-0.5, i+0.5), open-ended at the very first/last index.
            if (i > 0) result.Add(new AnimatorCondition { mode = AnimatorConditionMode.Greater, threshold = i - 0.5f, parameter = modeParam });
            if (i < count - 1) result.Add(new AnimatorCondition { mode = AnimatorConditionMode.Less, threshold = i + 0.5f, parameter = modeParam });
            return result;
        }
    }
}
