using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// The Menu Wizard: converts a premade VRChat menu by reading the FX ANIMATOR GRAPH — the actual source
    /// of truth — instead of guessing clips from file names in a folder.
    ///
    /// VRChat stores, in the FX controller, exactly which parameter plays exactly which clip: toggles are
    /// states reached by transitions conditioned on the parameter (If/IfNot for bools, Equals for ints,
    /// Greater/Less for floats), radials are 1D blend trees on the parameter, VRCFury-style setups are
    /// direct-blend-tree children weighted by the parameter. The wizard walks the menu, follows each
    /// control's parameter into that graph, and extracts the exact clips — with PROVENANCE (which layer,
    /// which states/tree) shown for every row, so a wrong pick is visible before it's applied instead of a
    /// mystery after. Toggles whose clip only flips GameObject active states are converted to CVR-NATIVE
    /// object toggles (no clip at all — nothing to regenerate, nothing to break).
    /// </summary>
    internal static class MenuWizard
    {
        internal sealed class Row
        {
            public string display;      // menu label incl. submenu path
            public string param;        // VRChat parameter name
            public bool isSlider;
            public float menuValue = 1f; // Int/Float toggles: the value the control sets
            public AnimationClip on;     // toggle ON  / slider MAX
            public AnimationClip off;    // toggle OFF / slider MIN
            public string provenance = "not found in the FX graph";
            public bool include = true;
            public List<(GameObject go, string path)> nativeOn = new List<(GameObject, string)>();
            public List<(GameObject go, string path)> nativeOff = new List<(GameObject, string)>();
            public bool NativeOnly => on != null && (nativeOn.Count > 0 || nativeOff.Count > 0);
        }

        // ---------------------------------------------------------------- preview -------------------

        public static List<Row> Preview(GameObject avatar, out string summary)
        {
            var rows = new List<Row>();
            summary = "";
            if (avatar == null) { summary = "Pick the avatar first."; return rows; }

            var descT = Reflect.FindType(VrcNames.AvatarDescriptorType);
            var desc = descT != null ? avatar.GetComponentInChildren(descT, true) : null;
            if (desc == null)
            { summary = "No VRC Avatar Descriptor — the wizard reads the original VRChat data, so run it on " +
                        "the avatar BEFORE stripping."; return rows; }

            var fx = FindFxController(desc);
            var menu = Reflect.GetField(desc, VrcNames.Desc_ExpressionsMenu);
            if (menu == null) { summary = "The descriptor has no expressions menu."; return rows; }

            WalkMenu(menu, "", fx, avatar, rows, new HashSet<object>());

            int found = rows.Count(r => r.on != null || r.off != null);
            int native = rows.Count(r => r.NativeOnly);
            summary = $"{rows.Count} menu control(s) · {found} matched in the FX graph · {native} convert to " +
                      "CVR-native object toggles (no clips needed)" +
                      (fx == null ? " · ⚠ NO FX controller found — only native/no-clip info available" : "") +
                      "\nEvery row shows WHERE its clips came from. Fix anything that looks wrong, then Apply.";
            return rows;
        }

        private static void WalkMenu(object menu, string path, AnimatorController fx, GameObject avatar,
                                     List<Row> rows, HashSet<object> visited)
        {
            if (menu == null || !visited.Add(menu)) return;
            var controls = Reflect.AsList(Reflect.GetField(menu, VrcNames.Menu_Controls));
            if (controls == null) return;

            foreach (var control in controls)
            {
                var name = Reflect.GetField(control, VrcNames.Control_Name) as string ?? "Control";
                var display = string.IsNullOrEmpty(path) ? name : $"{path}/{name}";
                var type = Reflect.GetField(control, VrcNames.Control_Type)?.ToString() ?? "";

                switch (type)
                {
                    case "Toggle":
                    case "Button":
                    {
                        var param = ParamName(control);
                        if (string.IsNullOrEmpty(param)) break;
                        var row = new Row { display = display, param = param };
                        if (Reflect.GetField(control, VrcNames.Control_Value) is float v) row.menuValue = v;
                        if (fx != null) ExtractToggle(fx, row, avatar);
                        rows.Add(row);
                        break;
                    }
                    case "RadialPuppet":
                    {
                        var param = SubParamName(control, 0);
                        if (string.IsNullOrEmpty(param)) break;
                        var row = new Row { display = display, param = param, isSlider = true };
                        if (fx != null) ExtractSlider(fx, row);
                        rows.Add(row);
                        break;
                    }
                    case "SubMenu":
                        WalkMenu(Reflect.GetField(control, VrcNames.Control_SubMenu), display, fx, avatar,
                                 rows, visited);
                        break;
                }
            }
        }

        // ------------------------------------------------- FX graph extraction ----------------------

        /// <summary>Toggle clips straight from the graph, most-authoritative shape first. The three shapes
        /// cover VRChat authoring reality: per-toggle 1D blend trees, VRCFury direct blend trees, and
        /// two-state layers with If/IfNot (bool), Equals/NotEqual (int) or Greater/Less (float) conditions —
        /// the bool/int cases are exactly what filename scanning and the old finder kept missing.</summary>
        private static void ExtractToggle(AnimatorController fx, Row row, GameObject avatar)
        {
            foreach (var (layer, tree) in AllTrees(fx))
            {
                if (tree.blendType == BlendTreeType.Simple1D && tree.blendParameter == row.param &&
                    tree.children.Length >= 2)
                {
                    var (lo, hi) = MinMax(tree);
                    row.off = lo; row.on = hi;
                    row.provenance = $"1D blend tree '{tree.name}' in layer '{layer}'";
                    DetectNative(row, avatar);
                    return;
                }
                if (tree.blendType == BlendTreeType.Direct)
                    foreach (var ch in tree.children)
                        if (ch.directBlendParameter == row.param && ch.motion is AnimationClip on)
                        {
                            row.on = on;
                            row.provenance = $"direct blend tree '{tree.name}' in layer '{layer}' (off = resting)";
                            DetectNative(row, avatar);
                            return;
                        }
            }

            foreach (var layer in fx.layers)
                if (ExtractFromTransitions(layer.stateMachine, layer.name, row)) { DetectNative(row, avatar); return; }
        }

        private static bool ExtractFromTransitions(AnimatorStateMachine sm, string layerName, Row row)
        {
            AnimatorState onState = null, offState = null;

            void Consider(AnimatorCondition cond, AnimatorState dest, AnimatorState source)
            {
                if (cond.parameter != row.param || dest == null) return;
                switch (cond.mode)
                {
                    case AnimatorConditionMode.If: onState = onState ?? dest; break;
                    case AnimatorConditionMode.IfNot: offState = offState ?? dest; break;
                    case AnimatorConditionMode.Greater: onState = onState ?? dest; offState = offState ?? source; break;
                    case AnimatorConditionMode.Less: offState = offState ?? dest; break;
                    case AnimatorConditionMode.Equals:
                        if (Mathf.Approximately(cond.threshold, row.menuValue)) onState = onState ?? dest;
                        else offState = offState ?? dest;
                        break;
                    case AnimatorConditionMode.NotEqual:
                        if (Mathf.Approximately(cond.threshold, row.menuValue)) offState = offState ?? dest;
                        break;
                }
            }

            foreach (var t in sm.anyStateTransitions)
                foreach (var c in t.conditions) Consider(c, t.destinationState, null);
            foreach (var cs in sm.states)
                foreach (var t in cs.state.transitions)
                    foreach (var c in t.conditions) Consider(c, t.destinationState, cs.state);

            foreach (var sub in sm.stateMachines)
                if (ExtractFromTransitions(sub.stateMachine, layerName, row)) return true;

            if (onState == null && offState == null) return false;
            row.on = onState != null ? onState.motion as AnimationClip : null;
            row.off = offState != null ? offState.motion as AnimationClip : null;
            row.provenance = $"layer '{layerName}': " +
                             (onState != null ? $"on = state '{onState.name}'" : "on = ?") +
                             (offState != null ? $", off = state '{offState.name}'" : ", off = resting");
            return row.on != null || row.off != null;
        }

        private static void ExtractSlider(AnimatorController fx, Row row)
        {
            foreach (var (layer, tree) in AllTrees(fx))
            {
                if (tree.blendType == BlendTreeType.Simple1D && tree.blendParameter == row.param &&
                    tree.children.Length >= 2)
                {
                    var (lo, hi) = MinMax(tree);
                    row.off = lo; row.on = hi;
                    row.provenance = $"1D blend tree '{tree.name}' in layer '{layer}'";
                    return;
                }
                if (tree.blendType == BlendTreeType.Direct)
                    foreach (var ch in tree.children)
                        if (ch.directBlendParameter == row.param && ch.motion is AnimationClip max)
                        {
                            row.on = max;
                            row.provenance = $"direct blend tree '{tree.name}' in layer '{layer}' (min = resting)";
                            return;
                        }
            }
        }

        /// <summary>A toggle whose ON clip only flips GameObject actives needs NO clips in CVR — the AAS
        /// GameObject toggle drives the objects natively, so there is nothing to regenerate or mismatch.</summary>
        private static void DetectNative(Row row, GameObject avatar)
        {
            if (row.on == null) return;
            if (AnimationUtility.GetObjectReferenceCurveBindings(row.on).Length > 0) return;
            foreach (var b in AnimationUtility.GetCurveBindings(row.on))
            {
                if (b.propertyName != "m_IsActive") { row.nativeOn.Clear(); row.nativeOff.Clear(); return; }
                var t = avatar.transform.Find(b.path);
                if (t == null) continue;
                var curve = AnimationUtility.GetEditorCurve(row.on, b);
                if (curve == null || curve.length == 0) continue;
                if (curve.keys[0].value > 0.5f) row.nativeOn.Add((t.gameObject, b.path));
                else row.nativeOff.Add((t.gameObject, b.path));
            }
        }

        // ---------------------------------------------------------------- apply ---------------------

        public static string Apply(GameObject avatar, List<Row> rows)
        {
            if (avatar == null) return "Pick the avatar first.";
            var cvr = CckAvatar.EnsureOn(avatar);
            if (cvr == null) return "Couldn't create/find the CVRAvatar (is the CCK imported?).";

            var sync = ReadParamInfo(avatar);
            int toggles = 0, sliders = 0, native = 0, skipped = 0;
            var log = new List<string>();

            foreach (var row in rows.Where(r => r.include))
            {
                var info = sync.TryGetValue(row.param, out var i) ? i : (synced: false, def: 0f);
                var machine = info.synced ? row.param : "#" + row.param;
                bool local = machine[0] == '#';

                if (row.isSlider)
                {
                    cvr.AddSlider(row.display.Split('/').Last(), machine, Mathf.Clamp01(info.def), local, row.off, row.on);
                    sliders++;
                    log.Add($"✓ slider '{row.display}' [{row.provenance}]");
                }
                else if (row.NativeOnly)
                {
                    var targets = row.nativeOn.Select(x => (x.go, x.path)).ToList();
                    cvr.AddGameObjectToggle(row.display.Split('/').Last(), machine, info.def > 0.5f, targets);
                    // Objects the ON clip turns OFF can't ride the native entry — keep them via clips instead.
                    if (row.nativeOff.Count > 0)
                        log.Add($"✓ native toggle '{row.display}' ({targets.Count} object(s)) — note: " +
                                $"{row.nativeOff.Count} object(s) the clip turns OFF were skipped (invert them manually)");
                    else
                        log.Add($"✓ native toggle '{row.display}' ({targets.Count} object(s)) [{row.provenance}]");
                    native++;
                }
                else if (row.on != null || row.off != null)
                {
                    cvr.AddToggle(row.display.Split('/').Last(), machine, info.def > 0.5f, local, row.on, row.off);
                    toggles++;
                    log.Add($"✓ toggle '{row.display}' [{row.provenance}]");
                }
                else
                {
                    cvr.AddToggle(row.display.Split('/').Last(), machine, info.def > 0.5f, local);
                    skipped++;
                    log.Add($"→ '{row.display}': no clips found in the FX graph — entry created without " +
                            "animation (drive it via a CVRFury component or assign clips on the CVRAvatar).");
                }
            }

            cvr.Persist();
            return $"Applied from the FX graph: {native} native object toggle(s), {toggles} clip toggle(s), " +
                   $"{sliders} slider(s), {skipped} without clips.\n" + string.Join("\n", log);
        }

        // ---------------------------------------------------------------- plumbing ------------------

        private static AnimatorController FindFxController(object desc)
        {
            if (!(Reflect.GetField(desc, VrcNames.Desc_BaseAnimationLayers) is System.Collections.IEnumerable layers))
                return null;
            foreach (var layer in layers)
            {
                if ((Reflect.GetField(layer, VrcNames.Layer_Type)?.ToString() ?? "") != "FX") continue;
                var rac = Reflect.GetField(layer, VrcNames.Layer_Controller) as RuntimeAnimatorController;
                if (rac is AnimatorController ac) return ac;
                if (rac is AnimatorOverrideController oc) return oc.runtimeAnimatorController as AnimatorController;
            }
            return null;
        }

        /// <summary>param → (network-synced, default value) from VRCExpressionParameters.</summary>
        private static Dictionary<string, (bool synced, float def)> ReadParamInfo(GameObject avatar)
        {
            var res = new Dictionary<string, (bool, float)>();
            var descT = Reflect.FindType(VrcNames.AvatarDescriptorType);
            var desc = descT != null ? avatar.GetComponentInChildren(descT, true) : null;
            var ep = desc != null ? Reflect.GetField(desc, VrcNames.Desc_ExpressionParameters) : null;
            var list = Reflect.AsList(ep == null ? null : Reflect.GetField(ep, VrcNames.ExprParams_List));
            if (list == null) return res;
            foreach (var p in list)
            {
                if (p == null) continue;
                var name = Reflect.GetField(p, VrcNames.ExprParam_Name) as string;
                if (string.IsNullOrEmpty(name)) continue;
                var synced = !(Reflect.GetField(p, VrcNames.ExprParam_NetworkSynced) is bool b) || b;
                var def = Reflect.GetField(p, VrcNames.ExprParam_DefaultValue) is float f ? f : 0f;
                res[name] = (synced, def);
            }
            return res;
        }

        private static IEnumerable<(string layer, BlendTree tree)> AllTrees(AnimatorController c)
        {
            foreach (var layer in c.layers)
                foreach (var tree in TreesIn(layer.stateMachine))
                    yield return (layer.name, tree);
        }

        private static IEnumerable<BlendTree> TreesIn(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
                foreach (var t in Motions(cs.state.motion)) yield return t;
            foreach (var sub in sm.stateMachines)
                foreach (var t in TreesIn(sub.stateMachine)) yield return t;
        }

        private static IEnumerable<BlendTree> Motions(Motion m)
        {
            if (!(m is BlendTree tree)) yield break;
            yield return tree;
            foreach (var ch in tree.children)
                foreach (var sub in Motions(ch.motion)) yield return sub;
        }

        private static (AnimationClip lo, AnimationClip hi) MinMax(BlendTree tree)
        {
            ChildMotion lo = tree.children[0], hi = tree.children[0];
            foreach (var ch in tree.children)
            {
                if (ch.threshold <= lo.threshold) lo = ch;
                if (ch.threshold >= hi.threshold) hi = ch;
            }
            return (lo.motion as AnimationClip, hi.motion as AnimationClip);
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
            var p = subs[index];
            return p == null ? null : Reflect.GetField(p, VrcNames.Control_ParameterName) as string;
        }
    }
}
