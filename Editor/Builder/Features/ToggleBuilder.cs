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

            // Record which parameter drives each toggled GameObject, so Blendshape Logic rules can build
            // conditions ("coat AND bra both on") against the real allocated parameter names.
            if (f.state?.actions != null)
                foreach (var a in f.state.actions)
                    if (a.type == FuryAction.ActionType.ObjectToggle && a.targetObject != null &&
                        !ctx.ToggleParamByObject.ContainsKey(a.targetObject))
                        ctx.ToggleParamByObject[a.targetObject] = param;

            var controller = ctx.GetOrCreateController();

            var onClip = ClipBuilder.Build(ctx.RootTransform, f.state, $"{displayName}_On");
            var offClip = ClipBuilder.BuildResting(ctx.RootTransform, f.state, $"{displayName}_Off");
            ctx.Assets.Save(onClip, onClip.name);
            ctx.Assets.Save(offClip, offClip.name);

            // Bool-driven layer (If/IfNot on a Bool param) so the animator parameter TYPE matches the Bool
            // the AAS entry declares below. A Float-driven layer would make CVR sync the parameter as a
            // 32/64-bit float while the menu says Bool — wasting the synced-bit budget and mis-reporting it.
            AnimatorUtil.AddBoolToggleLayer(
                controller,
                layerName: $"CVRFury {displayName}",
                param: param,
                offClip: offClip,
                onClip: onClip,
                defaultOn: f.defaultOn,
                transitionSeconds: f.transitionSeconds);

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
