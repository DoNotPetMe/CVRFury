using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CVRFury.Components;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Reads VRCFury feature components (which only load when VRCFury is imported) and converts the ones we
    /// can map to CVRFury equivalents. VRCFury injects its toggles into the VRChat menu only at build time,
    /// so the normal "expression menu → AAS" conversion never sees them — this reads the components directly.
    ///
    /// Reflection-based and version-tolerant: VRCFury's model is internal and changes, so we read fields
    /// defensively and skip (with a report) anything we don't recognise.
    /// </summary>
    internal static class VRCFuryConverter
    {
        private static readonly string[] CompTypeNames = { "VF.Model.VRCFury", "VRCFury", "VF.VRCFury" };

        public static List<Component> FindAll(GameObject avatar)
        {
            var res = new List<Component>();
            if (avatar == null) return res;
            foreach (var tn in CompTypeNames)
            {
                var t = Reflect.FindType(tn);
                if (t == null) continue;
                foreach (var c in avatar.GetComponentsInChildren(t, true))
                    if (c is Component comp) res.Add(comp);
            }
            return res.Distinct().ToList();
        }

        private static object Content(Component c) => Reflect.GetField(c, "content");
        private static string FeatureName(object content) => content?.GetType().Name ?? "?";

        public static string DetectReport(GameObject avatar)
        {
            if (Reflect.FindType("VF.Model.VRCFury") == null && Reflect.FindType("VRCFury") == null)
                return "VRCFury isn't imported, so its components show as 'missing script' and can't be read. " +
                       "Import VRCFury into the project, then detect again.";

            var comps = FindAll(avatar);
            if (comps.Count == 0) return "No VRCFury components found on this avatar.";

            var byType = comps.GroupBy(c => FeatureName(Content(c)))
                              .Select(g => $"{g.Count()}× {g.Key}").OrderBy(s => s);
            int toggles = comps.Count(c => FeatureName(Content(c)) == "Toggle");
            return $"Found {comps.Count} VRCFury feature(s): {string.Join(", ", byType)}.\n" +
                   (toggles > 0
                       ? $"\"Convert toggles\" will turn the {toggles} Toggle(s) into CVR menu toggles. " +
                         "(Armature Link / Blendshape Link / Full Controller aren't converted yet.)"
                       : "No simple Toggles to convert here yet.");
        }

        public static string ConvertToggles(GameObject avatar)
        {
            var comps = FindAll(avatar);
            int made = 0;
            var skipped = new List<string>();

            foreach (var c in comps)
            {
                var content = Content(c);
                if (content == null) continue;
                if (FeatureName(content) != "Toggle") continue;

                var menu = Reflect.GetField(content, "name") as string;
                if (string.IsNullOrEmpty(menu)) menu = c.gameObject.name;
                bool defaultOn = ToBool(Reflect.GetField(content, "defaultOn"), false);
                bool saved = ToBool(Reflect.GetField(content, "saved"), true);

                var actions = Reflect.GetField(Reflect.GetField(content, "state"), "actions") as IList;
                var furyActions = new List<FuryAction>();
                if (actions != null)
                    foreach (var act in actions)
                    {
                        if (act == null) continue;
                        if (!act.GetType().Name.Contains("ObjectToggle")) continue; // only object on/off for now
                        var obj = Reflect.GetField(act, "obj") as GameObject;
                        if (obj == null) continue;
                        var mode = Reflect.GetField(act, "mode")?.ToString() ?? "TurnOn";
                        furyActions.Add(new FuryAction
                        {
                            type = FuryAction.ActionType.ObjectToggle,
                            targetObject = obj,
                            targetState = mode != "TurnOff", // TurnOn / Toggle → on
                        });
                    }

                if (furyActions.Count == 0) { skipped.Add($"{menu} (no object actions)"); continue; }

                var toggle = Undo.AddComponent<CVRFuryToggle>(avatar);
                toggle.menuPath = menu;
                toggle.defaultOn = defaultOn;
                toggle.saved = saved;
                toggle.state.actions.AddRange(furyActions);
                made++;
            }

            EditorUtility.SetDirty(avatar);
            if (made == 0 && skipped.Count == 0)
                return "No VRCFury toggles found to convert.";
            var msg = $"Converted {made} VRCFury toggle(s) into CVRFury toggles (object on/off). They'll bake " +
                      "into the CVR menu on upload/Test Bake.";
            if (skipped.Count > 0)
                msg += $"\nSkipped {skipped.Count} with non-object actions (blendshape/material/etc., not yet " +
                       $"supported): {string.Join(", ", skipped.Take(15))}{(skipped.Count > 15 ? " …" : "")}.";
            return msg;
        }

        private static bool ToBool(object o, bool fallback = false)
        {
            try { return o == null ? fallback : System.Convert.ToBoolean(o); } catch { return fallback; }
        }
    }
}
