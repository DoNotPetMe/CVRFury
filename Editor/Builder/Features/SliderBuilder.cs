using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;

namespace CVRFury.Builder
{
    internal sealed class SliderBuilder : FeatureBuilder<CVRFurySlider>
    {
        protected override void Build(BuildContext ctx, CVRFurySlider f)
        {
            var displayName = MenuLeaf(f.menuPath, f.gameObject.name);
            var param = ctx.AllocateParam(
                !string.IsNullOrEmpty(f.parameterName) ? f.parameterName : displayName);
            var controller = ctx.GetOrCreateController();

            // Both endpoints must cover the same bindings so the blend is well-defined.
            var union = new List<FuryAction>();
            if (f.minState?.actions != null) union.AddRange(f.minState.actions);
            if (f.maxState?.actions != null) union.AddRange(f.maxState.actions);

            // If no explicit min state, value 0 = the resting scene state of whatever max changes.
            var zeroState = (f.minState != null && !f.minState.IsEmpty) ? f.minState : null;
            var zeroClip = ClipBuilder.BuildExclusive(ctx.RootTransform, zeroState, union, $"{displayName}_0");
            var oneClip = ClipBuilder.BuildExclusive(ctx.RootTransform, f.maxState, union, $"{displayName}_1");
            ctx.Assets.Save(zeroClip, zeroClip.name);
            ctx.Assets.Save(oneClip, oneClip.name);

            AnimatorUtil.AddBlendTreeLayer(controller, $"CVRFury {displayName}", param,
                zeroClip, oneClip, f.defaultValue, ctx.Assets);

            // CRITICAL: the AAS entry must carry the min/max clips itself. The CCK regenerates the AAS
            // animator from the entries at upload (nondeterministically), and an entry without clips
            // regenerates as an instant snap (or nothing) instead of a gradual blend — the "slider jumps
            // straight to max" / "hue does nothing" bug. With the clips on the entry, whichever animator
            // survives (ours or the CCK's regenerated one) blends correctly.
            if (!ctx.Avatar.AddSlider(displayName, param, f.defaultValue, f.localOnly, zeroClip, oneClip))
                ctx.Log.Warning($"Slider '{displayName}' blends correctly but could not be added to " +
                                "the in-game menu (AAS write failed).");
        }

        private static string MenuLeaf(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            var leaf = path.Split('/').Last().Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
