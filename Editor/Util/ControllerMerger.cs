using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Merges the layers and parameters of a source <see cref="AnimatorController"/> into a
    /// destination (asset-backed) one, optionally prefixing parameter names to avoid collisions.
    /// State machines are rebuilt in-place via the AnimatorController API so every state,
    /// transition and sub-machine attaches to the destination asset correctly; blend trees are
    /// added as sub-assets explicitly. Every parameter reference (conditions, blend-tree params,
    /// time/speed/mirror/cycle params) is remapped through the prefix.
    ///
    /// This is the engine behind the Full Controller feature. It covers the constructs ChilloutVR
    /// avatars actually use; VRChat-style state behaviours have no CVR equivalent and are ignored.
    /// </summary>
    internal static class ControllerMerger
    {
        public static void Merge(AnimatorController dst, AnimatorController src, AssetSaver assets,
                                 string paramPrefix, BuildLog log)
        {
            if (dst == null || src == null) return;
            var remap = new Dictionary<string, string>();

            // --- parameters ---
            var existing = new HashSet<string>(dst.parameters.Select(p => p.name));
            foreach (var p in src.parameters)
            {
                var newName = string.IsNullOrEmpty(paramPrefix) ? p.name : paramPrefix + p.name;
                remap[p.name] = newName;
                if (existing.Contains(newName)) continue;
                dst.AddParameter(new AnimatorControllerParameter
                {
                    name = newName,
                    type = p.type,
                    defaultBool = p.defaultBool,
                    defaultFloat = p.defaultFloat,
                    defaultInt = p.defaultInt,
                });
                existing.Add(newName);
            }

            // --- layers ---
            foreach (var srcLayer in src.layers)
            {
                var name = AnimatorUtil.UniqueLayerName(dst, srcLayer.name);
                dst.AddLayer(name);
                var layers = dst.layers;
                var idx = layers.Length - 1;
                layers[idx].defaultWeight = dst.layers.Length == 1 ? 1f : srcLayer.defaultWeight;
                layers[idx].blendingMode = srcLayer.blendingMode;
                layers[idx].iKPass = srcLayer.iKPass;
                layers[idx].avatarMask = srcLayer.avatarMask;
                dst.layers = layers;

                var stateMap = new Dictionary<AnimatorState, AnimatorState>();
                var smMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();

                Populate(dst, dst.layers[idx].stateMachine, srcLayer.stateMachine, remap, assets, stateMap, smMap);
                Wire(srcLayer.stateMachine, remap, stateMap, smMap);
            }
        }

        /// <summary>Copy states + nested machines into <paramref name="dstSm"/> (already owned by
        /// the asset). Transitions are wired in a second pass once all states exist.</summary>
        private static void Populate(AnimatorController dst, AnimatorStateMachine dstSm,
                                     AnimatorStateMachine srcSm, Dictionary<string, string> remap,
                                     AssetSaver assets,
                                     Dictionary<AnimatorState, AnimatorState> stateMap,
                                     Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            smMap[srcSm] = dstSm;

            foreach (var cs in srcSm.states)
            {
                var s = cs.state;
                var ns = dstSm.AddState(s.name, cs.position);
                ns.speed = s.speed;
                ns.cycleOffset = s.cycleOffset;
                ns.mirror = s.mirror;
                ns.iKOnFeet = s.iKOnFeet;
                ns.writeDefaultValues = s.writeDefaultValues;
                ns.tag = s.tag;
                ns.motion = CloneMotion(s.motion, remap, dst, assets);
                ns.timeParameterActive = s.timeParameterActive;
                ns.timeParameter = Map(remap, s.timeParameter);
                ns.speedParameterActive = s.speedParameterActive;
                ns.speedParameter = Map(remap, s.speedParameter);
                ns.mirrorParameterActive = s.mirrorParameterActive;
                ns.mirrorParameter = Map(remap, s.mirrorParameter);
                ns.cycleOffsetParameterActive = s.cycleOffsetParameterActive;
                ns.cycleOffsetParameter = Map(remap, s.cycleOffsetParameter);
                stateMap[s] = ns;
            }

            foreach (var child in srcSm.stateMachines)
            {
                var nestedDst = dstSm.AddStateMachine(child.stateMachine.name, child.position);
                Populate(dst, nestedDst, child.stateMachine, remap, assets, stateMap, smMap);
            }
        }

        private static void Wire(AnimatorStateMachine srcSm, Dictionary<string, string> remap,
                                 Dictionary<AnimatorState, AnimatorState> stateMap,
                                 Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            var dstSm = smMap[srcSm];
            if (srcSm.defaultState != null && stateMap.TryGetValue(srcSm.defaultState, out var def))
                dstSm.defaultState = def;

            foreach (var cs in srcSm.states)
            {
                if (!stateMap.TryGetValue(cs.state, out var ns)) continue;
                foreach (var t in cs.state.transitions)
                {
                    if (t.destinationState == null || !stateMap.TryGetValue(t.destinationState, out var d))
                        continue;
                    CopyTransition(t, ns.AddTransition(d), remap);
                }
            }

            foreach (var t in srcSm.anyStateTransitions)
            {
                if (t.destinationState == null || !stateMap.TryGetValue(t.destinationState, out var d))
                    continue;
                var nt = dstSm.AddAnyStateTransition(d);
                nt.canTransitionToSelf = t.canTransitionToSelf;
                CopyTransition(t, nt, remap);
            }

            foreach (var t in srcSm.entryTransitions)
            {
                if (t.destinationState == null || !stateMap.TryGetValue(t.destinationState, out var d))
                    continue;
                var nt = dstSm.AddEntryTransition(d);
                foreach (var c in t.conditions)
                    nt.AddCondition(c.mode, c.threshold, Map(remap, c.parameter));
            }

            foreach (var child in srcSm.stateMachines)
                Wire(child.stateMachine, remap, stateMap, smMap);
        }

        private static void CopyTransition(AnimatorStateTransition src, AnimatorStateTransition dst,
                                            Dictionary<string, string> remap)
        {
            dst.hasExitTime = src.hasExitTime;
            dst.exitTime = src.exitTime;
            dst.hasFixedDuration = src.hasFixedDuration;
            dst.duration = src.duration;
            dst.offset = src.offset;
            dst.interruptionSource = src.interruptionSource;
            dst.orderedInterruption = src.orderedInterruption;
            foreach (var c in src.conditions)
                dst.AddCondition(c.mode, c.threshold, Map(remap, c.parameter));
        }

        private static Motion CloneMotion(Motion motion, Dictionary<string, string> remap,
                                          AnimatorController dst, AssetSaver assets)
        {
            if (!(motion is BlendTree srcTree))
                return motion; // AnimationClips are shared assets — reference directly.

            var tree = new BlendTree
            {
                name = srcTree.name,
                blendType = srcTree.blendType,
                blendParameter = Map(remap, srcTree.blendParameter),
                blendParameterY = Map(remap, srcTree.blendParameterY),
                minThreshold = srcTree.minThreshold,
                maxThreshold = srcTree.maxThreshold,
                useAutomaticThresholds = srcTree.useAutomaticThresholds,
            };
            assets.AddSubAsset(tree, dst);

            foreach (var ch in srcTree.children)
            {
                tree.AddChild(CloneMotion(ch.motion, remap, dst, assets));
                var arr = tree.children;
                var last = arr.Length - 1;
                arr[last].threshold = ch.threshold;
                arr[last].position = ch.position;
                arr[last].timeScale = ch.timeScale;
                arr[last].cycleOffset = ch.cycleOffset;
                arr[last].directBlendParameter = Map(remap, ch.directBlendParameter);
                arr[last].mirror = ch.mirror;
                tree.children = arr;
            }
            return tree;
        }

        private static string Map(Dictionary<string, string> remap, string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return remap.TryGetValue(name, out var mapped) ? mapped : name;
        }
    }
}
