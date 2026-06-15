using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// One-click parameter linker. Reads the avatar's VRChat Expressions Menu + Parameters and creates
    /// a matching ChilloutVR Advanced Avatar Settings (AAS) entry for every menu control, with each
    /// entry's <b>Machine Name set to the exact VRChat parameter name</b> and the default carried over.
    ///
    /// For toggles it also tries to wire the CVR-native <b>GameObject target</b> automatically: it matches
    /// the toggle's parameter (e.g. <c>Toggle/Witch Outfit/Corset</c>) to a GameObject in the hierarchy
    /// (here, a "Corset" object), so the toggle directly shows/hides that object — no animation clip and
    /// no custom controller needed. Anything it can't confidently match is listed so you can assign it by
    /// hand.
    ///
    /// It never creates, merges or attaches an animator controller. After running, click the CCK's
    /// <b>Create Controller</b> once: CVR then generates one parameter per entry (clearing the "parameter
    /// not present" warnings) and the GameObject toggles work.
    /// </summary>
    public static class AasParameterLinker
    {
        /// <summary>Create/refresh the CCK Advanced Avatar Settings entries from the avatar's VRChat menu.
        /// Non-destructive (only adds parameters not already present) and never touches the controller.
        /// Returns a human-readable report. Hosted by the unified CVRFury window.</summary>
        public static string LinkParameters(GameObject target)
        {
            if (target == null)
                return "Select your avatar (the root that has the VRChat descriptor) first.";

            var descType = Reflect.FindType(VrcNames.AvatarDescriptorType);
            if (descType == null)
                return "VRChat SDK not found in this project. Its types have to be loadable for CVRFury to read the menu.";
            var desc = target.GetComponent(descType);
            if (desc == null)
                return $"'{target.name}' has no VRCAvatarDescriptor. Select the avatar root that shows the VRChat " +
                       "'Expressions' section (Menu + Parameters).";

            var menu = Reflect.GetField(desc, VrcNames.Desc_ExpressionsMenu);
            if (menu == null)
                return "The VRChat descriptor has no Expressions Menu assigned (the 'Menu' slot is empty).";

            var defaults = BuildParamMap(Reflect.GetField(desc, VrcNames.Desc_ExpressionParameters));
            var index = BuildNameIndex(target);

            var cvr = CckAvatar.EnsureOn(target);
            if (cvr == null)
                return "ChilloutVR CCK not found in this project (the CVRAvatar type is missing).";
            cvr.EnsureAdvancedSettingsContainer();

            // Non-destructive: keep every existing entry (and any clips you set on them), and only add
            // parameters that aren't already present. Seeding 'seen' with existing machine names is what
            // makes re-running safe — it can never clear your work again.
            var seen = new HashSet<string>();
            var list = cvr.SettingsList;
            if (list != null)
                foreach (var e in list)
                {
                    var mn = CckAvatar.EntryMachineName(e);
                    if (!string.IsNullOrEmpty(mn)) seen.Add(mn);
                }
            int existing = seen.Count;

            int toggles = 0, sliders = 0, matched = 0;
            var unmatched = new List<string>();
            WalkMenu(menu, defaults, cvr, target, index, seen, new HashSet<UnityEngine.Object>(),
                     ref toggles, ref sliders, ref matched, unmatched);

            cvr.Persist();

            var msg = $"Added {toggles + sliders} new setting(s) ({toggles} toggle, {sliders} slider)" +
                      (existing > 0 ? $"; {existing} already existed and were left untouched" : "") + ".\n" +
                      $"GameObject targets auto-assigned: {matched}/{toggles} toggles.";
            if (unmatched.Count > 0)
            {
                msg += $"\nNo GameObject match for {unmatched.Count} toggle(s) (presets/idle toggles, or driven " +
                       "by clips — link clips in step 2):\n  " +
                       string.Join("\n  ", unmatched.GetRange(0, Math.Min(unmatched.Count, 20)));
                if (unmatched.Count > 20) msg += $"\n  …and {unmatched.Count - 20} more.";
            }
            return msg;
        }

        private static void WalkMenu(object menu, Dictionary<string, (int type, float def)> defaults, CckAvatar cvr,
                                     GameObject root, Dictionary<string, List<Transform>> index,
                                     HashSet<string> seen, HashSet<UnityEngine.Object> visited,
                                     ref int toggles, ref int sliders, ref int matched, List<string> unmatched)
        {
            if (menu == null) return;
            if (menu is UnityEngine.Object mo && !visited.Add(mo)) return; // guard against submenu cycles

            var controls = Reflect.AsList(Reflect.GetField(menu, VrcNames.Menu_Controls));
            if (controls == null) return;

            foreach (var control in controls)
            {
                var name = Reflect.GetField(control, VrcNames.Control_Name) as string ?? "Control";
                var type = Reflect.GetField(control, VrcNames.Control_Type)?.ToString() ?? "";

                switch (type)
                {
                    case "Toggle":
                    case "Button":
                    {
                        var p = ParamName(control);
                        if (string.IsNullOrEmpty(p) || !seen.Add(p)) break;
                        bool on = defaults.TryGetValue(p, out var d) && d.def != 0f;

                        var targets = new List<(GameObject, string)>();
                        var tf = FindTarget(index, p, name);
                        if (tf != null)
                        {
                            targets.Add((tf.gameObject, AnimationUtility.CalculateTransformPath(tf, root.transform)));
                            matched++;
                        }
                        else unmatched.Add($"{name}   →   {p}");

                        if (cvr.AddGameObjectToggle(name, p, on, targets)) toggles++;
                        break;
                    }
                    case "RadialPuppet":
                    {
                        var p = SubParamName(control, 0);
                        if (string.IsNullOrEmpty(p) || !seen.Add(p)) break;
                        float def = defaults.TryGetValue(p, out var d) ? Mathf.Clamp01(d.def) : 0f;
                        if (cvr.AddSlider(name, p, def, false)) sliders++;
                        break;
                    }
                    case "TwoAxisPuppet":
                    case "FourAxisPuppet":
                        for (int i = 0; i < 2; i++)
                        {
                            var p = SubParamName(control, i);
                            if (string.IsNullOrEmpty(p) || !seen.Add(p)) continue;
                            float def = defaults.TryGetValue(p, out var d) ? Mathf.Clamp01(d.def) : 0f;
                            if (cvr.AddSlider($"{name} {i + 1}", p, def, false)) sliders++;
                        }
                        break;
                    case "SubMenu":
                        WalkMenu(Reflect.GetField(control, VrcNames.Control_SubMenu), defaults, cvr, root, index,
                                 seen, visited, ref toggles, ref sliders, ref matched, unmatched);
                        break;
                }
            }
        }

        /// <summary>Match a toggle parameter to a GameObject by name. Tries the parameter's last path
        /// segment (e.g. "Corset" from "Toggle/Witch Outfit/Corset") then the control's display name;
        /// when several objects share the name, prefers one whose ancestor path contains the parameter's
        /// category segment (e.g. "Witch Outfit"). Returns null if there's no confident match.</summary>
        private static Transform FindTarget(Dictionary<string, List<Transform>> index, string param, string displayName)
        {
            var segs = param.Split('/');
            var leaf = segs[segs.Length - 1].Trim();
            var category = segs.Length >= 2 ? segs[segs.Length - 2].Trim() : null;
            return Pick(index, leaf, category) ?? Pick(index, displayName?.Trim(), category);
        }

        private static Transform Pick(Dictionary<string, List<Transform>> index, string name, string category)
        {
            if (string.IsNullOrEmpty(name) || !index.TryGetValue(name, out var matches) || matches.Count == 0)
                return null;
            if (matches.Count == 1) return matches[0];
            if (!string.IsNullOrEmpty(category))
                foreach (var m in matches)
                    if (AncestorPathContains(m, category)) return m;
            return matches[0];
        }

        private static bool AncestorPathContains(Transform t, string name)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (string.Equals(p.name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Dictionary<string, List<Transform>> BuildNameIndex(GameObject root)
        {
            var index = new Dictionary<string, List<Transform>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                var key = t.name.Trim();
                if (!index.TryGetValue(key, out var l)) index[key] = l = new List<Transform>();
                l.Add(t);
            }
            return index;
        }

        private static Dictionary<string, (int type, float def)> BuildParamMap(object prms)
        {
            var map = new Dictionary<string, (int type, float def)>();
            if (prms == null) return map;
            var list = Reflect.AsList(Reflect.GetField(prms, VrcNames.ExprParams_List));
            if (list == null) return map;
            foreach (var p in list)
            {
                var n = Reflect.GetField(p, VrcNames.ExprParam_Name) as string;
                if (string.IsNullOrEmpty(n)) continue;
                int vt = 0;
                var vto = Reflect.GetField(p, VrcNames.ExprParam_ValueType);
                if (vto != null) { try { vt = System.Convert.ToInt32(vto); } catch { } }
                float def = 0f;
                var dvo = Reflect.GetField(p, VrcNames.ExprParam_DefaultValue);
                if (dvo != null) { try { def = System.Convert.ToSingle(dvo); } catch { } }
                map[n] = (vt, def);
            }
            return map;
        }

        private static string ParamName(object control)
        {
            var p = Reflect.GetField(control, VrcNames.Control_Parameter);
            return p == null ? null : Reflect.GetField(p, VrcNames.Control_ParameterName) as string;
        }

        private static string SubParamName(object control, int index)
        {
            var subs = Reflect.AsList(Reflect.GetField(control, VrcNames.Control_SubParameters));
            if (subs == null || index >= subs.Count) return null;
            var sp = subs[index];
            return sp == null ? null : Reflect.GetField(sp, VrcNames.Control_ParameterName) as string;
        }
    }
}
