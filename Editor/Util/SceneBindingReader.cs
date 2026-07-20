using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Reads the CURRENT scene value behind any animation binding — including the ones Unity's
    /// <c>AnimationUtility.GetFloatValue</c> can't provide (<c>material.*</c> shader properties are the big
    /// one: they're GPU uniforms, not serialized fields). Used by off-clip synthesis (an OFF state must
    /// cover EVERY property the ON clip touches) and by the Menu Verifier's visible-change analysis.
    /// </summary>
    internal static class SceneBindingReader
    {
        public static bool TryReadFloat(GameObject root, EditorCurveBinding b, out float value)
        {
            if (AnimationUtility.GetFloatValue(root, b, out value)) return true;
            value = 0f;
            var t = string.IsNullOrEmpty(b.path) ? root.transform : root.transform.Find(b.path);
            if (t == null) return false;
            var prop = b.propertyName;

            if (prop == "m_IsActive") { value = t.gameObject.activeSelf ? 1f : 0f; return true; }
            if (prop == "m_Enabled")
            {
                var comp = t.GetComponent(b.type);
                if (comp is Behaviour beh) { value = beh.enabled ? 1f : 0f; return true; }
                if (comp is Renderer ren) { value = ren.enabled ? 1f : 0f; return true; }
                return false;
            }
            if (prop.StartsWith("blendShape."))
            {
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                var idx = smr != null && smr.sharedMesh != null
                    ? smr.sharedMesh.GetBlendShapeIndex(prop.Substring("blendShape.".Length)) : -1;
                if (idx < 0) return false;
                value = smr.GetBlendShapeWeight(idx);
                return true;
            }
            if (prop.StartsWith("material."))
            {
                var ren = t.GetComponent<Renderer>();
                if (ren == null) return false;
                var rest = prop.Substring("material.".Length);
                char chan = '\0';
                if (rest.Length > 2 && rest[rest.Length - 2] == '.' &&
                    "rgbaxyzw".IndexOf(rest[rest.Length - 1]) >= 0)
                { chan = rest[rest.Length - 1]; rest = rest.Substring(0, rest.Length - 2); }

                foreach (var m in ren.sharedMaterials)
                {
                    if (m == null || !m.HasProperty(rest)) continue;
                    if (chan == '\0') { value = m.GetFloat(rest); return true; }
                    var v = m.GetVector(rest); // colors read identically through GetVector
                    value = chan == 'r' || chan == 'x' ? v.x
                          : chan == 'g' || chan == 'y' ? v.y
                          : chan == 'b' || chan == 'z' ? v.z : v.w;
                    return true;
                }
                return false;
            }
            return false;
        }

        public static bool TryReadObject(GameObject root, EditorCurveBinding b, out Object value)
        {
            if (AnimationUtility.GetObjectReferenceValue(root, b, out value) && value != null) return true;
            value = null;
            var t = string.IsNullOrEmpty(b.path) ? root.transform : root.transform.Find(b.path);
            if (t == null) return false;
            var match = System.Text.RegularExpressions.Regex.Match(b.propertyName, @"m_Materials\.Array\.data\[(\d+)\]");
            if (!match.Success) return false;
            var ren = t.GetComponent<Renderer>();
            if (ren == null) return false;
            int slot = int.Parse(match.Groups[1].Value);
            if (slot < 0 || slot >= ren.sharedMaterials.Length) return false;
            value = ren.sharedMaterials[slot];
            return value != null;
        }
    }
}
