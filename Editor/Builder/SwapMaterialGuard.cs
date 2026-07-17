using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Makes material-swap targets pass the CCK's build-blocking validators.
    ///
    /// A material referenced ONLY by a MaterialSwap action was never on a renderer, so the user was never
    /// pushed to fix its import settings — but the swap clip pulls it into the build, where two ERROR-level
    /// CCK validations reject the whole upload ("Build asset failed content validation"):
    ///   • "Requires Streaming Mipmaps" — every texture used must have streaming mipmaps enabled,
    ///   • "Missing or Broken Shaders".
    /// This mirrors the CCK's own autofix for the first (enable streaming mipmaps on the swap materials'
    /// textures) and reports the second with the material named, since a broken shader can't be auto-fixed.
    /// Also flags swap targets that aren't saved assets (a scene-instance material can't ship in a bundle).
    /// </summary>
    internal static class SwapMaterialGuard
    {
        public static void Run(GameObject root, BuildLog log)
        {
            var mats = CollectSwapMaterials(root);
            if (mats.Count == 0) return;

            int fixedTex = 0;
            var badShaders = new List<string>();
            var notAssets = new List<string>();

            foreach (var m in mats)
            {
                if (!EditorUtility.IsPersistent(m))
                {
                    notAssets.Add(m.name);
                    continue;
                }

                if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader" ||
                    ShaderUtil.GetShaderMessages(m.shader).Any(msg =>
                        msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                    badShaders.Add($"'{m.name}' ({(m.shader != null ? m.shader.name : "no shader")})");

                foreach (var prop in m.GetTexturePropertyNames())
                {
                    var tex = m.GetTexture(prop) as Texture2D;
                    if (tex == null) continue;
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
                    if (imp == null || (imp.streamingMipmaps && imp.mipmapEnabled)) continue;
                    imp.mipmapEnabled = true;
                    imp.streamingMipmaps = true;
                    imp.SaveAndReimport();
                    fixedTex++;
                }
            }

            if (fixedTex > 0)
                log.Info($"Material swaps: enabled streaming mipmaps on {fixedTex} texture(s) of swap-target " +
                         "materials — the CCK's 'Requires Streaming Mipmaps' validator blocks the build otherwise.");
            if (badShaders.Count > 0)
                log.Error("Material-swap target(s) with broken/missing shaders — the CCK validator will REJECT " +
                          "this build: " + string.Join("; ", badShaders) + ". Fix or re-lock these materials, " +
                          "or remove them from the swap.");
            if (notAssets.Count > 0)
                log.Error("Material-swap target(s) that are not saved assets (scene instances can't ship in a " +
                          "bundle): " + string.Join("; ", notAssets) + ". Save them as .mat assets and re-pick them.");
        }

        /// <summary>Every distinct material referenced by a MaterialSwap action anywhere on the avatar
        /// (toggles, slider min/max states, mode lists).</summary>
        public static List<Material> CollectSwapMaterials(GameObject root)
        {
            var states = new List<FuryState>();
            foreach (var t in root.GetComponentsInChildren<CVRFuryToggle>(true)) states.Add(t.state);
            foreach (var s in root.GetComponentsInChildren<CVRFurySlider>(true)) { states.Add(s.minState); states.Add(s.maxState); }
            foreach (var m in root.GetComponentsInChildren<CVRFuryModes>(true))
                foreach (var mode in m.modes) states.Add(mode.state);

            return states.Where(s => s?.actions != null)
                         .SelectMany(s => s.actions)
                         .Where(a => a.type == FuryAction.ActionType.MaterialSwap && a.material != null)
                         .Select(a => a.material)
                         .Distinct()
                         .ToList();
        }
    }
}
