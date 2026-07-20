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
    /// mystery after. Both toggle states are always explicit: missing OFF clips are synthesized (object actives
    /// inverted, everything else restored to scene values), and shared-parameter outfit systems fold into
    /// real Int dropdowns with per-option layers.
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

            // Set when several menu controls share ONE parameter with different values (the classic
            // one-Int outfit system): this row becomes a single DROPDOWN and `options` replaces on/off.
            public List<Option> options;
        }

        internal sealed class Option
        {
            public string label;
            public float value;          // the VRChat value this option selected (extraction only)
            public AnimationClip clip;   // plays while this option is selected
            public string provenance = "";
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
            rows = GroupSharedParams(rows);

            int found = rows.Count(r => r.on != null || r.off != null || r.options != null);
            int dropdowns = rows.Count(r => r.options != null);
            summary = $"{rows.Count} menu entr(ies) · {found} matched in the FX graph · {dropdowns} shared-" +
                      "parameter group(s) folded into dropdowns (one-Int outfit systems)" +
                      (fx == null ? " · ⚠ NO FX controller found" : "") +
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

        /// <summary>Several menu controls sharing ONE parameter with different values (the classic one-Int
        /// outfit system) must become a SINGLE dropdown — one Bool entry per control would both collapse to
        /// one AAS entry (machine names collide) and mistype the Int as Bool, which is exactly why clothing
        /// menus died. Each control becomes an option carrying the clip extracted for ITS value; a value-0
        /// "Off" option is prepended when none of the controls covers 0.</summary>
        private static List<Row> GroupSharedParams(List<Row> rows)
        {
            var res = new List<Row>();
            foreach (var group in rows.GroupBy(r => (r.param, r.isSlider)))
            {
                var list = group.ToList();
                if (group.Key.isSlider || list.Count == 1) { res.AddRange(list); continue; }

                var options = list.OrderBy(r => r.menuValue).Select(r => new Option
                {
                    label = r.display.Split('/').Last(),
                    value = r.menuValue,
                    clip = r.on,
                    provenance = r.provenance,
                }).ToList();
                if (!options.Any(o => Mathf.Approximately(o.value, 0f)))
                    options.Insert(0, new Option { label = "Off", value = 0f, provenance = "default state (resting)" });

                res.Add(new Row
                {
                    display = list[0].display.Contains("/")
                        ? list[0].display.Substring(0, list[0].display.LastIndexOf('/'))
                        : group.Key.param,
                    param = group.Key.param,
                    options = options,
                    provenance = $"{list.Count} controls share parameter '{group.Key.param}' → one dropdown",
                });
            }
            return res;
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
                    return;
                }
                if (tree.blendType == BlendTreeType.Direct)
                    foreach (var ch in tree.children)
                        if (ch.directBlendParameter == row.param && ch.motion is AnimationClip on)
                        {
                            row.on = on;
                            row.provenance = $"direct blend tree '{tree.name}' in layer '{layer}' (off = resting)";
                            return;
                        }
            }

            foreach (var layer in fx.layers)
                if (ExtractFromTransitions(layer.stateMachine, layer.name, row)) return;
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

        // ---------------------------------------------------------------- apply ---------------------

        public static string Apply(GameObject avatar, List<Row> rows)
        {
            if (avatar == null) return "Pick the avatar first.";
            var cvr = CckAvatar.EnsureOn(avatar);
            if (cvr == null) return "Couldn't create/find the CVRAvatar (is the CCK imported?).";

            var sync = ReadParamInfo(avatar);
            var dropdownClips = new Dictionary<string, IList<AnimationClip>>();
            int toggles = 0, sliders = 0, dropdowns = 0, bare = 0;
            var log = new List<string>();

            foreach (var row in rows.Where(r => r.include))
            {
                var info = sync.TryGetValue(row.param, out var i) ? i : (synced: false, def: 0f);
                var machine = CckAvatar.SanitizeMachineName(info.synced ? row.param : "#" + row.param);
                bool local = machine[0] == '#';
                var leaf = row.display.Split('/').Last();

                if (row.options != null)
                {
                    // One-Int outfit system → ONE dropdown. Options missing a clip (usually "Off") get a
                    // synthesized resting clip covering everything the other options animate, so selecting
                    // them explicitly restores the default look instead of leaving the last outfit stuck.
                    var union = row.options.Where(o => o.clip != null).Select(o => o.clip).ToList();
                    var clips = new List<AnimationClip>();
                    foreach (var o in row.options)
                        clips.Add(o.clip != null ? o.clip : SynthesizeResting(avatar, union, $"{leaf} {o.label}"));
                    int defIdx = 0;
                    for (int k = 0; k < row.options.Count; k++)
                        if (Mathf.Approximately(row.options[k].value, info.def)) { defIdx = k; break; }

                    cvr.AddDropdown(leaf, machine, row.options.Select(o => o.label).ToArray(), defIdx, local);
                    dropdownClips[machine] = clips;
                    dropdowns++;
                    log.Add($"✓ dropdown '{row.display}' — {row.options.Count} option(s): " +
                            string.Join(", ", row.options.Select(o => o.label)) + $" [{row.provenance}]");
                }
                else if (row.isSlider)
                {
                    cvr.AddSlider(leaf, machine, Mathf.Clamp01(info.def), local, row.off, row.on);
                    sliders++;
                    log.Add($"✓ slider '{row.display}' [{row.provenance}]");
                }
                else
                {
                    var on = row.on;
                    var off = row.off;
                    if (on == null && off != null) { on = off; off = null; } // odd graphs: found clip = ON
                    // BOTH states must always exist explicitly: with WriteDefaults off, an empty Off state
                    // writes nothing and the toggle is one-way or dead. Missing OFF is synthesized —
                    // object actives get the INVERSE of the ON value, everything else the current scene value.
                    if (on != null && off == null) off = SynthesizeOff(avatar, on);

                    if (on != null)
                    {
                        cvr.AddToggle(leaf, machine, info.def > 0.5f, local, on, off);
                        toggles++;
                        log.Add($"✓ toggle '{row.display}' [{row.provenance}]" +
                                (row.off == null ? " (off clip synthesized)" : ""));
                    }
                    else
                    {
                        cvr.AddToggle(leaf, machine, info.def > 0.5f, local);
                        bare++;
                        log.Add($"→ '{row.display}': no clips found in the FX graph — entry created without " +
                                "animation (drive it via a CVRFury component or assign clips manually).");
                    }
                }
            }

            cvr.Persist();

            // The other half Step 2 always did: build the animator parameters + masked layers for every
            // entry into a locomotion-carrying controller and attach it. Dropdown groups get real
            // multi-state Int layers from the per-option clips.
            var buildReport = ToggleClipLinker.BuildAndAttach(cvr, avatar, cvr.SettingsList, null, dropdownClips);

            return $"Applied from the FX graph: {toggles} toggle(s), {dropdowns} dropdown(s), {sliders} " +
                   $"slider(s), {bare} without clips.\n" + string.Join("\n", log) +
                   "\n\nController: " + buildReport;
        }

        // ------------------------------------------------- clip synthesis ---------------------------

        private const string WizardDir = "Assets/CVRFury Generated/Wizard";

        /// <summary>OFF clip for a toggle: object actives inverted (a clip that shows the jacket gets an off
        /// clip that hides it), every other property restored to its current scene value, material swaps
        /// restored to the current material.</summary>
        private static AnimationClip SynthesizeOff(GameObject avatar, AnimationClip on)
        {
            var off = new AnimationClip { name = on.name + " Off (CVRFury)" };
            FillResting(off, avatar, new[] { on });
            return SaveClip(off);
        }

        /// <summary>Resting clip covering every property the given clips animate — the explicit "none of
        /// these outfits" state for dropdown Off options.</summary>
        private static AnimationClip SynthesizeResting(GameObject avatar, IEnumerable<AnimationClip> union, string name)
        {
            var clip = new AnimationClip { name = name + " (CVRFury resting)" };
            FillResting(clip, avatar, union);
            return SaveClip(clip);
        }

        private static void FillResting(AnimationClip dst, GameObject avatar, IEnumerable<AnimationClip> sources)
        {
            // NEVER skip a binding: an OFF state that misses even one property the ON clip writes leaves
            // that property stuck forever (WD off). Rules: actives/enabled invert; anything readable takes
            // its current scene value (SceneBindingReader covers material.* and friends that Unity's
            // GetFloatValue can't); the last-resort fallback flips the ON value.
            var done = new HashSet<(string, string)>();
            foreach (var src in sources)
            {
                if (src == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(src))
                {
                    if (!done.Add((b.path, b.propertyName))) continue;
                    var curve = AnimationUtility.GetEditorCurve(src, b);
                    if (curve == null || curve.length == 0) continue;
                    var onVal = curve.keys[curve.length - 1].value;
                    float rest;
                    if (b.propertyName == "m_IsActive" || b.propertyName == "m_Enabled")
                        rest = onVal > 0.5f ? 0f : 1f;
                    else if (!SceneBindingReader.TryReadFloat(avatar, b, out rest))
                        rest = Mathf.Abs(onVal) > 0.001f ? 0f : 1f;
                    AnimationUtility.SetEditorCurve(dst, b, AnimationCurve.Constant(0f, 1f / 60f, rest));
                }
                foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(src))
                {
                    if (!done.Add((b.path, b.propertyName))) continue;
                    if (!SceneBindingReader.TryReadObject(avatar, b, out var cur)) continue;
                    AnimationUtility.SetObjectReferenceCurve(dst, b,
                        new[] { new ObjectReferenceKeyframe { time = 0f, value = cur } });
                }
            }
        }

        private static AnimationClip SaveClip(AnimationClip clip)
        {
            if (!AssetDatabase.IsValidFolder("Assets/CVRFury Generated"))
                AssetDatabase.CreateFolder("Assets", "CVRFury Generated");
            if (!AssetDatabase.IsValidFolder(WizardDir))
                AssetDatabase.CreateFolder("Assets/CVRFury Generated", "Wizard");
            var file = string.Join("_", clip.name.Split(System.IO.Path.GetInvalidFileNameChars()));
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath($"{WizardDir}/{file}.anim"));
            return clip;
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
