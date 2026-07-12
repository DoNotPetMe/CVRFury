using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Clothing setup — the MANUAL way: drag the items in, configure each one, create everything at once.
    /// Each item gets a menu toggle, and two things a plain toggle can't give you:
    ///   • a Blendshape Link (the clothing mesh follows the BODY's shape keys, so body sliders — bust, hips,
    ///     weight — deform the clothes too instead of the body clipping through them), and
    ///   • clipping-fix blendshapes (when the item is ON, set body shapes like "Shrink_Torso" so skin never
    ///     pokes through — wired into the item's own toggle state).
    /// </summary>
    internal static class AvatarFeaturePack
    {
        internal sealed class ClipFix
        {
            public int shapeIndex;      // index into the body mesh's blendshapes
            public float value = 100f;
        }

        internal sealed class ClothingItem
        {
            public GameObject go;
            public string label = "";
            public bool defaultOn = true;
            public bool linkBodyShapes = true;               // add a CVRFuryBlendshapeLink to the body
            public readonly List<ClipFix> fixes = new List<ClipFix>(); // body shapes set while the item is ON
        }

        public static string CreateClothing(GameObject avatar, SkinnedMeshRenderer body,
                                            List<ClothingItem> items, string menuFolder)
        {
            if (avatar == null) return "Pick the avatar first.";
            var rows = items.Where(i => i.go != null).ToList();
            if (rows.Count == 0) return "Drag at least one clothing item into the list.";

            int toggles = 0, links = 0, fixes = 0;
            foreach (var item in rows)
            {
                var label = string.IsNullOrEmpty(item.label) ? Prettify(item.go.name) : item.label;

                var t = item.go.GetComponent<CVRFuryToggle>();
                if (t == null) { t = Undo.AddComponent<CVRFuryToggle>(item.go); toggles++; }
                t.menuPath = string.IsNullOrEmpty(menuFolder) ? label : $"{menuFolder}/{label}";
                t.defaultOn = item.defaultOn;
                t.saved = true;
                if (!t.state.actions.Any(a => a.type == FuryAction.ActionType.ObjectToggle && a.targetObject == item.go))
                    t.state.actions.Add(new FuryAction
                    {
                        type = FuryAction.ActionType.ObjectToggle, targetObject = item.go, targetState = true,
                    });

                // Clipping fixes ride the SAME toggle: item on → body shape applied; item off → back to rest.
                if (body != null && body.sharedMesh != null)
                    foreach (var fix in item.fixes)
                    {
                        if (fix.shapeIndex < 0 || fix.shapeIndex >= body.sharedMesh.blendShapeCount) continue;
                        var shape = body.sharedMesh.GetBlendShapeName(fix.shapeIndex);
                        if (t.state.actions.Any(a => a.type == FuryAction.ActionType.BlendShape && a.blendShape == shape))
                            continue;
                        t.state.actions.Add(new FuryAction
                        {
                            type = FuryAction.ActionType.BlendShape,
                            blendShapeRenderer = body, blendShape = shape, blendShapeValue = fix.value,
                        });
                        fixes++;
                    }

                // Blendshape Link: the clothing follows the body's shape keys, so body sliders fit the clothes.
                if (item.linkBodyShapes && body != null)
                {
                    var smr = item.go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0 && smr != body)
                    {
                        var existing = avatar.GetComponentsInChildren<CVRFuryBlendshapeLink>(true)
                            .FirstOrDefault(l => l.sourceMesh == body && l.targetMeshes.Contains(smr));
                        if (existing == null)
                        {
                            var link = Undo.AddComponent<CVRFuryBlendshapeLink>(item.go);
                            link.sourceMesh = body;
                            link.targetMeshes.Add(smr);
                            links++;
                        }
                    }
                }
                EditorUtility.SetDirty(item.go);
            }

            return $"Set up {rows.Count} item(s): {toggles} toggle(s) created, {links} blendshape link(s) " +
                   $"(clothes follow body sliders), {fixes} clipping-fix shape(s) wired into the toggles.";
        }

        /// <summary>Small CCK trigger volume that writes 1 into an AAS parameter while something is inside
        /// it. Fuzzy field wiring — names drift across CCK versions, every set is best-effort.</summary>
        internal static bool TryAddTouchTrigger(string parameter, Transform parent, Vector3 offset,
                                                float size, bool othersCanTrigger)
        {
            if (parent == null) return false;
            var go = new GameObject($"CVRFury Touch Trigger ({parameter})");
            Undo.RegisterCreatedObjectUndo(go, "Touch trigger");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            if (TryAddTouchTriggerOn(go, parameter, new Vector3(size, size, size), othersCanTrigger)) return true;
            Object.DestroyImmediate(go);
            return false;
        }

        /// <summary>Wires the CCK trigger onto an EXISTING GameObject — used by custom (user-placed) touch
        /// zones, whose position/size the user already confirmed visually.</summary>
        internal static bool TryAddTouchTriggerOn(GameObject go, string parameter, Vector3 size, bool othersCanTrigger)
        {
            var t = Reflect.FindType("ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger");
            if (t == null || go == null) return false;
            try
            {
                var comp = go.GetComponent(t);
                if (comp == null) comp = Undo.AddComponent(go, t); // '??' breaks on Unity's fake-null

                // These three are the trigger's core and stable across CCK versions.
                Reflect.SetField(comp, "settingName", parameter);
                Reflect.SetField(comp, "settingValue", 1f);
                Reflect.SetField(comp, "areaSize", size);

                // Touch-permission fields drift between CCK versions — find them by name shape instead of
                // exact name, and when nothing matches, log the REAL field layout so one console paste is
                // enough to pin the correct names.
                bool permissionSet =
                    SetBoolFieldFuzzy(comp, new[] { "allowother", "otherstotrigger", "othersinteract" }, othersCanTrigger) |
                    SetBoolFieldFuzzy(comp, new[] { "network" }, othersCanTrigger);
                if (!permissionSet)
                    Debug.Log("[CVRFury] Touch trigger: no others-can-trigger field recognised on this CCK's " +
                              "trigger — it was created with the CCK's default touch permissions. Its actual " +
                              "fields are: " + DumpFields(t) + " — share this line to get the toggle wired.");

                EditorUtility.SetDirty(go);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Sets the first bool field whose normalized name contains any candidate. Quiet on miss —
        /// optional fields must not spam the console like a missing required field would.</summary>
        private static bool SetBoolFieldFuzzy(object obj, string[] candidates, bool value)
        {
            foreach (var f in obj.GetType().GetFields(System.Reflection.BindingFlags.Public |
                                                      System.Reflection.BindingFlags.Instance))
            {
                if (f.FieldType != typeof(bool)) continue;
                var n = f.Name.ToLowerInvariant().Replace("_", "");
                if (!candidates.Any(c => n.Contains(c))) continue;
                f.SetValue(obj, value);
                return true;
            }
            return false;
        }

        private static string DumpFields(System.Type t) =>
            "[" + string.Join(", ", t.GetFields(System.Reflection.BindingFlags.Public |
                                                System.Reflection.BindingFlags.Instance)
                                     .Select(f => $"{f.FieldType.Name} {f.Name}")) + "]";

        // "AS_Fishnet_top" → "Fishnet top"
        internal static string Prettify(string name)
        {
            var s = name.Trim();
            foreach (var prefix in new[] { "AS_", "AS ", "T_", "C_" })
                if (s.StartsWith(prefix)) { s = s.Substring(prefix.Length); break; }
            return s.Replace('_', ' ').Trim();
        }
    }
}
