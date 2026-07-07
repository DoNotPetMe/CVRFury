using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class ModesBuilder : FeatureBuilder<CVRFuryModes>
    {
        protected override void Build(BuildContext ctx, CVRFuryModes f)
        {
            if (f.modes == null || f.modes.Count == 0)
            {
                ctx.Log.Warning($"Modes '{f.gameObject.name}' has no modes; skipped.");
                return;
            }

            var displayName = MenuLeaf(f.menuPath, f.gameObject.name);
            var param = ctx.AllocateParam(
                !string.IsNullOrEmpty(f.parameterName) ? f.parameterName : displayName);
            var controller = ctx.GetOrCreateController();

            // Union of every action across all modes → exclusive coverage.
            var allActions = f.modes.SelectMany(m => m.state?.actions ?? new List<FuryAction>()).ToList();

            var clips = new AnimationClip[f.modes.Count];
            for (var i = 0; i < f.modes.Count; i++)
            {
                var clip = ClipBuilder.BuildExclusive(ctx.RootTransform, f.modes[i].state, allActions,
                    $"{displayName}_{i}");
                ctx.Assets.Save(clip, clip.name);
                clips[i] = clip;
            }

            AnimatorUtil.AddModesLayer(controller, $"CVRFury {displayName}", param, clips,
                f.transitionSeconds, f.defaultMode);
            ctx.RecordParam(f, param);

            var options = f.modes.Select(m => string.IsNullOrEmpty(m.name) ? "Mode" : m.name).ToArray();
            if (!ctx.Avatar.AddDropdown(displayName, param, options,
                    Mathf.Clamp(f.defaultMode, 0, options.Length - 1), f.localOnly))
                ctx.Log.Warning($"Modes '{displayName}' animate correctly but could not be added to " +
                                "the in-game menu (AAS dropdown write failed).");
        }

        private static string MenuLeaf(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            var leaf = path.Split('/').Last().Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
