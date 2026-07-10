using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Reads the PUBLIC VARIABLES of an UdonBehaviour by reflection — the part of an Udon setup that is
    /// plain serialized data, not compiled program: which objects a toggle toggles, where a teleporter sends
    /// you, which Light a switch drives. The program itself can't run outside VRChat, but these references
    /// are exactly what's needed to rebuild the behaviour with CVR components.
    ///
    /// The Udon variable-table API has shifted across SDK versions, so every lookup is tolerant: we try the
    /// known method shapes (TryGetVariableNames / GetVariableNames / VariableNames, then the non-generic
    /// TryGetVariableValue(string, out object)) and return an empty result rather than throw when a shape
    /// isn't found.
    /// </summary>
    internal static class UdonVariables
    {
        internal struct Ref { public string variable; public GameObject target; }

        /// <summary>Every scene GameObject referenced by the behaviour's public variables (assets and the
        /// behaviour's own object excluded — a toggle's interesting targets are other things in the scene).</summary>
        public static List<Ref> SceneReferences(Component udon)
        {
            var res = new List<Ref>();
            if (udon == null) return res;
            var table = Reflect.GetProperty(udon, "publicVariables") ?? Reflect.GetField(udon, "publicVariables");
            if (table == null) return res;

            foreach (var name in VariableNames(table))
            {
                if (!TryGetValue(table, name, out var value) || value == null) continue;
                foreach (var go in AsGameObjects(value))
                {
                    if (go == null || !go.scene.IsValid()) continue;   // scene objects only, no assets
                    if (go == udon.gameObject) continue;
                    if (res.Any(r => r.target == go)) continue;
                    res.Add(new Ref { variable = name, target = go });
                }
            }
            return res;
        }

        private static IEnumerable<string> VariableNames(object table)
        {
            var t = table.GetType();
            // bool TryGetVariableNames(out IEnumerable<string>)
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "TryGetVariableNames" && m.Name != "GetVariableNames") continue;
                var ps = m.GetParameters();
                try
                {
                    if (ps.Length == 1 && ps[0].IsOut)
                    {
                        var args = new object[] { null };
                        m.Invoke(table, args);
                        if (args[0] is IEnumerable names) return names.Cast<object>().Select(o => o?.ToString()).Where(s => s != null);
                    }
                    if (ps.Length == 0 && m.Invoke(table, null) is IEnumerable direct)
                        return direct.Cast<object>().Select(o => o?.ToString()).Where(s => s != null);
                }
                catch { /* wrong shape — try the next */ }
            }
            if (Reflect.GetProperty(table, "VariableNames") is IEnumerable prop)
                return prop.Cast<object>().Select(o => o?.ToString()).Where(s => s != null);
            return System.Array.Empty<string>();
        }

        private static bool TryGetValue(object table, string name, out object value)
        {
            value = null;
            foreach (var m in table.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "TryGetVariableValue" || m.IsGenericMethod) continue;
                var ps = m.GetParameters();
                if (ps.Length != 2 || ps[0].ParameterType != typeof(string) || !ps[1].IsOut) continue;
                try
                {
                    var args = new object[] { name, null };
                    m.Invoke(table, args);
                    value = args[1];
                    return value != null;
                }
                catch { /* wrong shape — try the next */ }
            }
            return false;
        }

        private static IEnumerable<GameObject> AsGameObjects(object value)
        {
            switch (value)
            {
                case string: break; // strings are IEnumerable — don't walk their chars
                case GameObject go: yield return go; break;
                case Component c when c != null: yield return c.gameObject; break;
                case IEnumerable list:
                    foreach (var item in list)
                        foreach (var go in AsGameObjects(item))
                            yield return go;
                    break;
            }
        }
    }
}
