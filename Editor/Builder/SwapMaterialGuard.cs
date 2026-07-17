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
            foreach (var problem in FindSlotProblems(root))
                log.Warning("Material swap: " + problem);

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

        /// <summary>The two silent ways a material swap "does nothing" in-game:
        ///  • the slot index doesn't exist on that renderer (slot = 0-based index into ITS Materials list,
        ///    not a unique ID across swaps — a one-material hair mesh is slot 0), so the animation writes
        ///    to nothing;
        ///  • several independent toggles/sliders animate the SAME renderer+slot — each is its own animator
        ///    layer writing constantly, the topmost wins, and the rest appear dead. Exclusive variants
        ///    belong in ONE Modes dropdown.</summary>
        public static List<string> FindSlotProblems(GameObject root)
        {
            var problems = new List<string>();
            var writers = new Dictionary<(Renderer r, int slot), List<string>>();

            void Inspect(FuryState s, string who)
            {
                if (s?.actions == null) return;
                foreach (var a in s.actions)
                {
                    if (a.type != FuryAction.ActionType.MaterialSwap || a.materialRenderer == null) continue;
                    int slots = a.materialRenderer.sharedMaterials.Length;
                    if (a.materialSlot < 0 || a.materialSlot >= slots)
                    {
                        problems.Add($"{who}: slot {a.materialSlot} doesn't exist on '{a.materialRenderer.name}' " +
                                     $"(it has {slots} slot(s), numbered 0–{slots - 1}) — this swap animates NOTHING. " +
                                     "Slot = the position in THAT renderer's Materials list, not a unique ID.");
                        continue;
                    }
                    var key = (a.materialRenderer, a.materialSlot);
                    if (!writers.TryGetValue(key, out var l)) writers[key] = l = new List<string>();
                    if (!l.Contains(who)) l.Add(who);
                }
            }

            foreach (var t in root.GetComponentsInChildren<CVRFuryToggle>(true))
                Inspect(t.state, $"Toggle '{(string.IsNullOrEmpty(t.menuPath) ? t.gameObject.name : t.menuPath)}'");
            foreach (var s in root.GetComponentsInChildren<CVRFurySlider>(true))
            {
                Inspect(s.minState, $"Slider '{s.menuPath}'");
                Inspect(s.maxState, $"Slider '{s.menuPath}'");
            }
            foreach (var m in root.GetComponentsInChildren<CVRFuryModes>(true))
                foreach (var mode in m.modes)
                    Inspect(mode.state, $"Dropdown '{m.menuPath}'");

            foreach (var kv in writers.Where(kv => kv.Value.Count > 1))
                problems.Add($"'{kv.Key.r.name}' slot {kv.Key.slot} is animated by {kv.Value.Count} separate " +
                             $"controls ({string.Join(" + ", kv.Value)}) — each is its own animator layer, the " +
                             "topmost always wins, and the others appear DEAD. Put exclusive variants in ONE " +
                             "Modes dropdown instead.");
            return problems;
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
