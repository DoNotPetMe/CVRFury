using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class PresetsBuilder : FeatureBuilder<CVRFuryPresets>
    {
        protected override void Build(BuildContext ctx, CVRFuryPresets f)
        {
            var presets = (f.presets ?? new List<CVRFuryPresets.Preset>())
                .Where(p => p != null).ToList();
            if (presets.Count == 0)
            {
                ctx.Log.Warning($"Presets '{f.gameObject.name}' has no presets; skipped.");
                return;
            }

            var displayName = MenuLeaf(f.menuPath, f.gameObject.name);
            var param = ctx.AllocateParam(
                !string.IsNullOrEmpty(f.parameterName) ? f.parameterName : displayName);
            var controller = ctx.GetOrCreateController();

            // Union of every binding ANY preset touches (referenced toggles' ON actions + extra
            // actions) → exclusive coverage: picking a preset resets everything it doesn't equip.
            var union = new List<FuryAction>();
            foreach (var p in presets)
            {
                foreach (var t in (p.toggles ?? new List<CVRFuryToggle>()).Where(t => t != null))
                    if (t.state?.actions != null) union.AddRange(t.state.actions);
                if (p.state?.actions != null) union.AddRange(p.state.actions);
            }
            if (union.Count == 0)
            {
                ctx.Log.Warning($"Presets '{displayName}' reference no toggles or actions; skipped.");
                return;
            }

            // Option 0 = "Custom": an empty clip that animates nothing, so the individual toggles
            // stay in manual control until a preset is selected.
            var clips = new AnimationClip[presets.Count + 1];
            clips[0] = new AnimationClip { name = $"{displayName}_Custom" };
            ctx.Assets.Save(clips[0], clips[0].name);

            for (var i = 0; i < presets.Count; i++)
            {
                var merged = new FuryState();
                foreach (var t in (presets[i].toggles ?? new List<CVRFuryToggle>()).Where(t => t != null))
                    if (t.state?.actions != null) merged.actions.AddRange(t.state.actions);
                if (presets[i].state?.actions != null) merged.actions.AddRange(presets[i].state.actions);

                var clip = ClipBuilder.BuildExclusive(ctx.RootTransform, merged, union,
                    $"{displayName}_{i + 1}");
                ctx.Assets.Save(clip, clip.name);
                clips[i + 1] = clip;
            }

            // Int-driven exclusive layer (Equals per option), matching the Int the AAS dropdown syncs.
            AnimatorUtil.AddModesLayer(controller, $"CVRFury {displayName}", param, clips,
                f.transitionSeconds, 0, useInt: true);

            var options = new[] { "Custom" }
                .Concat(presets.Select(p => string.IsNullOrEmpty(p.name) ? "Preset" : p.name))
                .ToArray();
            if (!ctx.Avatar.AddDropdown(displayName, param, options, 0, f.localOnly))
                ctx.Log.Warning($"Presets '{displayName}' animate correctly but could not be added " +
                                "to the in-game menu (AAS dropdown write failed).");
        }

        private static string MenuLeaf(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            var leaf = path.Split('/').Last().Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
