using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Locates the animation clip(s) a VRChat menu control drives inside the merged FX controller, so
    /// they can be attached to a ChilloutVR Advanced Avatar Settings entry. ChilloutVR builds the
    /// working toggle/slider animator layer from the AAS entry's clips at upload time, so without
    /// these a converted toggle appears in the menu but does nothing.
    ///
    /// Recognises the common shapes: a per-toggle 1D blend tree (two clips at min/max), a Direct
    /// Blend Tree weight (the child clip is "on"; "off" is synthesised by zeroing its float curves,
    /// which matches a blend weight of 0), and a simple two-state transition driven by the parameter.
    /// </summary>
    internal static class ToggleClipFinder
    {
        /// <summary>On/off clips for a toggle parameter, or (null,null) if none could be found.</summary>
        public static (AnimationClip on, AnimationClip off) FindToggle(AnimatorController c, string param,
                                                                       AssetSaver assets)
        {
            // 1D blend tree (param, two clip children) — real off/on clips.
            foreach (var tree in AllTrees(c))
            {
                if (tree.blendType != BlendTreeType.Simple1D || tree.blendParameter != param) continue;
                if (tree.children.Length != 2) continue;
                var a = tree.children[0].motion as AnimationClip;
                var b = tree.children[1].motion as AnimationClip;
                if (a == null || b == null) continue;
                return tree.children[0].threshold <= tree.children[1].threshold ? (b, a) : (a, b);
            }

            // Direct blend tree weight — child clip is "on"; synthesise a zeroed "off".
            foreach (var tree in AllTrees(c))
            {
                if (tree.blendType != BlendTreeType.Direct) continue;
                foreach (var ch in tree.children)
                {
                    if (ch.directBlendParameter != param) continue;
                    if (!(ch.motion is AnimationClip on) || HasObjectOrTransformCurves(on)) continue;
                    var off = MakeZeroed(on);
                    assets.AddSubAsset(off, c);
                    return (on, off);
                }
            }

            // Two-state transition driven by the parameter (Greater → On state's clip).
            foreach (var layer in c.layers)
            {
                var found = FindTransitionClips(layer.stateMachine, param);
                if (found.on != null) return found;
            }

            return (null, null);
        }

        /// <summary>Min/max clips for a radial slider (a 1D blend tree on the parameter).</summary>
        public static (AnimationClip min, AnimationClip max) FindRadial(AnimatorController c, string param)
        {
            foreach (var tree in AllTrees(c))
            {
                if (tree.blendType != BlendTreeType.Simple1D || tree.blendParameter != param) continue;
                if (tree.children.Length < 2) continue;
                // lowest- and highest-threshold clip children
                ChildMotion lo = tree.children[0], hi = tree.children[0];
                foreach (var ch in tree.children)
                {
                    if (ch.threshold <= lo.threshold) lo = ch;
                    if (ch.threshold >= hi.threshold) hi = ch;
                }
                var minClip = lo.motion as AnimationClip;
                var maxClip = hi.motion as AnimationClip;
                if (minClip != null && maxClip != null) return (minClip, maxClip);
            }
            return (null, null);
        }

        private static (AnimationClip on, AnimationClip off) FindTransitionClips(AnimatorStateMachine sm,
                                                                                 string param)
        {
            foreach (var cs in sm.states)
                foreach (var t in cs.state.transitions)
                    foreach (var cond in t.conditions)
                        if (cond.parameter == param && cond.mode == AnimatorConditionMode.Greater &&
                            t.destinationState != null &&
                            t.destinationState.motion is AnimationClip on && cs.state.motion is AnimationClip off)
                            return (on, off);

            foreach (var child in sm.stateMachines)
            {
                var f = FindTransitionClips(child.stateMachine, param);
                if (f.on != null) return f;
            }
            return (null, null);
        }

        private static bool HasObjectOrTransformCurves(AnimationClip clip)
        {
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0) return true;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (b.type == typeof(Transform)) return true;
            return false;
        }

        private static AnimationClip MakeZeroed(AnimationClip on)
        {
            var off = new AnimationClip { name = on.name + " (Off)", frameRate = on.frameRate };
            foreach (var b in AnimationUtility.GetCurveBindings(on))
                AnimationUtility.SetEditorCurve(off, b, new AnimationCurve(new Keyframe(0f, 0f)));
            return off;
        }

        private static IEnumerable<BlendTree> AllTrees(AnimatorController c)
        {
            foreach (var layer in c.layers)
                foreach (var t in TreesIn(layer.stateMachine))
                    yield return t;
        }

        private static IEnumerable<BlendTree> TreesIn(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
                foreach (var t in Motions(cs.state.motion)) yield return t;
            foreach (var child in sm.stateMachines)
                foreach (var t in TreesIn(child.stateMachine)) yield return t;
        }

        private static IEnumerable<BlendTree> Motions(Motion m)
        {
            if (!(m is BlendTree tree)) yield break;
            yield return tree;
            foreach (var ch in tree.children)
                foreach (var t in Motions(ch.motion)) yield return t;
        }
    }
}
