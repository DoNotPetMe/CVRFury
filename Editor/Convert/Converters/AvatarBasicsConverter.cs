using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>Maps the VRCAvatarDescriptor essentials onto the CVRAvatar: viewpoint, viseme face
    /// mesh + lipsync, blink, and eye movement. Values VRChat stores avatar-local map directly.</summary>
    internal sealed class AvatarBasicsConverter : IConverter
    {
        public string Title => "Avatar basics (viewpoint / visemes / blink / eyes)";
        public int Order => 10;
        public bool ShouldRun(ConversionContext ctx) => ctx.Options.avatarBasics;

        public void Run(ConversionContext ctx)
        {
            var d = ctx.VrcDescriptor;

            if (Reflect.GetField(d, VrcNames.Desc_ViewPosition) is Vector3 view)
            {
                // VRChat ViewPosition is avatar-local; CVR viewpoint is too.
                ctx.Cvr.SetViewPosition(view);
                // No VRChat equivalent for voice — seed it at the viewpoint so it isn't at the origin.
                ctx.Cvr.SetVoicePosition(view);
            }

            if (Reflect.GetField(d, VrcNames.Desc_VisemeMesh) is SkinnedMeshRenderer faceMesh)
            {
                ctx.Cvr.SetFaceMesh(faceMesh);
                ctx.Cvr.SetUseVisemes(true);
                ctx.Log.Info("Viseme face mesh assigned; CVR will auto-map visemes from it.");
            }

            var eye = Reflect.GetField(d, VrcNames.Desc_EyeLook);
            if (eye != null)
            {
                if (Reflect.GetField(eye, VrcNames.Eye_EyelidsMesh) is SkinnedMeshRenderer)
                    ctx.Cvr.SetUseBlink(true);
                ctx.Cvr.SetUseEyeMovement(true);
                ctx.Log.Info("Blink + eye movement enabled (verify the blink blendshapes in the CVRAvatar inspector).");
            }
        }
    }
}
