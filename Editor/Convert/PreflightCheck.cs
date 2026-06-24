using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Read-only "is this avatar upload-ready?" check. Surfaces, in one place and proactively, the problems
    /// that otherwise only show up as a cryptic ChilloutVR upload abort: VRChat (GoGo Loco) locomotion,
    /// missing scripts, non-compiling shaders, an over-budget synced-bit count, and a missing controller.
    /// </summary>
    internal static class PreflightCheck
    {
        public struct Result { public bool ok; public string label; public string detail; }

        public static List<Result> Run(GameObject avatar)
        {
            var r = new List<Result>();
            if (avatar == null) { r.Add(Bad("Avatar", "none selected")); return r; }

            var cvr = CckAvatar.FindOn(avatar);
            r.Add(cvr != null ? Ok("CVRAvatar component", "present")
                              : Bad("CVRAvatar component", "missing — not a ChilloutVR avatar yet"));

            // Controller + locomotion (the motorbike check).
            var anim = cvr != null
                ? Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator) as AnimatorController
                : null;
            if (anim == null)
            {
                var a = avatar.GetComponentInChildren<Animator>();
                anim = a != null ? a.runtimeAnimatorController as AnimatorController : null;
            }
            if (anim == null)
                r.Add(Bad("Locomotion", "no controller attached — run Step 2 (Build & attach)"));
            else if (ControllerGuard.LooksLikeVRChatLocomotion(anim))
                r.Add(Bad("Locomotion", "VRChat locomotion (GoGo Loco) — won't work in CVR; use \"Reset to CVR native locomotion\""));
            else if (!ControllerGuard.HasCvrLocomotion(anim))
                r.Add(Bad("Locomotion", "no CVR locomotion blend tree — avatar may motorbike; use \"Reset to CVR native locomotion\""));
            else
                r.Add(Ok("Locomotion", "CVR locomotion present"));

            // Missing scripts.
            int missing = 0;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            r.Add(missing == 0 ? Ok("Missing scripts", "none")
                               : Bad("Missing scripts", $"{missing} broken component(s) — run Clean Missing Scripts / Strip"));

            // Shaders compile.
            var badShaders = ShaderErrors(avatar);
            r.Add(badShaders.Count == 0 ? Ok("Shaders compile", "all OK")
                : Bad("Shaders compile", $"{badShaders.Count} failing — {string.Join("; ", badShaders.Take(3))}"));

            // Synced-bit budget.
            if (cvr != null)
            {
                int bits = cvr.EstimateSyncedBits();
                r.Add(bits <= 3200 ? Ok("Synced bits", $"~{bits} / 3200")
                                   : Bad("Synced bits", $"~{bits} / 3200 — OVER the limit, upload will fail"));
            }
            return r;
        }

        /// <summary>One-line summary + the per-check lines, for display.</summary>
        public static string Report(GameObject avatar, out bool allOk)
        {
            var results = Run(avatar);
            allOk = results.All(x => x.ok);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(allOk ? "Ready to upload — all checks pass." : "Not ready — fix the ✗ items below:");
            foreach (var x in results)
                sb.AppendLine($"  {(x.ok ? "✓" : "✗")} {x.label}: {x.detail}");
            return sb.ToString().TrimEnd();
        }

        private static List<string> ShaderErrors(GameObject avatar)
        {
            var seen = new HashSet<Shader>();
            var bad = new List<string>();
            foreach (var rend in avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in rend.sharedMaterials)
                {
                    if (m == null || m.shader == null || !seen.Add(m.shader)) continue;
                    foreach (var msg in ShaderUtil.GetShaderMessages(m.shader))
                        if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        { bad.Add(m.shader.name); break; }
                }
            return bad.Distinct().ToList();
        }

        private static Result Ok(string label, string detail) => new Result { ok = true, label = label, detail = detail };
        private static Result Bad(string label, string detail) => new Result { ok = false, label = label, detail = detail };
    }
}
