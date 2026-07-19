using System.Linq;
using CVRFury.Components;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class FullControllerBuilder : FeatureBuilder<CVRFuryFullController>
    {
        protected override void Build(BuildContext ctx, CVRFuryFullController f)
        {
            var dst = ctx.GetOrCreateController();

            foreach (var rac in f.controllers)
            {
                var src = Resolve(rac);
                if (src == null)
                {
                    ctx.Log.Warning("Full Controller entry is empty or not an AnimatorController; skipped.");
                    continue;
                }
                // Rename merged params through the SAME sanitizer the AAS entries use — VRCFury-style names
                // ("Nsfw/(s-b)Toy") are invalid CCK machine names, and the menu entry and the animator param
                // must stay identical for the toggle to drive the layer.
                ControllerMerger.Merge(dst, src, ctx.Assets, f.parameterPrefix, ctx.Log,
                                       renameParameter: CckAvatar.SanitizeMachineName);
            }

            // Expose requested parameters in the in-game menu.
            foreach (var po in f.parameters)
            {
                if (!po.exposeToMenu || string.IsNullOrEmpty(po.name)) continue;

                var machine = CckAvatar.SanitizeMachineName(
                    string.IsNullOrEmpty(f.parameterPrefix) ? po.name : f.parameterPrefix + po.name);
                if (f.createMissingParameters && !dst.parameters.Any(p => p.name == machine))
                    AnimatorUtil.EnsureFloatParam(dst, machine);

                var display = MenuLeaf(po.menuPath, po.name);
                switch (po.menuType)
                {
                    case CVRFuryFullController.AasParamType.Slider:
                    case CVRFuryFullController.AasParamType.Dropdown:
                        ctx.Avatar.AddSlider(display, machine, 0f, po.localOnly);
                        break;
                    default:
                        ctx.Avatar.AddToggle(display, machine, false, po.localOnly);
                        break;
                }
            }
        }

        private static AnimatorController Resolve(RuntimeAnimatorController rac)
        {
            switch (rac)
            {
                case AnimatorController ac: return ac;
                case AnimatorOverrideController oc: return oc.runtimeAnimatorController as AnimatorController;
                default: return null;
            }
        }

        private static string MenuLeaf(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            var leaf = path.Split('/').Last().Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
