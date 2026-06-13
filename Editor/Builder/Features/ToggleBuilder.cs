using CVRFury.Components;

namespace CVRFury.Builder
{
    internal sealed class ToggleBuilder : FeatureBuilder<CVRFuryToggle>
    {
        protected override void Build(BuildContext ctx, CVRFuryToggle f)
        {
            var displayName = MenuLeaf(f.menuPath, f.gameObject.name);
            var param = ctx.AllocateParam(
                !string.IsNullOrEmpty(f.parameterName) ? f.parameterName : displayName);

            var controller = ctx.GetOrCreateController();

            var onClip = ClipBuilder.Build(ctx.RootTransform, f.state, $"{displayName}_On");
            var offClip = ClipBuilder.BuildResting(ctx.RootTransform, f.state, $"{displayName}_Off");
            ctx.Assets.Save(onClip, onClip.name);
            ctx.Assets.Save(offClip, offClip.name);

            AnimatorUtil.AddToggleLayer(
                controller,
                layerName: $"CVRFury {displayName}",
                param: param,
                offClip: offClip,
                onClip: onClip,
                transitionSeconds: f.transitionSeconds,
                defaultOn: f.defaultOn);

            // Expose it in the in-game Advanced Settings menu as a synced toggle.
            if (!ctx.Avatar.AddToggle(displayName, param, f.defaultOn, f.localOnly))
                ctx.Log.Warning($"Toggle '{displayName}' animates correctly but could not be added " +
                                $"to the in-game menu (AAS write failed).");
        }

        private static string MenuLeaf(string menuPath, string fallback)
        {
            if (string.IsNullOrEmpty(menuPath)) return fallback;
            var parts = menuPath.Split('/');
            var leaf = parts[parts.Length - 1].Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
