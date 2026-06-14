using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Runs ChilloutVR's own Advanced Avatar Settings generation automatically — the equivalent of
    /// clicking "Create Controller" + "Attach Created Override to Avatar" in the CVRAvatar inspector.
    ///
    /// CVR doesn't run the Base Controller directly; it generates a fresh AnimatorController by calling
    /// each AAS entry's <c>SetupAnimator()</c> (which builds that entry's layer, parameter and states
    /// from its clips/targets) on top of the Base Controller, then attaches the result as the avatar's
    /// override controller.
    ///
    /// We generate onto a COPY of the merged CVR controller — the same controller the Base Controller
    /// points at — which already carries locomotion plus every merged FX toggle layer. <c>SetupAnimator</c>
    /// calls <c>AddParameter</c> per entry, so any parameter the base already declares is dropped first so
    /// it doesn't throw. Crucially we then attach the GENERATED controller to the avatar's
    /// <c>overrides</c>: earlier versions generated the controller but left the avatar pointing at the raw
    /// merged controller, so the generated layers were orphaned (toggles did nothing) and the merged
    /// controller's gesture/blend-tree validation errors were uploaded. Attaching the generated controller
    /// is exactly what worked when "Create Controller" + "Attach" were clicked by hand.
    /// </summary>
    internal sealed class AasControllerGenerator : IConverter
    {
        public string Title => "Generate Advanced Avatar Settings controller";
        public int Order => 90; // after Expressions (30) builds the entries, before FinalCleanup (100)
        public bool ShouldRun(ConversionContext ctx) => ctx.Options.expressions;

        public void Run(ConversionContext ctx)
        {
            var aas = ctx.Cvr?.AdvancedSettings;
            var entries = ctx.Cvr?.SettingsList;
            if (aas == null || entries == null || entries.Count == 0)
            {
                ctx.Log.Warning("AAS generation skipped: no Advanced Avatar Settings entries to build.");
                return;
            }

            // Base = the merged CVR controller (locomotion + every merged FX toggle layer). This is the
            // controller the avatar's Base Controller already points at, and the one "Create Controller"
            // extends. We generate onto a COPY so the documented base stays untouched.
            var baseController = ctx.Controller ?? ctx.FindCvrLocomotion();
            if (baseController == null)
            {
                ctx.Log.Warning("AAS generation skipped: no base controller (merge step produced nothing " +
                                "and no CVR locomotion controller was found).");
                return;
            }

            var genPath = ctx.Assets.NewPath(ctx.AvatarRoot.name + " AAS", "controller");
            AnimatorController gen = null;
            if (AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseController), genPath))
                gen = AssetDatabase.LoadAssetAtPath<AnimatorController>(genPath);
            if (gen == null)
            {
                ctx.Log.Warning("AAS generation failed: couldn't copy the base controller to the generated path.");
                return;
            }

            var folder = Path.GetDirectoryName(genPath)?.Replace('\\', '/');

            // Mirror the CCK's own CreateAASController exactly: an entry whose machine name is ALREADY a
            // parameter in the base controller is driven by the base's existing layer, so SetupAnimator
            // is NOT called for it (calling it would throw on AddParameter and/or add a redundant,
            // conflicting layer). Because our base is the merged controller — which already contains a
            // layer + parameter for every converted menu control — virtually every entry is satisfied by
            // the base; only genuinely-new parameters get a fresh generated layer.
            var baseParams = new System.Collections.Generic.HashSet<string>(gen.parameters.Select(p => p.name));

            int built = 0, reused = 0, failed = 0, i = 0;
            foreach (var entry in entries)
            {
                i++;
                if (entry == null) continue;
                var machineName = Reflect.GetField(entry, CckNames.Entry_MachineName) as string;
                var setting = Reflect.GetProperty(entry, CckNames.Entry_Setting);
                if (string.IsNullOrEmpty(machineName) || setting == null) { failed++; continue; }

                if (baseParams.Contains(machineName)) { reused++; continue; } // existing layer drives it

                // SetupAnimator(ref AnimatorController controller, string machineName, string folderPath, string fileName)
                var fileName = Sanitize(machineName) + "_" + i;
                var args = new object[] { gen, machineName, folder, fileName };
                if (Reflect.InvokeMethod(setting, CckNames.Setting_SetupAnimator, args))
                {
                    if (args[0] is AnimatorController updated) gen = updated; // honour the ref parameter
                    baseParams.Add(machineName);
                    built++;
                }
                else failed++;
            }

            // Don't break the hand-pose blend trees: a parameter used as a blend-tree parameter (e.g.
            // GestureLeft/GestureRight) must stay Float, so only Equals/NotEqual-gated params that are
            // NOT blend parameters are retyped to Int.
            SyncBitOptimizer.HarmonizeConditionParamTypes(gen, ctx.Log);
            EditorUtility.SetDirty(gen);
            AssetDatabase.SaveAssets();

            // Attach — the generated controller is what the avatar actually runs.
            Reflect.SetField(aas, CckNames.AdvancedSettings_BaseController, baseController);
            Reflect.SetField(aas, CckNames.AdvancedSettings_Animator, gen);
            // Belt and braces: keep the container marked initialized so the inspector never wipes it.
            Reflect.SetField(aas, CckNames.AdvancedSettings_Initialized, true);

            var aoc = new AnimatorOverrideController(gen) { name = gen.name + " (Override)" };
            AssetDatabase.AddObjectToAsset(aoc, gen);
            Reflect.SetField(aas, CckNames.AdvancedSettings_Overrides, aoc);

            // "Attach Created Override to Avatar": the avatar's override controller IS the generated one.
            ctx.Cvr.Overrides = aoc;

            var animator = ctx.AvatarRoot.GetComponent<Animator>();
            if (animator != null) animator.runtimeAnimatorController = gen;

            ctx.AasControllerAttached = true;
            ctx.Cvr.Persist();
            EditorUtility.SetDirty(gen);
            AssetDatabase.SaveAssets();

            ctx.Log.Info($"Generated AAS controller '{gen.name}': {reused} entr(y/ies) driven by the merged " +
                         $"controller's existing layers, {built} got a freshly generated layer, {failed} failed. " +
                         "Attached as the avatar's animator + override — the automatic equivalent of clicking " +
                         "Create Controller + Attach.");

            // X-ray the generated controller so the two field-only symptoms — the "motorcycle pose" and
            // toggles that do nothing — show up in the build log instead of only in-game.
            ControllerDiagnostics.Report(gen, entries.Cast<object>(), ctx.Log);
        }

        /// <summary>Make a machine name safe for an asset file name (CVR machine names contain
        /// '/', '#', '(' etc. which are illegal in paths and would make CreateAsset throw).</summary>
        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
