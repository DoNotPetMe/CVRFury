using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// One-click parameter linker. Reads the avatar's VRChat Expressions Menu + Parameters and creates
    /// a matching ChilloutVR Advanced Avatar Settings (AAS) entry for every menu control, with each
    /// entry's <b>Machine Name set to the exact VRChat parameter name</b> and the default value carried
    /// over.
    ///
    /// It deliberately does NOT create, merge, generate or attach any animator controller — it leaves
    /// the controller you set up completely alone. Use it when you've already built and attached your
    /// own controller and only need the CCK menu parameters wired to it. The toggle works in-game as
    /// long as your controller has an animator parameter with that same Machine Name.
    ///
    /// VRCFury internal parameters never appear in the VRChat menu, so walking the menu (rather than the
    /// raw parameter list) naturally skips them — only the human-facing controls are linked.
    /// </summary>
    public static class AasParameterLinker
    {
        [MenuItem("Tools/CVRFury/Link CCK Parameters from VRChat Menu (keep my controller)", false, 1)]
        public static void Run()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Tell("Select your avatar (the root that has the VRChat descriptor) in the Hierarchy first.");
                return;
            }

            var descType = Reflect.FindType(VrcNames.AvatarDescriptorType);
            if (descType == null)
            {
                Tell("VRChat SDK not found in this project. Its types have to be loadable for CVRFury to read the menu.");
                return;
            }
            var desc = target.GetComponent(descType);
            if (desc == null)
            {
                Tell($"'{target.name}' has no VRCAvatarDescriptor.\n\nSelect the avatar root that shows the VRChat " +
                     "'Expressions' section (Menu + Parameters).");
                return;
            }

            var menu = Reflect.GetField(desc, VrcNames.Desc_ExpressionsMenu);
            if (menu == null)
            {
                Tell("The VRChat descriptor has no Expressions Menu assigned (the 'Menu' slot is empty).");
                return;
            }
            var defaults = BuildParamMap(Reflect.GetField(desc, VrcNames.Desc_ExpressionParameters));

            var cvr = CckAvatar.EnsureOn(target);
            if (cvr == null)
            {
                Tell("ChilloutVR CCK not found in this project (the CVRAvatar type is missing).");
                return;
            }
            cvr.EnsureAdvancedSettingsContainer();

            // Rebuild the AAS list cleanly so re-running can't produce duplicates.
            var list = cvr.SettingsList;
            int replaced = list?.Count ?? 0;
            list?.Clear();

            int toggles = 0, sliders = 0;
            WalkMenu(menu, defaults, cvr, new HashSet<string>(), new HashSet<Object>(), ref toggles, ref sliders);

            cvr.Persist();
            Reselect(target); // rebuild the CCK inspector against the now-populated list

            Tell($"Linked {toggles + sliders} CCK Advanced Avatar Setting(s) from the VRChat menu " +
                 $"({toggles} toggle, {sliders} slider)" +
                 (replaced > 0 ? $", replacing {replaced} existing entr(y/ies)" : "") + ".\n\n" +
                 "Every entry's Machine Name now equals its VRChat parameter name, and your animator controller " +
                 "was NOT touched.\n\nFinish up in the CVRAvatar ▸ Advanced Settings: make sure the Base Controller " +
                 "is your working controller. A toggle drives whatever animator parameter shares its Machine Name — " +
                 "so this only does something in-game if your controller actually has those parameters.");
        }

        private static void WalkMenu(object menu, Dictionary<string, (int type, float def)> defaults, CckAvatar cvr,
                                     HashSet<string> seen, HashSet<Object> visited, ref int toggles, ref int sliders)
        {
            if (menu == null) return;
            if (menu is Object mo && !visited.Add(mo)) return; // guard against submenu cycles

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
                        if (cvr.AddToggle(name, p, on, false)) toggles++;
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
                        WalkMenu(Reflect.GetField(control, VrcNames.Control_SubMenu), defaults, cvr,
                                 seen, visited, ref toggles, ref sliders);
                        break;
                }
            }
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

        private static void Reselect(GameObject target)
        {
            Selection.activeObject = null;
            EditorApplication.delayCall += () => { if (target != null) Selection.activeObject = target; };
        }

        private static void Tell(string msg) => EditorUtility.DisplayDialog("CVRFury — Link CCK Parameters", msg, "OK");
    }
}
