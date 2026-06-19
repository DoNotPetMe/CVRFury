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

                // Auto-map the 15 viseme blendshapes from VRChat's descriptor so the user never has to click
                // the CCK's "Auto Select Visemes" button — doing that after the controller is built can make
                // the CCK regenerate the AAS animator and drop CVRFury's toggle/slider layers.
                var vrcVisemes = Reflect.GetField(d, VrcNames.Desc_VisemeBlendShapes) as string[];
                int n = ctx.Cvr.SetVisemeBlendshapes(vrcVisemes);
                if (n > 0)
                    ctx.Log.Info($"Viseme face mesh assigned and {n} viseme blendshape(s) auto-mapped from VRChat.");
                else
                    ctx.Log.Info("Viseme face mesh assigned. Could not auto-map viseme blendshapes " +
                                 "(VRChat descriptor had none, or the CCK field differs) — set them in the CVRAvatar inspector if visemes look wrong.");
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
