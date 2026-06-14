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
    /// CVR doesn't run the Base Controller directly; it generates a fresh AnimatorController by
    /// calling each AAS entry's <c>SetupAnimator()</c> (which builds that entry's layer, parameter and
    /// states from its clips/targets) on top of a CLEAN base, then attaches it. Crucially that base
    /// must be clean: <c>SetupAnimator</c> calls <c>AddParameter</c> for each entry, so if the base
    /// already contains the parameter (as our merged VRChat FX controller did) generation throws and
    /// nothing is produced — which is why converted toggles did nothing. We therefore generate onto a
    /// copy of CVR's default locomotion controller and assign the result to the avatar's Animator.
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

            // Clean base — locomotion only. SetupAnimator adds each entry's parameter, so the base
            // must NOT already contain them (the merged FX controller did, which broke generation).
            var loco = ctx.FindCvrLocomotion();
            var genPath = ctx.Assets.NewPath(ctx.AvatarRoot.name + " AAS", "controller");
            AnimatorController gen;
            if (loco != null && AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(loco), genPath))
                gen = AssetDatabase.LoadAssetAtPath<AnimatorController>(genPath);
            else
                gen = AnimatorController.CreateAnimatorControllerAtPath(genPath);

            if (gen == null)
            {
                ctx.Log.Warning("AAS generation failed: couldn't create the generated controller asset.");
                return;
            }

            var folder = Path.GetDirectoryName(genPath)?.Replace('\\', '/');
            var existingParams = new System.Collections.Generic.HashSet<string>(gen.parameters.Select(p => p.name));

            int ok = 0, failed = 0, i = 0;
            foreach (var entry in entries)
            {
                i++;
                if (entry == null) continue;
                var machineName = Reflect.GetField(entry, CckNames.Entry_MachineName) as string;
                var setting = Reflect.GetProperty(entry, CckNames.Entry_Setting);
                if (string.IsNullOrEmpty(machineName) || setting == null) { failed++; continue; }

                // Defensive: if the clean base somehow already declares this parameter, drop it so
                // SetupAnimator's AddParameter doesn't throw.
                if (existingParams.Contains(machineName))
                    gen.parameters = gen.parameters.Where(p => p.name != machineName).ToArray();
                existingParams.Add(machineName);

                // SetupAnimator(ref AnimatorController controller, string machineName, string folderPath, string fileName)
                var fileName = Sanitize(machineName) + "_" + i;
                var args = new object[] { gen, machineName, folder, fileName };
                if (Reflect.InvokeMethod(setting, CckNames.Setting_SetupAnimator, args))
                {
                    if (args[0] is AnimatorController updated) gen = updated; // honour the ref parameter
                    ok++;
                }
                else failed++;
            }

            EditorUtility.SetDirty(gen);
            AssetDatabase.SaveAssets();

            // VRChat's gesture params (Int, Equals conditions) clash with CVR's Float gesture params
            // once both are present; make any Equals/NotEqual-gated parameter Int so the controller
            // validates cleanly.
            SyncBitOptimizer.HarmonizeConditionParamTypes(gen, ctx.Log);
            EditorUtility.SetDirty(gen);
            AssetDatabase.SaveAssets();

            // Attach: clean base stays as the documented input, the generated controller is what runs.
            Reflect.SetField(aas, CckNames.AdvancedSettings_BaseController, loco);
            Reflect.SetField(aas, CckNames.AdvancedSettings_Animator, gen);

            var aoc = new AnimatorOverrideController(gen) { name = gen.name + " (Override)" };
            AssetDatabase.AddObjectToAsset(aoc, gen);
            Reflect.SetField(aas, CckNames.AdvancedSettings_Overrides, aoc);

            var animator = ctx.AvatarRoot.GetComponent<Animator>();
            if (animator != null) animator.runtimeAnimatorController = gen;

            EditorUtility.SetDirty(ctx.Cvr.Component);
            AssetDatabase.SaveAssets();

            ctx.Log.Info($"Generated AAS controller '{gen.name}': {ok} entr(y/ies) built, {failed} skipped " +
                         (loco != null ? "(seeded from CVR locomotion). " : "(no locomotion base found). ") +
                         "Attached as the avatar's animator + override — toggles should now drive in-game " +
                         "without clicking Create Controller manually.");
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
