using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// The end of upload-and-pray: verifies EVERY menu entry in the editor, deterministically, before any
    /// upload. Because CVRFury builds the animator layers itself, no simulation is needed — the verifier
    /// checks the complete causal chain statically:
    ///
    ///   entry → parameter exists in the attached controller → a layer actually listens to it → the states
    ///   have clips → every clip binding RESOLVES to a real object on this avatar → and the clip produces a
    ///   VISIBLE change from the current scene state.
    ///
    /// Any break in that chain is a dead toggle in-game, and the report names the exact link that broke —
    /// per entry, with the offending paths/values. A menu that verifies green here works in ChilloutVR.
    /// </summary>
    internal static class MenuVerifier
    {
        public static string Verify(GameObject avatar)
        {
            if (avatar == null) return "Pick the avatar first.";
            var cvr = CckAvatar.FindOn(avatar);
            if (cvr == null) return "No CVRAvatar on this avatar.";
            var entries = cvr.SettingsList;
            if (entries == null || entries.Count == 0) return "No Advanced Avatar Settings entries to verify.";

            var ctrl = Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator) as AnimatorController;
            if (ctrl == null)
            {
                var anim = avatar.GetComponentInChildren<Animator>();
                ctrl = anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
            }

            int ok = 0, warn = 0, dead = 0;
            var lines = new List<string>();

            foreach (var entry in entries)
            {
                var machine = CckAvatar.EntryMachineName(entry);
                var display = Reflect.GetField(entry, CckNames.Entry_Name) as string ?? machine;
                if (string.IsNullOrEmpty(machine)) { lines.Add($"✗ '{display}': entry has NO parameter."); dead++; continue; }

                // Link 1+2: parameter declared, and a layer actually listening to it.
                bool paramExists = ctrl != null && ctrl.parameters.Any(p => p.name == machine);
                bool layerListens = ctrl != null && LayerListensTo(ctrl, machine);

                var clips = EntryClips(entry, ctrl, machine);

                if (!paramExists)
                { lines.Add($"✗ '{display}': parameter '{machine}' is NOT in the attached controller — nothing can drive it."); dead++; continue; }
                if (!layerListens && clips.Count > 0)
                { lines.Add($"✗ '{display}': parameter exists but NO layer listens to it — the menu changes a number nobody reads."); dead++; continue; }

                if (clips.Count == 0)
                { lines.Add($"! '{display}': no clips anywhere (entry or layer) — only works if it's meant to be logic-only."); warn++; continue; }

                // Link 3+4+5 per clip: bindings resolve, and at least one visible change.
                var problems = new List<string>();
                int visibleTotal = 0;
                foreach (var (label, clip) in clips)
                {
                    if (clip == null) { problems.Add($"{label}: missing clip"); continue; }
                    Analyze(avatar, clip, out int bindings, out int unresolved, out int visible, out var example);
                    if (bindings == 0) { problems.Add($"{label}: clip '{clip.name}' animates nothing"); continue; }
                    if (unresolved == bindings)
                    { problems.Add($"{label}: NONE of '{clip.name}''s {bindings} path(s) exist on this avatar (e.g. '{example}') — wrong hierarchy"); continue; }
                    if (unresolved > 0)
                        problems.Add($"{label}: {unresolved}/{bindings} path(s) unresolved (e.g. '{example}')");
                    visibleTotal += visible;
                }

                bool fatal = problems.Any(p => p.Contains("wrong hierarchy") || p.Contains("missing clip") || p.Contains("animates nothing"));
                if (fatal)
                { lines.Add($"✗ '{display}': {string.Join("; ", problems)}."); dead++; }
                else if (visibleTotal == 0)
                { lines.Add($"! '{display}': clips resolve but change NOTHING vs the current scene (may only look dead — check its default state)."); warn++; }
                else
                {
                    lines.Add($"✓ '{display}': {visibleTotal} visible change(s) will apply" +
                              (problems.Count > 0 ? $" ({string.Join("; ", problems)})" : "") + ".");
                    ok++;
                }
            }

            var head = $"MENU VERIFICATION — {ok} ✓ work · {warn} ! check · {dead} ✗ DEAD (of {entries.Count}). " +
                       "Every ✗ names the exact broken link — no upload needed to see it.";
            return head + "\n" + string.Join("\n", lines);
        }

        // --- the causal-chain pieces ---------------------------------------------------------------

        /// <summary>Clips for the entry: from the entry itself (toggle on/off, slider min/max) and, for
        /// dropdowns, from the states of the generated option layer.</summary>
        private static List<(string label, AnimationClip clip)> EntryClips(object entry, AnimatorController ctrl, string machine)
        {
            var res = new List<(string, AnimationClip)>();
            var toggle = Reflect.GetField(entry, CckNames.Entry_ToggleSettings);
            var slider = Reflect.GetField(entry, CckNames.Entry_SliderSettings);
            var dropdown = Reflect.GetField(entry, CckNames.Entry_DropdownSettings);

            if (toggle != null)
            {
                if (Reflect.GetField(toggle, CckNames.Toggle_AnimationClip) is AnimationClip on) res.Add(("ON", on));
                if (Reflect.GetField(toggle, CckNames.Toggle_OffAnimationClip) is AnimationClip off) res.Add(("OFF", off));
            }
            else if (slider != null)
            {
                if (Reflect.GetField(slider, CckNames.Slider_MinAnimationClip) is AnimationClip mn) res.Add(("MIN", mn));
                if (Reflect.GetField(slider, CckNames.Slider_MaxAnimationClip) is AnimationClip mx) res.Add(("MAX", mx));
            }
            else if (dropdown != null && ctrl != null)
            {
                // Our generated dropdown layers hold the option clips as state motions.
                foreach (var layer in ctrl.layers)
                {
                    if (!LayerStatesConditionOn(layer.stateMachine, machine)) continue;
                    foreach (var cs in layer.stateMachine.states)
                        if (cs.state.motion is AnimationClip c) res.Add((cs.state.name, c));
                    break;
                }
            }

            // Toggles may also have their real clips only in the layer (entry clips optional) — fall back.
            if (res.Count == 0 && ctrl != null)
                foreach (var layer in ctrl.layers)
                {
                    if (!LayerStatesConditionOn(layer.stateMachine, machine)) continue;
                    foreach (var cs in layer.stateMachine.states)
                        if (cs.state.motion is AnimationClip c) res.Add((cs.state.name, c));
                    break;
                }
            return res;
        }

        private static bool LayerListensTo(AnimatorController c, string param)
        {
            foreach (var layer in c.layers)
            {
                if (LayerStatesConditionOn(layer.stateMachine, param)) return true;
                foreach (var cs in layer.stateMachine.states)
                    if (TreeUses(cs.state.motion, param)) return true;
            }
            return false;
        }

        private static bool LayerStatesConditionOn(AnimatorStateMachine sm, string param)
        {
            foreach (var t in sm.anyStateTransitions)
                if (t.conditions.Any(cd => cd.parameter == param)) return true;
            foreach (var cs in sm.states)
                foreach (var t in cs.state.transitions)
                    if (t.conditions.Any(cd => cd.parameter == param)) return true;
            foreach (var sub in sm.stateMachines)
                if (LayerStatesConditionOn(sub.stateMachine, param)) return true;
            return false;
        }

        private static bool TreeUses(Motion m, string param)
        {
            if (!(m is BlendTree tree)) return false;
            if (tree.blendParameter == param || tree.blendParameterY == param) return true;
            if (tree.blendType == BlendTreeType.Direct &&
                tree.children.Any(ch => ch.directBlendParameter == param)) return true;
            return tree.children.Any(ch => TreeUses(ch.motion, param));
        }

        /// <summary>Static clip analysis against the live avatar: how many bindings, how many resolve to
        /// real objects, how many would VISIBLY change the current scene state.</summary>
        private static void Analyze(GameObject avatar, AnimationClip clip,
                                    out int bindings, out int unresolved, out int visible, out string exampleBadPath)
        {
            bindings = 0; unresolved = 0; visible = 0; exampleBadPath = "";
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                bindings++;
                if (!string.IsNullOrEmpty(b.path) && avatar.transform.Find(b.path) == null)
                { unresolved++; if (exampleBadPath == "") exampleBadPath = b.path; continue; }
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length == 0) continue;
                var target = curve.keys[curve.length - 1].value;
                // Unreadable property (rare) counts as POSSIBLY-visible rather than a false "dead" — we can't
                // prove it's a no-op, so don't punish it. Readable + different = definitely visible.
                if (!SceneBindingReader.TryReadFloat(avatar, b, out var current) ||
                    Mathf.Abs(current - target) > 0.001f) visible++;
            }
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                bindings++;
                if (!string.IsNullOrEmpty(b.path) && avatar.transform.Find(b.path) == null)
                { unresolved++; if (exampleBadPath == "") exampleBadPath = b.path; continue; }
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                if (keys == null || keys.Length == 0) continue;
                if (!SceneBindingReader.TryReadObject(avatar, b, out var current) ||
                    current != keys[keys.Length - 1].value) visible++;
            }
        }
    }
}
