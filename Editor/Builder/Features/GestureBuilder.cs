using CVRFury.Components;

namespace CVRFury.Builder
{
    internal sealed class GestureBuilder : FeatureBuilder<CVRFuryGesture>
    {
        protected override void Build(BuildContext ctx, CVRFuryGesture f)
        {
            if (f.state == null || f.state.IsEmpty)
            {
                ctx.Log.Warning($"Gesture on '{f.gameObject.name}' has an empty state; skipped.");
                return;
            }

            var param = f.hand == CVRFuryGesture.Hand.Left
                ? CckNames.GestureLeftParam
                : CckNames.GestureRightParam;

            var controller = ctx.GetOrCreateController();
            var label = $"{f.hand} {f.gesture}";

            var onClip = ClipBuilder.Build(ctx.RootTransform, f.state, $"Gesture_{label}_On");
            var offClip = ClipBuilder.BuildResting(ctx.RootTransform, f.state, $"Gesture_{label}_Off");
            ctx.Assets.Save(onClip, onClip.name);
            ctx.Assets.Save(offClip, offClip.name);

            // Gesture parameters are driven by the platform, so there is no menu/AAS entry.
            AnimatorUtil.AddGestureLayer(controller, $"CVRFury Gesture {label}", param,
                (int)f.gesture, offClip, onClip, f.transitionSeconds);
        }
    }
}
