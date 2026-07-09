using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Builds an in-game "Crouch / Prone style" dropdown from locomotion animation packs (clips OR blend-tree
    /// assets, e.g. the CCK BaseAnimatorPatch styles). Styles are injected as states INSIDE the base
    /// locomotion layer — the same pattern that made dances safe — gated on the style dropdown AND the
    /// matching CVR pose parameter (Crouching / Prone), so a style plays only while actually crouched/prone
    /// and "Default" (0) leaves CVR's stock locomotion untouched. No SimpleAAS or controller surgery needed.
    /// </summary>
    internal static class LocoStyles
    {
        public enum Trigger { Crouch, Prone }

        public struct Style { public string name; public Motion motion; public Trigger trigger; }

        public static string Build(GameObject avatar, CckAvatar cvr, AnimatorController[] controllers,
                                   List<Style> styles, string menuName = "Crouch Style", string param = "LocoStyle")
        {
            if (cvr == null) return "No CVRAvatar — run Step 1 first.";
            if (controllers == null || controllers.Length == 0) return "No writable controller — run Step 2 first.";
            var valid = styles?.Where(s => s.motion != null).ToList() ?? new List<Style>();
            if (valid.Count == 0) return "Add at least one style with a motion (a clip or a blend-tree asset).";

            int added = 0;
            foreach (var c in controllers.Where(x => x != null).Distinct())
            {
                Clean(c);
                AnimatorUtil.EnsureIntParam(c, param, 0);
                if (c.layers.Length == 0) continue;
                var sm = c.layers[0].stateMachine;
                var home = sm.defaultState;
                bool wd = home != null && home.writeDefaultValues;

                for (int i = 0; i < valid.Count; i++)
                {
                    var s = valid[i];
                    var pose = s.trigger == Trigger.Crouch ? "Crouching" : "Prone";
                    var st = sm.AddState("CVRFury LocoStyle " + s.name, new Vector3(600, 80 + i * 60, 0));
                    st.motion = s.motion;
                    st.writeDefaultValues = wd;

                    var enter = sm.AddAnyStateTransition(st);
                    enter.hasExitTime = false; enter.hasFixedDuration = true; enter.duration = 0.15f;
                    enter.canTransitionToSelf = false;
                    enter.AddCondition(AnimatorConditionMode.Equals, i + 1, param);
                    AddPoseCondition(c, enter, pose, true);

                    if (home != null)
                    {
                        var offStyle = st.AddTransition(home);
                        offStyle.hasExitTime = false; offStyle.hasFixedDuration = true; offStyle.duration = 0.15f;
                        offStyle.AddCondition(AnimatorConditionMode.NotEqual, i + 1, param);

                        var unposed = st.AddTransition(home);
                        unposed.hasExitTime = false; unposed.hasFixedDuration = true; unposed.duration = 0.15f;
                        AddPoseCondition(c, unposed, pose, false);
                    }
                }
                EditorUtility.SetDirty(c);
                added++;
            }
            if (added == 0) return "No controller layer to add styles to — run Step 2 (Build & attach) first.";

            RemoveEntry(cvr, param);
            var names = new[] { "Default" }.Concat(valid.Select(s => s.name)).ToArray();
            if (!cvr.AddDropdown(menuName, param, names, 0, false))
                return "Built the style states, but couldn't add the menu dropdown (AAS write failed).";

            AssetDatabase.SaveAssets();
            cvr.Persist();
            return $"Built the \"{menuName}\" dropdown with {valid.Count} style(s) (plus Default). Each plays " +
                   "only while you're actually crouched/prone with that style selected; Default keeps CVR's " +
                   "normal locomotion. Test in Play mode (tester can set Crouching) or upload.";
        }

        /// <summary>Condition on a CVR pose parameter whose type we don't control: If/IfNot for Bool,
        /// a 0.5 threshold window for Float/Int. Ensures the parameter exists (Float) if absent.</summary>
        private static void AddPoseCondition(AnimatorController c, AnimatorStateTransition t, string pose, bool active)
        {
            var p = c.parameters.FirstOrDefault(x => x.name == pose);
            if (p == null) { AnimatorUtil.EnsureFloatParam(c, pose, 0f); p = c.parameters.First(x => x.name == pose); }
            if (p.type == AnimatorControllerParameterType.Bool)
                t.AddCondition(active ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, pose);
            else
                t.AddCondition(active ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.5f, pose);
        }

        private static void Clean(AnimatorController c)
        {
            if (c.layers.Length == 0) return;
            var sm = c.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions.ToList())
                if (t.destinationState != null && t.destinationState.name.StartsWith("CVRFury LocoStyle "))
                    sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states.ToList())
                if (s.state != null && s.state.name.StartsWith("CVRFury LocoStyle "))
                    sm.RemoveState(s.state);
        }

        private static void RemoveEntry(CckAvatar cvr, string machine)
        {
            var list = cvr.SettingsList;
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
                if (CckAvatar.EntryMachineName(list[i]) == machine) list.RemoveAt(i);
        }
    }
}
