using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Orchestrates converting a VRChat avatar into a ChilloutVR-ready one. Reads VRChat data by
    /// reflection (the VRChat SDK must be present so its types load), ensures a CVRAvatar, runs the
    /// enabled converter steps in order, then optionally strips the leftover VRChat components.
    /// Conversion edits the avatar in place and writes generated controllers to a kept folder under
    /// "Assets/CVRFury Converted/".
    /// </summary>
    internal static class VRChatConverter
    {
        public static BuildLog Convert(GameObject avatarRoot, ConversionOptions options)
        {
            var assets = AssetSaver.CreatePersistent(avatarRoot.name);
            var ctx = new ConversionContext(avatarRoot, options, assets);

            // The VRChat SDK must be loaded for us to read the source avatar.
            var descType = Reflect.FindType(VrcNames.AvatarDescriptorType);
            if (descType == null)
            {
                ctx.Log.Error(
                    "VRChat SDK not found in this project. To convert, the VRChat Avatars SDK must be " +
                    "imported so CVRFury can read the avatar's data (descriptor, menu, parameters, " +
                    "PhysBones). Import it, convert, then remove it. If you only need to clean broken " +
                    "components, use Tools ▸ CVRFury ▸ Clean Missing Scripts instead.");
                Finish(ctx);
                return ctx.Log;
            }

            ctx.VrcDescriptor = avatarRoot.GetComponent(descType);
            if (ctx.VrcDescriptor == null)
            {
                ctx.Log.Error($"'{avatarRoot.name}' has no VRCAvatarDescriptor — nothing to convert.");
                Finish(ctx);
                return ctx.Log;
            }

            ctx.Cvr = CckAvatar.EnsureOn(avatarRoot);
            if (ctx.Cvr == null)
            {
                ctx.Log.Error("ChilloutVR CCK not found — cannot create a CVRAvatar.");
                Finish(ctx);
                return ctx.Log;
            }
            ctx.Cvr.EnsureAdvancedSettingsContainer();

            ctx.Log.Info($"CVRFury v{CckNames.CvrFuryVersion} — converting '{avatarRoot.name}' from VRChat to ChilloutVR…");

            foreach (var step in Steps().OrderBy(s => s.Order))
            {
                if (!step.ShouldRun(ctx)) continue;
                try
                {
                    step.Run(ctx);
                    ctx.Log.Info($"✓ {step.Title}");
                }
                catch (System.Exception e)
                {
                    ctx.Log.Error($"{step.Title} failed: {e.Message}");
                    if (CVRFurySettings.VerboseLogging) Debug.LogException(e);
                }
            }

            // Wire the merged controller onto the CVR avatar.
            if (ctx.Controller != null)
            {
                ctx.Cvr.BaseController = ctx.Controller;
                var overrides = new AnimatorOverrideController(ctx.Controller)
                {
                    name = ctx.Controller.name + " (Overrides)",
                };
                ctx.Assets.Save(overrides, overrides.name);
                ctx.Cvr.Overrides = overrides;
            }

            Finish(ctx);
            ctx.Log.Info("Conversion complete. Review the avatar, then remove the VRChat SDK if desired.");
            return ctx.Log;
        }

        private static IEnumerable<IConverter> Steps()
        {
            yield return new AvatarBasicsConverter();
            yield return new PhysBoneConverter();
            yield return new ExpressionsConverter();
            yield return new FinalCleanupConverter();
        }

        private static void Finish(ConversionContext ctx)
        {
            ctx.Assets.Flush();
            BuildLogWindow.Publish(ctx.Log);
        }
    }
}
