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

            // Key on the discrete GestureLeftIdx / GestureRightIdx core Ints (−1 … 6) rather than
            // the analog GestureLeft/GestureRight Floats: exact Equals matching, and a fist is
            // detected regardless of how far the trigger is squeezed.
            var param = f.hand == CVRFuryGesture.Hand.Left
                ? CckNames.GestureLeftIdxParam
                : CckNames.GestureRightIdxParam;

            var controller = ctx.GetOrCreateController();
            var label = $"{f.hand} {f.gesture}";

            var onClip = ClipBuilder.Build(ctx.RootTransform, f.state, $"Gesture_{label}_On");
            var offClip = ClipBuilder.BuildResting(ctx.RootTransform, f.state, $"Gesture_{label}_Off");
            ctx.Assets.Save(onClip, onClip.name);
            ctx.Assets.Save(offClip, offClip.name);

            // Gesture parameters are driven by the platform, so there is no menu/AAS entry.
            AnimatorUtil.AddGestureLayer(controller, $"CVRFury Gesture {label}", param,
                ToCvrGestureIndex(f.gesture), offClip, onClip, f.transitionSeconds);
        }

        /// <summary>
        /// ChilloutVR's gesture indices (docs.chilloutvr.net → Animator Core Parameters) differ
        /// from VRChat's 0–7 order the enum is declared in: Open Hand = −1, Neutral = 0, Fist = 1,
        /// Thumbs Up = 2, Gun = 3, Point = 4, Peace = 5, Rock n Roll = 6. Building with the raw
        /// enum value made gestures fire on the wrong hand pose (or never, for ThumbsUp = 7).
        /// </summary>
        internal static int ToCvrGestureIndex(CVRFuryGesture.GestureType gesture)
        {
            switch (gesture)
            {
                case CVRFuryGesture.GestureType.HandOpen: return -1;
                case CVRFuryGesture.GestureType.Fist: return 1;
                case CVRFuryGesture.GestureType.ThumbsUp: return 2;
                case CVRFuryGesture.GestureType.HandGun: return 3;
                case CVRFuryGesture.GestureType.FingerPoint: return 4;
                case CVRFuryGesture.GestureType.Victory: return 5;
                case CVRFuryGesture.GestureType.RockAndRoll: return 6;
                default: return 0; // Neutral
            }
        }
    }
}
