using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Last step: remove the VRChat components now that their data has been converted, plus any
    /// remaining broken/missing scripts. Anything in a <c>VRC.*</c> / <c>VRCSDK*</c> namespace is
    /// destroyed; the DynamicBones we just created (and CVR components) are untouched.
    /// </summary>
    internal sealed class FinalCleanupConverter : IConverter
    {
        public string Title => "Strip VRChat + broken components";
        public int Order => 100;
        public bool ShouldRun(ConversionContext ctx) => ctx.Options.stripVrcAndBroken;

        public void Run(ConversionContext ctx)
        {
            var removed = 0;
            foreach (var c in ctx.AvatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue; // missing scripts handled below
                var fn = c.GetType().FullName ?? "";
                if (fn.StartsWith("VRC.") || fn.StartsWith("VRCSDK") || fn.StartsWith("VRC_"))
                {
                    Object.DestroyImmediate(c, true);
                    removed++;
                }
            }

            var missing = MissingScriptCleaner.RemoveInHierarchy(ctx.AvatarRoot);
            ctx.Log.Info($"Removed {removed} VRChat component(s) and {missing} broken/missing-script component(s).");
        }
    }
}
