using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Post-generation X-ray of the controller CVRFury hands to ChilloutVR's Advanced Avatar
    /// Settings. It exists to turn the two field-only symptoms — the avatar stuck in the
    /// "motorcycle pose", and menu toggles that appear but do nothing — into something visible in
    /// the build log, because neither can be diagnosed from CVRFury's own bookkeeping (the
    /// conversion log reports success while the in-game result is wrong).
    ///
    /// It answers two concrete questions:
    ///   1. Which layers animate humanoid muscles / Transforms at their default state? An Override
    ///      layer that poses the body at weight 1 overrides CVR locomotion → the motorcycle pose.
    ///   2. Is each AAS entry's machineName parameter actually READ by some transition condition or
    ///      blend-tree parameter? A machineName nothing reads is a dead toggle — CVR drives the
    ///      synced value but no layer responds.
    ///
    /// Output is bounded (it lists at most a handful of offenders per category) so it stays
    /// readable when pasted back from the Unity console.
    /// </summary>
    internal static class ControllerDiagnostics
    {
        private const int MaxList = 20;

        public static void Report(AnimatorController gen, IEnumerable<object> entries, BuildLog log)
        {
            if (gen == null) { log.Warning("AAS diagnostic skipped: no generated controller."); return; }

            ReportPoseSuspects(gen, log);
            ReportDeadToggles(gen, entries, log);
        }

        // --- "motorcycle pose": layers that pose the body at their default state ---------------
        private static void ReportPoseSuspects(AnimatorController gen, BuildLog log)
        {
            var suspects = new List<string>();
            int bodyLayers = 0;
            foreach (var layer in gen.layers)
            {
                var def = layer.stateMachine != null ? layer.stateMachine.defaultState : null;
                var defClip = def != null ? def.motion as AnimationClip : null;
                bool muscles = false, transforms = false;
                foreach (var clip in ClipsIn(def != null ? def.motion : null))
                {
                    if (AnimatesMuscles(clip)) muscles = true;
                    if (AnimatesTransforms(clip)) transforms = true;
                }
                if (!muscles && !transforms) continue;
                bodyLayers++;
                // Weight-1 Override layers posing the body are the ones that fight locomotion.
                if (layer.defaultWeight > 0f && suspects.Count < MaxList)
                    suspects.Add($"  L '{layer.name}' weight={layer.defaultWeight:0.##} " +
                                 $"mode={layer.blendingMode} default='{(defClip ? defClip.name : "(none/blendtree)")}'" +
                                 $"{(muscles ? " [muscles]" : "")}{(transforms ? " [transforms]" : "")}");
            }

            if (bodyLayers == 0)
            {
                log.Info("AAS diagnostic — pose: no layer animates humanoid muscles/Transforms at its " +
                         "default state, so the generated controller should not force a body pose.");
                return;
            }

            log.Warning($"AAS diagnostic — pose: {bodyLayers} layer(s) animate the body (muscles/Transforms) " +
                        $"at their default state. At weight>0 these override CVR locomotion and are the likely " +
                        $"cause of the 'motorcycle pose'. Suspects:\n" + string.Join("\n", suspects));
        }

        // --- "dead toggles": machineNames no layer reads ---------------------------------------
        private static void ReportDeadToggles(AnimatorController gen, IEnumerable<object> entries, BuildLog log)
        {
            if (entries == null) return;
            var read = ParametersReadBy(gen);
            var declared = new HashSet<string>(gen.parameters.Select(p => p.name));

            int total = 0, dead = 0, undeclared = 0;
            var deadList = new List<string>();
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var machineName = Reflect.GetField(entry, CckNames.Entry_MachineName) as string;
                if (string.IsNullOrEmpty(machineName)) continue;
                total++;
                bool isRead = read.Contains(machineName);
                bool isDeclared = declared.Contains(machineName);
                if (!isDeclared) undeclared++;
                if (!isRead)
                {
                    dead++;
                    if (deadList.Count < MaxList)
                        deadList.Add($"  '{machineName}'{(isDeclared ? "" : " (not even declared as a parameter)")}");
                }
            }

            if (dead == 0)
            {
                log.Info($"AAS diagnostic — toggles: all {total} AAS machineName parameter(s) are read by a " +
                         "transition condition or blend-tree parameter, so each toggle has a layer that responds.");
                return;
            }

            log.Warning($"AAS diagnostic — toggles: {dead}/{total} AAS machineName parameter(s) are NOT read by " +
                        $"any transition condition or blend-tree parameter in the generated controller " +
                        $"({undeclared} aren't even declared). Nothing responds when CVR drives these, which is the " +
                        $"'toggle does nothing' symptom. Dead machineNames:\n" + string.Join("\n", deadList));
        }

        /// <summary>Every parameter name referenced by a transition condition, a blend-tree blend
        /// parameter, or a direct-blend child parameter — i.e. names that actually drive behaviour.</summary>
        private static HashSet<string> ParametersReadBy(AnimatorController c)
        {
            var read = new HashSet<string>();
            foreach (var layer in c.layers)
                CollectReads(layer.stateMachine, read);
            return read;
        }

        private static void CollectReads(AnimatorStateMachine sm, HashSet<string> read)
        {
            if (sm == null) return;
            foreach (var cs in sm.states)
            {
                foreach (var t in cs.state.transitions)
                    foreach (var cond in t.conditions) Add(read, cond.parameter);
                foreach (var tree in TreesIn(cs.state.motion)) AddTreeParams(tree, read);
            }
            foreach (var t in sm.anyStateTransitions)
                foreach (var cond in t.conditions) Add(read, cond.parameter);
            foreach (var t in sm.entryTransitions)
                foreach (var cond in t.conditions) Add(read, cond.parameter);
            foreach (var child in sm.stateMachines) CollectReads(child.stateMachine, read);
        }

        private static void AddTreeParams(BlendTree tree, HashSet<string> read)
        {
            Add(read, tree.blendParameter);
            Add(read, tree.blendParameterY);
            foreach (var ch in tree.children) Add(read, ch.directBlendParameter);
        }

        private static void Add(HashSet<string> set, string name)
        {
            if (!string.IsNullOrEmpty(name)) set.Add(name);
        }

        // --- curve / motion helpers -------------------------------------------------------------
        private static bool AnimatesMuscles(AnimationClip clip) =>
            HumanoidCurves.PosesHumanoid(clip); // muscle/root/IK curves only — not AAP parameter curves

        private static bool AnimatesTransforms(AnimationClip clip)
        {
            if (clip == null) return false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (b.type == typeof(Transform)) return true;
            return false;
        }

        private static IEnumerable<AnimationClip> ClipsIn(Motion m)
        {
            if (m is AnimationClip clip) { yield return clip; yield break; }
            if (m is BlendTree tree)
                foreach (var ch in tree.children)
                    foreach (var c in ClipsIn(ch.motion)) yield return c;
        }

        private static IEnumerable<BlendTree> TreesIn(Motion m)
        {
            if (!(m is BlendTree tree)) yield break;
            yield return tree;
            foreach (var ch in tree.children)
                foreach (var t in TreesIn(ch.motion)) yield return t;
        }
    }
}
