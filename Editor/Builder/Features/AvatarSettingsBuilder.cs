using CVRFury.Components;

namespace CVRFury.Builder
{
    internal sealed class AvatarSettingsBuilder : FeatureBuilder<CVRFuryAvatarSettings>
    {
        protected override void Build(BuildContext ctx, CVRFuryAvatarSettings f)
        {
            if (f.viewpoint != null) ctx.Avatar.SetViewPosition(f.viewpoint.position);
            if (f.voicePosition != null) ctx.Avatar.SetVoicePosition(f.voicePosition.position);
            if (f.faceMesh != null) ctx.Avatar.SetFaceMesh(f.faceMesh);

            ctx.Avatar.SetUseVisemes(f.enableVisemes);
            ctx.Avatar.SetUseBlink(f.enableBlink);
            ctx.Avatar.SetUseEyeMovement(f.enableEyeMovement);
        }
    }
}
