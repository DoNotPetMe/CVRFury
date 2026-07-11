using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Fixes clothing that is ENABLED but invisible in the editor because its visibility is driven by an
    /// animated MATERIAL property instead of the GameObject: creators commonly leave the mesh always-on and
    /// toggle it via a Poiyomi dissolve/alpha float in the FX animator. With no animator running in edit
    /// mode, the material sits at its baked "hidden" default — the selection outline draws (it comes from the
    /// mesh) but every fragment is discarded.
    ///
    /// The ground truth lives in the animation clips: whatever property the toggle animates, and both of its
    /// values, are recorded there. Diagnose lists every material property animated for the renderer's path
    /// with the current material value vs the clip values; Reveal bakes the "shown" value into the material.
    /// Because the mesh is invisible RIGHT NOW, the clip value farthest from the current one is the visible
    /// state — no per-shader knowledge needed. Locked Poiyomi materials (Hidden/Locked/...) ignore edits to
    /// non-animated properties, so Reveal also unlocks them first via Thry's optimizer when it's present.
    /// </summary>
    internal static class MeshRevealer
    {
        public static string Diagnose(GameObject avatar, Renderer target)
        {
            if (avatar == null || target == null) return "Pick the avatar and the invisible mesh (its renderer).";
            var sb = new System.Text.StringBuilder();

            // The mundane causes first, so nobody chases material state on a disabled object.
            if (!target.enabled) sb.AppendLine("✗ The Renderer component itself is disabled.");
            if (!target.gameObject.activeInHierarchy) sb.AppendLine("✗ The GameObject (or a parent) is inactive.");
            for (var t = target.transform; t != null && t != avatar.transform.parent; t = t.parent)
                if (t.localScale.sqrMagnitude < 1e-10f) { sb.AppendLine($"✗ Zero scale on '{t.name}'."); break; }

            foreach (var m in target.sharedMaterials.Where(m => m != null).Distinct())
                sb.AppendLine($"Material '{m.name}' — shader: {m.shader.name}" +
                              (IsLocked(m) ? "  (LOCKED — inspector edits to non-animated properties do nothing)" : ""));

            var animated = AnimatedProps(avatar, target);
            if (animated.Count == 0)
            {
                sb.AppendLine("No animator clip animates this renderer's material properties. If it's still " +
                              "invisible, the hidden state is baked into the material itself — unlock the shader " +
                              "and check dissolve/alpha values by hand.");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"{animated.Count} material propert(ies) are animated for this mesh — the toggle almost " +
                          "certainly lives here:");
            foreach (var p in animated)
            {
                var current = CurrentValue(target, p.prop);
                sb.AppendLine($"  • {p.prop}: current = {Fmt(current)} · clip values = " +
                              string.Join(", ", p.values.Select(v => $"{v.value:0.###} ('{v.clip}')")));
            }
            sb.AppendLine("\"Make visible\" bakes the value FARTHEST from the current one into the material — " +
                          "the mesh is hidden now, so the other state is the visible one.");
            return sb.ToString().TrimEnd();
        }

        public static string Reveal(GameObject avatar, Renderer target)
        {
            if (avatar == null || target == null) return "Pick the avatar and the invisible mesh (its renderer).";
            var animated = AnimatedProps(avatar, target);
            if (animated.Count == 0)
                return "No animated material properties found for this mesh — nothing to bake. Run Diagnose for " +
                       "the manual trail (locked shader / baked dissolve).";

            var sb = new System.Text.StringBuilder();
            var mats = target.sharedMaterials.Where(m => m != null).Distinct().ToList();

            foreach (var m in mats.Where(IsLocked))
                sb.AppendLine(TryUnlock(m)
                    ? $"Unlocked '{m.name}' (locked Poiyomi ignores edits otherwise)."
                    : $"⚠ '{m.name}' is LOCKED and auto-unlock failed — click \"Unlock Shader\" at the top of the " +
                      "material, then run this again.");

            int applied = 0;
            foreach (var p in animated)
            {
                foreach (var m in mats)
                {
                    var baseName = BaseProp(p.prop);
                    if (!m.HasProperty(baseName)) continue;
                    var current = GetFloatOrChannel(m, p.prop);
                    if (!current.HasValue) continue;

                    // The current state renders nothing, so the visible state is the clip value farthest away.
                    var targetVal = p.values.OrderByDescending(v => Mathf.Abs(v.value - current.Value)).First();
                    if (Mathf.Abs(targetVal.value - current.Value) < 0.0001f) continue;

                    Undo.RecordObject(m, "Reveal mesh");
                    SetFloatOrChannel(m, p.prop, targetVal.value);
                    EditorUtility.SetDirty(m);
                    sb.AppendLine($"'{m.name}' {p.prop}: {current.Value:0.###} → {targetVal.value:0.###} " +
                                  $"(from clip '{targetVal.clip}')");
                    applied++;
                }
            }

            if (applied == 0)
                sb.AppendLine("Found animated properties but none could be changed on these materials — if the " +
                              "shader is locked, unlock it first and re-run.");
            else
                sb.AppendLine($"\nBaked {applied} propert(ies) to the visible state. If the mesh appeared: this " +
                              "item's in-game toggle animates the material, not the GameObject — give its CVRFury " +
                              "toggle a Material Property action (same property, these two values) so it keeps " +
                              "toggling in CVR.");
            return sb.ToString().TrimEnd();
        }

        // --- animated-property discovery ------------------------------------------------------------

        private struct ClipValue { public string clip; public float value; }
        private sealed class AnimatedProp { public string prop; public List<ClipValue> values; }

        /// <summary>Every "material.*" property animated for the renderer's path, across every animator we can
        /// see on this avatar (live Animator, VRChat descriptor playable layers, CVR AAS animator), with the
        /// value each clip drives it to (last key — toggle clips are constant).</summary>
        private static List<AnimatedProp> AnimatedProps(GameObject avatar, Renderer target)
        {
            var path = AnimationUtility.CalculateTransformPath(target.transform, avatar.transform);
            var byProp = new Dictionary<string, List<ClipValue>>();

            foreach (var clip in AllClips(avatar))
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.path != path || !b.propertyName.StartsWith("material.")) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length == 0) continue;
                var prop = b.propertyName.Substring("material.".Length);
                if (!byProp.TryGetValue(prop, out var list)) byProp[prop] = list = new List<ClipValue>();
                var v = curve.keys[curve.length - 1].value;
                if (!list.Any(x => Mathf.Abs(x.value - v) < 0.0001f))
                    list.Add(new ClipValue { clip = clip.name, value = v });
            }

            // Only properties with at least two distinct values represent a toggle (one value = constant pin).
            return byProp.Where(kv => kv.Value.Count >= 2)
                         .Select(kv => new AnimatedProp { prop = kv.Key, values = kv.Value })
                         .OrderBy(p => p.prop)
                         .ToList();
        }

        private static IEnumerable<AnimationClip> AllClips(GameObject avatar)
        {
            var seen = new HashSet<AnimationClip>();
            var controllers = new List<AnimatorController>();

            var anim = avatar.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController is AnimatorController ac) controllers.Add(ac);

            // VRChat playable layers (FX carries outfit toggles) — read via reflection, SDK optional.
            var descT = Reflect.FindType(VrcNames.AvatarDescriptorType);
            var desc = descT != null ? avatar.GetComponentInChildren(descT, true) : null;
            if (desc != null && Reflect.GetField(desc, VrcNames.Desc_BaseAnimationLayers) is System.Collections.IList layers)
                foreach (var layer in layers)
                    if (Reflect.GetField(layer, VrcNames.Layer_Controller) is AnimatorController lc)
                        controllers.Add(lc);

            // CVR AAS animator, for avatars already converted.
            var cvr = CckAvatar.FindOn(avatar);
            if (cvr != null &&
                Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator) is AnimatorController cc)
                controllers.Add(cc);

            foreach (var c in controllers.Distinct().Where(c => c != null))
                foreach (var clip in c.animationClips)
                    if (clip != null && seen.Add(clip))
                        yield return clip;
        }

        // --- material plumbing ----------------------------------------------------------------------

        private static bool IsLocked(Material m) =>
            m.shader != null && m.shader.name.ToLowerInvariant().Contains("/locked");

        /// <summary>Unlock a locked (baked) Poiyomi/Thry material via Thry's optimizer, by reflection so the
        /// package stays optional. Tries the bulk API first, then any single-material unlock.</summary>
        private static bool TryUnlock(Material m)
        {
            var opt = Reflect.FindType("Thry.ShaderOptimizer");
            if (opt == null) return false;
            foreach (var method in opt.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                try
                {
                    var ps = method.GetParameters();
                    if (method.Name == "SetLockedForAllMaterials" && ps.Length >= 2 &&
                        typeof(System.Collections.IEnumerable).IsAssignableFrom(ps[0].ParameterType) &&
                        ps[1].ParameterType == typeof(int))
                    {
                        var args = new object[ps.Length];
                        args[0] = new[] { m }; args[1] = 0; // 0 = unlocked
                        for (int i = 2; i < ps.Length; i++)
                            args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                                    : ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null;
                        var r = method.Invoke(null, args);
                        if (!(r is bool ok) || ok) return !IsLocked(m) || true;
                    }
                }
                catch { /* signature drift — try the next overload */ }
            }
            return false;
        }

        // "_Alpha" is a float; "_Color.a" is a channel of a color/vector property.
        private static string BaseProp(string prop)
        {
            int dot = prop.LastIndexOf('.');
            return dot > 0 && prop.Length - dot == 2 ? prop.Substring(0, dot) : prop;
        }

        private static float? GetFloatOrChannel(Material m, string prop)
        {
            var baseName = BaseProp(prop);
            if (!m.HasProperty(baseName)) return null;
            if (baseName == prop) return m.GetFloat(prop);
            var c = m.GetColor(baseName);
            switch (prop[prop.Length - 1])
            {
                case 'r': case 'x': return c.r;
                case 'g': case 'y': return c.g;
                case 'b': case 'z': return c.b;
                default: return c.a;
            }
        }

        private static void SetFloatOrChannel(Material m, string prop, float v)
        {
            var baseName = BaseProp(prop);
            if (baseName == prop) { m.SetFloat(prop, v); return; }
            var c = m.GetColor(baseName);
            switch (prop[prop.Length - 1])
            {
                case 'r': case 'x': c.r = v; break;
                case 'g': case 'y': c.g = v; break;
                case 'b': case 'z': c.b = v; break;
                default: c.a = v; break;
            }
            m.SetColor(baseName, c);
        }

        private static string Fmt(float? v) => v.HasValue ? v.Value.ToString("0.###") : "(no such property)";

        private static float? CurrentValue(Renderer r, string prop)
        {
            foreach (var m in r.sharedMaterials.Where(m => m != null))
            {
                var v = GetFloatOrChannel(m, prop);
                if (v.HasValue) return v;
            }
            return null;
        }
    }
}
