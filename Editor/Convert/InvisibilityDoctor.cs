using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Diagnoses (and where possible fixes) the "my avatar is invisible in CVR but fine in Unity" problem.
    ///
    /// The usual culprit on protected avatars (Kanna Protecc / AvaCrypt / "Gonso"-style systems): the mesh
    /// is scrambled and only renders correctly when decryption KEYS are applied. In VRChat the keys arrive
    /// as saved avatar parameters at runtime; in the editor they exist as scene state (blendshape weights /
    /// material floats) — which is why it looks right in Unity. In CVR nothing supplies the keys, and any
    /// animator clip that resets those blendshapes to their (scrambled) defaults makes the avatar invisible.
    ///
    /// The user owns the avatar and its key, so baking THEIR key's scene state to survive upload is
    /// legitimate. Beyond protection, this also catches the mundane causes: disabled renderers, zero scale,
    /// missing root bones (bounds culling), and clips that zero non-zero scene blendshapes.
    /// </summary>
    internal static class InvisibilityDoctor
    {
        public static string Diagnose(GameObject avatar)
        {
            if (avatar == null) return "Select your avatar first.";
            var sb = new System.Text.StringBuilder();
            int issues = 0;

            // 1) Renderers disabled / zero scale / missing root bone (bounds culling).
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) { sb.AppendLine($"✗ Renderer disabled: {r.name}"); issues++; }
                if (!r.gameObject.activeInHierarchy) { sb.AppendLine($"✗ Object inactive: {r.name}"); issues++; }
                for (var t = r.transform; t != null && t != avatar.transform.parent; t = t.parent)
                    if (t.localScale.sqrMagnitude < 1e-10f)
                    { sb.AppendLine($"✗ Zero scale on '{t.name}' hides {r.name}"); issues++; break; }
                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.rootBone == null)
                    { sb.AppendLine($"! {r.name}: no Root Bone — bounds can collapse and the mesh gets culled. " +
                                    "Set Root Bone (usually Hips) or enable Update When Offscreen."); issues++; }
                    if (smr.sharedMesh == null)
                    { sb.AppendLine($"✗ {r.name}: no mesh assigned."); issues++; }
                }
            }

            // 2) Protection-key heuristic: meshes carrying non-zero blendshape weights whose names look like
            //    generated keys, plus what would happen if an animator reset them.
            var keyed = FindKeyedRenderers(avatar);
            foreach (var (smr, keys) in keyed)
            {
                sb.AppendLine($"! {smr.name}: {keys.Count} non-zero blendshape(s) that look like protection keys " +
                              $"({string.Join(", ", keys.Take(4).Select(k => smr.sharedMesh.GetBlendShapeName(k)))}" +
                              (keys.Count > 4 ? " …" : "") + "). If anything resets these in game, the mesh scrambles/vanishes.");
                issues++;
            }

            // 3) Animator clips that fight the scene's non-zero blendshape weights (the in-game reset).
            var fights = FindWeightFights(avatar);
            foreach (var f in fights) { sb.AppendLine("✗ " + f); issues++; }

            if (issues == 0)
                return "No local cause found. If the avatar is still invisible in CVR, the protection shader " +
                       "likely needs its keys as runtime parameters (which CVR never supplies) — use " +
                       "\"Bake scene look\" below, and if that's not enough the mesh itself needs the key " +
                       "baked in (tell me and we'll add mesh baking).";
            return $"{issues} potential cause(s):\n{sb}\nFix: \"Bake scene look\" pins the current (working) " +
                   "blendshape state so nothing in the upload resets it.";
        }

        /// <summary>Make the scene's current (visible/decrypted) look survive the upload: serialize current
        /// blendshape weights as the renderer defaults, and strip curves from CVRFury-generated clips that
        /// would drive those non-zero shapes to a different value.</summary>
        public static string BakeSceneLook(GameObject avatar)
        {
            if (avatar == null) return "Select your avatar first.";

            int pinned = 0;
            var protectedBindings = new HashSet<(string path, string prop)>();
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                Undo.RecordObject(smr, "Bake scene look");
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    var w = smr.GetBlendShapeWeight(i);
                    if (Mathf.Abs(w) < 0.0001f) continue;
                    smr.SetBlendShapeWeight(i, w); // re-assert → serialized as the default the bundle ships
                    protectedBindings.Add((AnimationUtility.CalculateTransformPath(smr.transform, avatar.transform),
                                           "blendShape." + mesh.GetBlendShapeName(i)));
                    pinned++;
                }
                EditorUtility.SetDirty(smr);
            }

            // Strip curves in OUR generated clips that would move a pinned shape (never touches user assets).
            int stripped = 0;
            foreach (var dir in new[] { "Assets/CVRFury Generated", "Assets/CVRFury Converted" })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { dir }))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    if (clip == null) continue;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        if (protectedBindings.Contains((b.path, b.propertyName)))
                        {
                            AnimationUtility.SetEditorCurve(clip, b, null);
                            stripped++;
                        }
                }
            }
            AssetDatabase.SaveAssets();

            return $"Pinned {pinned} non-zero blendshape weight(s) as defaults and removed {stripped} generated " +
                   "animation curve(s) that would have reset them in game. Re-upload and check. If it's STILL " +
                   "invisible, bake the keys permanently: Advanced ▸ \"Bake avatar-lock keys into the mesh\".";
        }

        private static List<(SkinnedMeshRenderer smr, List<int> keys)> FindKeyedRenderers(GameObject avatar)
        {
            var res = new List<(SkinnedMeshRenderer, List<int>)>();
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var keys = new List<int>();
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    if (Mathf.Abs(smr.GetBlendShapeWeight(i)) < 0.0001f) continue;
                    if (LooksLikeKey(mesh.GetBlendShapeName(i))) keys.Add(i);
                }
                if (keys.Count >= 2) res.Add((smr, keys)); // protection systems use several keys
            }
            return res;
        }

        // Key shapes tend to be generated names: hex-ish strings, "Key…", long no-vowel tokens.
        private static bool LooksLikeKey(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return false;
            var lower = n.ToLowerInvariant();
            if (lower.StartsWith("key") || lower.Contains("crypt") || lower.Contains("protec")) return true;
            if (n.Length >= 8 && n.All(ch => "0123456789abcdefABCDEF_-".IndexOf(ch) >= 0)) return true;
            int letters = n.Count(char.IsLetter), vowels = lower.Count(c => "aeiou".IndexOf(c) >= 0);
            return letters >= 8 && vowels == 0; // long consonant/hash-like soup
        }

        private static List<string> FindWeightFights(GameObject avatar)
        {
            var res = new List<string>();
            var anim = avatar.GetComponentInChildren<Animator>();
            var ctrl = anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
            if (ctrl == null) return res;

            // Scene truth: path+shape → current weight.
            var scene = new Dictionary<(string, string), float>();
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var path = AnimationUtility.CalculateTransformPath(smr.transform, avatar.transform);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    scene[(path, "blendShape." + mesh.GetBlendShapeName(i))] = smr.GetBlendShapeWeight(i);
            }

            foreach (var clip in ctrl.animationClips.Distinct())
            {
                if (clip == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!b.propertyName.StartsWith("blendShape.")) continue;
                    if (!scene.TryGetValue((b.path, b.propertyName), out var w) || Mathf.Abs(w) < 0.0001f) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    if (curve == null || curve.length == 0) continue;
                    if (Mathf.Abs(curve.keys[0].value - w) > 0.5f)
                        res.Add($"Clip '{clip.name}' sets {b.path}/{b.propertyName.Substring(11)} to " +
                                $"{curve.keys[0].value:0.#} but the scene (working) value is {w:0.#} — this " +
                                "resets it in game.");
                }
            }
            return res.Take(12).ToList();
        }
    }
}
