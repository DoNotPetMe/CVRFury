using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Makes Poiyomi/Thry materials ANIMATABLE for ChilloutVR. A LOCKED Poiyomi shader only responds to
    /// animation on properties tagged "Animated"; everything else is baked and ignores animation — so a
    /// converted dissolve/hue/emission toggle silently does nothing in-game even though the property works
    /// when set by hand. VRChat avatars get away with locked shaders because VRCFury/Poiyomi mark the
    /// animated properties before locking; a converted CVR avatar has no such pass, so the fix is to UNLOCK
    /// the materials (CVR doesn't need locking's variant optimization the way VRChat does). Unlocked = every
    /// property animatable = every material toggle works.
    ///
    /// Uses Thry's optimizer by reflection so the package stays optional; reports exactly what it did.
    /// </summary>
    internal static class PoiyomiTools
    {
        public static string UnlockAll(GameObject avatar)
        {
            if (avatar == null) return "Pick the avatar first.";
            var opt = Reflect.FindType("Thry.ShaderOptimizer");
            if (opt == null)
                return "Thry/Poiyomi isn't in this project (Thry.ShaderOptimizer not found). If your materials " +
                       "aren't Poiyomi, this isn't needed. If they are, import Poiyomi and retry.";

            var locked = new List<Material>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null &&
                        m.shader.name.ToLowerInvariant().Contains("/locked") && !locked.Contains(m))
                        locked.Add(m);

            if (locked.Count == 0)
                return "No LOCKED Poiyomi materials found — either they're already unlocked (good; animated " +
                       "toggles will work) or they aren't Poiyomi.";

            int unlocked = locked.Count(Unlock);
            var stillLocked = locked.Count - unlocked;
            return $"Unlocked {unlocked} of {locked.Count} Poiyomi material(s) — their properties are now " +
                   "animatable, so material-driven toggles (dissolve/hue/emission) will actually change in CVR." +
                   (stillLocked > 0 ? $" {stillLocked} couldn't be auto-unlocked — select them and click " +
                    "\"Unlock Shader\" at the top of the material inspector." : "") +
                   "\nRe-run your menu conversion (or upload) after this.";
        }

        private static bool Unlock(Material m)
        {
            var opt = Reflect.FindType("Thry.ShaderOptimizer");
            if (opt == null) return false;
            foreach (var method in opt.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (method.Name != "SetLockedForAllMaterials") continue;
                var ps = method.GetParameters();
                if (ps.Length < 2 || !typeof(System.Collections.IEnumerable).IsAssignableFrom(ps[0].ParameterType) ||
                    ps[1].ParameterType != typeof(int)) continue;
                try
                {
                    var args = new object[ps.Length];
                    args[0] = new[] { m };
                    args[1] = 0; // 0 = unlocked
                    for (int i = 2; i < ps.Length; i++)
                        args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                                : ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null;
                    method.Invoke(null, args);
                    return m.shader == null || !m.shader.name.ToLowerInvariant().Contains("/locked");
                }
                catch { /* try next overload shape */ }
            }
            return false;
        }
    }
}
