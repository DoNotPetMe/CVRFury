using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Helpers for building animator layers and clips the way ChilloutVR's AAS
    /// expects (synced float/bool parameters driving toggle layers).</summary>
    internal static class AnimatorUtil
    {
        /// <summary>Returns the WriteDefaults convention the controller already uses (the majority of its
        /// existing states), so layers we add match it. A WriteDefaults mismatch between our layers and CVR's
        /// locomotion is what makes an emote look right while moving but wrong when idle.</summary>
        public static bool DetectWriteDefaults(AnimatorController c)
        {
            int on = 0, off = 0;
            if (c != null)
                foreach (var layer in c.layers)
                    CountWriteDefaults(layer.stateMachine, ref on, ref off);
            return on >= off; // tie or empty → true (CVR's stock animator uses WriteDefaults on)
        }

        private static void CountWriteDefaults(AnimatorStateMachine sm, ref int on, ref int off)
        {
            if (sm == null) return;
            foreach (var s in sm.states)
                if (s.state != null) { if (s.state.writeDefaultValues) on++; else off++; }
            foreach (var sub in sm.stateMachines)
                CountWriteDefaults(sub.stateMachine, ref on, ref off);
        }

        /// <summary>Force the WriteDefaults flag on every state of layers whose name starts with
        /// <paramref name="layerNamePrefix"/>. Used to repair already-built emote layers in place.</summary>
        public static int SetWriteDefaultsForLayers(AnimatorController c, string layerNamePrefix, bool value)
        {
            if (c == null) return 0;
            int changed = 0;
            foreach (var layer in c.layers)
            {
                if (layer.name == null || !layer.name.StartsWith(layerNamePrefix)) continue;
                changed += ApplyWriteDefaults(layer.stateMachine, value);
            }
            return changed;
        }

        private static int ApplyWriteDefaults(AnimatorStateMachine sm, bool value)
        {
            if (sm == null) return 0;
            int changed = 0;
            foreach (var s in sm.states)
                if (s.state != null && s.state.writeDefaultValues != value) { s.state.writeDefaultValues = value; changed++; }
            foreach (var sub in sm.stateMachines)
                changed += ApplyWriteDefaults(sub.stateMachine, value);
            return changed;
        }

        /// <summary>Remove every layer whose name starts with <paramref name="prefix"/>. Returns the count.</summary>
        public static int RemoveLayers(AnimatorController c, string prefix)
        {
            if (c == null) return 0;
            int removed = 0;
            for (int i = c.layers.Length - 1; i >= 0; i--)
                if (c.layers[i].name != null && c.layers[i].name.StartsWith(prefix)) { c.RemoveLayer(i); removed++; }
            return removed;
        }

        /// <summary>Remove every parameter whose name starts with <paramref name="prefix"/>. Returns the count.</summary>
        public static int RemoveParameters(AnimatorController c, string prefix)
        {
            if (c == null) return 0;
            int removed = 0;
            for (int i = c.parameters.Length - 1; i >= 0; i--)
                if (c.parameters[i].name != null && c.parameters[i].name.StartsWith(prefix)) { c.RemoveParameter(i); removed++; }
            return removed;
        }

        public static void EnsureFloatParam(AnimatorController c, string name, float def = 0f)
        {
            if (c.parameters.Any(p => p.name == name)) return;
            c.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = def,
            });
        }

        public static void EnsureBoolParam(AnimatorController c, string name, bool def = false)
        {
            if (c.parameters.Any(p => p.name == name)) return;
            c.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = def,
            });
        }

        /// <summary>
        /// Add a two-state toggle layer driven by a float parameter (the AAS convention: a
        /// synced float that is 0 or 1). <paramref name="transitionSeconds"/> &gt; 0 produces a
        /// smooth blend; 0 is an instant cut.
        /// </summary>
        public static void EnsureIntParam(AnimatorController c, string name, int def = 0)
        {
            if (c.parameters.Any(p => p.name == name)) return;
            c.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Int,
                defaultInt = def,
            });
        }

        public static void AddToggleLayer(AnimatorController c, string layerName, string param,
                                          AnimationClip offClip, AnimationClip onClip,
                                          float transitionSeconds, bool defaultOn)
        {
            EnsureFloatParam(c, param, defaultOn ? 1f : 0f);

            // Add the layer first so its state machine is owned by the (asset-backed) controller;
            // states/transitions created on it then attach as sub-assets automatically.
            var name = UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;

            var off = sm.AddState("Off");
            off.motion = offClip;
            off.writeDefaultValues = false;

            var on = sm.AddState("On");
            on.motion = onClip;
            on.writeDefaultValues = false;

            sm.defaultState = defaultOn ? on : off;

            var toOn = off.AddTransition(on);
            ConfigureTransition(toOn, transitionSeconds);
            toOn.AddCondition(AnimatorConditionMode.Greater, 0.5f, param);

            var toOff = on.AddTransition(off);
            ConfigureTransition(toOff, transitionSeconds);
            toOff.AddCondition(AnimatorConditionMode.Less, 0.5f, param);
        }

        /// <summary>
        /// Add a layer that activates while a (platform-driven) float parameter equals a specific
        /// discrete value — used for hand gestures. The On state is entered when the parameter is
        /// within ±0.5 of <paramref name="value"/> and exited otherwise.
        /// </summary>
        public static void AddGestureLayer(AnimatorController c, string layerName, string param,
                                           int value, AnimationClip offClip, AnimationClip onClip,
                                           float transitionSeconds)
        {
            EnsureFloatParam(c, param, 0f);

            var name = UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            var off = sm.AddState("Off");
            off.motion = offClip;
            off.writeDefaultValues = false;
            var on = sm.AddState("On");
            on.motion = onClip;
            on.writeDefaultValues = false;
            sm.defaultState = off;

            var toOn = off.AddTransition(on);
            ConfigureTransition(toOn, transitionSeconds);
            toOn.AddCondition(AnimatorConditionMode.Greater, value - 0.5f, param);
            toOn.AddCondition(AnimatorConditionMode.Less, value + 0.5f, param);

            // Two exit transitions cover "below the window" OR "above the window".
            var toOffLow = on.AddTransition(off);
            ConfigureTransition(toOffLow, transitionSeconds);
            toOffLow.AddCondition(AnimatorConditionMode.Less, value - 0.5f, param);
            var toOffHigh = on.AddTransition(off);
            ConfigureTransition(toOffHigh, transitionSeconds);
            toOffHigh.AddCondition(AnimatorConditionMode.Greater, value + 0.5f, param);
        }

        /// <summary>
        /// Add an exclusive multi-state layer driven by a float parameter whose value selects the
        /// active state (0, 1, 2 …). Each state is reached from Any State when the parameter is
        /// within ±0.5 of its index. This is the animator side of a Modes / Dropdown control.
        /// </summary>
        public static void AddModesLayer(AnimatorController c, string layerName, string param,
                                         AnimationClip[] clips, float transitionSeconds, int defaultIndex,
                                         bool useInt = false)
        {
            if (useInt) EnsureIntParam(c, param, defaultIndex);
            else EnsureFloatParam(c, param, defaultIndex);

            var name = UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            for (var i = 0; i < clips.Length; i++)
            {
                var state = sm.AddState($"Mode {i}");
                state.motion = clips[i];
                state.writeDefaultValues = false;
                if (i == Mathf.Clamp(defaultIndex, 0, clips.Length - 1)) sm.defaultState = state;

                var t = sm.AddAnyStateTransition(state);
                ConfigureTransition(t, transitionSeconds);
                t.canTransitionToSelf = false;
                if (useInt)
                    t.AddCondition(AnimatorConditionMode.Equals, i, param); // exact int match (dropdown)
                else
                {
                    // Float window [i-0.5, i+0.5); omit the unbounded side at the ends.
                    if (i > 0) t.AddCondition(AnimatorConditionMode.Greater, i - 0.5f, param);
                    if (i < clips.Length - 1) t.AddCondition(AnimatorConditionMode.Less, i + 0.5f, param);
                }
            }
        }

        /// <summary>
        /// Add an always-active single-state layer holding a 1D blend tree, blending from
        /// <paramref name="zeroClip"/> at 0 to <paramref name="oneClip"/> at 1 across the float
        /// <paramref name="param"/>. This is the animator side of a radial / puppet slider.
        /// </summary>
        public static void AddBlendTreeLayer(AnimatorController c, string layerName, string param,
                                             AnimationClip zeroClip, AnimationClip oneClip,
                                             float defaultValue, AssetSaver assets)
        {
            EnsureFloatParam(c, param, defaultValue);

            var name = UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            var tree = new BlendTree
            {
                name = layerName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = param,
                useAutomaticThresholds = false,
                minThreshold = 0f,
                maxThreshold = 1f,
            };
            assets.AddSubAsset(tree, c);
            tree.AddChild(zeroClip, 0f);
            tree.AddChild(oneClip, 1f);

            var state = sm.AddState(layerName);
            state.motion = tree;
            state.writeDefaultValues = false;
            sm.defaultState = state;
        }

        /// <summary>Always-on 1D blend tree (min→max across a synced Float), the animator side of a CCK
        /// slider / radial. The blend tree is stored as a sub-asset of the controller directly (no
        /// AssetSaver needed), and an optional <paramref name="mask"/> keeps the layer off the humanoid
        /// rig. Either clip may be null (a null end leaves the property at the avatar's default there).</summary>
        public static void AddBlendTreeLayer(AnimatorController c, string layerName, string param,
                                             AnimationClip zeroClip, AnimationClip oneClip,
                                             float defaultValue, AvatarMask mask)
        {
            EnsureFloatParam(c, param, defaultValue);

            var name = UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            if (mask != null) layers[idx].avatarMask = mask;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            var tree = new BlendTree
            {
                name = layerName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = param,
                useAutomaticThresholds = false,
                minThreshold = 0f,
                maxThreshold = 1f,
            };
            UnityEditor.AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(zeroClip, 0f);
            tree.AddChild(oneClip, 1f);

            var state = sm.AddState(layerName);
            state.motion = tree;
            state.writeDefaultValues = false;
            sm.defaultState = state;
        }

        /// <summary>Add a two-state toggle layer driven by a <b>Bool</b> parameter (CVR's synced toggle
        /// encoding): Off plays <paramref name="offClip"/>, On plays <paramref name="onClip"/>, switched by
        /// If/IfNot on the bool. Used to build a working clip-driven layer for a CCK toggle so the avatar's
        /// controller actually carries the parameter and animates.</summary>
        public static void AddBoolToggleLayer(AnimatorController c, string layerName, string param,
                                              AnimationClip offClip, AnimationClip onClip, bool defaultOn,
                                              AvatarMask mask = null, bool writeDefaults = false,
                                              float transitionSeconds = 0f)
        {
            EnsureBoolParam(c, param, defaultOn);

            var name = UniqueLayerName(c, layerName);
            c.AddLayer(name);
            var layers = c.layers;
            var idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            // A mask with every humanoid body part disabled makes it impossible for this layer to drive
            // the rig's muscles/IK — so even a clip that (accidentally) animates the body can only ever
            // toggle GameObjects/blendshapes, never pose the avatar. This is the structural cure for the
            // "motorbike pose": clothing/accessory toggles must not be able to move the skeleton.
            if (mask != null) layers[idx].avatarMask = mask;
            c.layers = layers;

            var sm = c.layers[idx].stateMachine;
            var off = sm.AddState("Off");
            off.motion = offClip;
            // For body-posing layers (emotes) writeDefaults=true lets the Off state release the muscles back
            // to the locomotion layer instead of leaving this Override layer holding them at the bind pose —
            // which is the "motorbike" freeze. Clothing toggles keep writeDefaults=false (mask-protected).
            off.writeDefaultValues = writeDefaults;
            var on = sm.AddState("On");
            on.motion = onClip;
            on.writeDefaultValues = writeDefaults;
            sm.defaultState = defaultOn ? on : off;

            var toOn = off.AddTransition(on);
            ConfigureTransition(toOn, transitionSeconds);
            toOn.AddCondition(AnimatorConditionMode.If, 0f, param);
            var toOff = on.AddTransition(off);
            ConfigureTransition(toOff, transitionSeconds);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, param);
        }

        /// <summary>Create an AvatarMask that disables every humanoid body part (muscles + IK goals),
        /// leaving the Transform section empty so generic transform/GameObject curves still apply. Assigned
        /// to CVRFury's clip-toggle layers so they can never pose the humanoid rig (the "motorbike pose").
        /// The mask is stored as a sub-asset of the controller so it ships with it.</summary>
        public static AvatarMask CreateNoHumanoidMask(AnimatorController c)
        {
            var mask = new AvatarMask { name = "CVRFury No-Humanoid Mask" };
            for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, false);
            if (AssetDatabase.Contains(c))
                AssetDatabase.AddObjectToAsset(mask, c);
            return mask;
        }

        private static void ConfigureTransition(AnimatorStateTransition t, float seconds)
        {
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.hasFixedDuration = true;
            t.duration = Mathf.Max(0f, seconds);
            t.canTransitionToSelf = false;
        }

        public static string UniqueLayerName(AnimatorController c, string desired)
        {
            var name = desired;
            var i = 1;
            while (c.layers.Any(l => l.name == name))
                name = $"{desired} ({i++})";
            return name;
        }

        /// <summary>
        /// Ensure every parameter referenced by a blend tree actually exists on the controller.
        ///
        /// VRChat avatars built with VRCFury / d4rk's "Direct Blend Tree" optimiser drive their entire
        /// toggle / blendshape / material / AAP system through one always-on Direct Blend Tree whose
        /// children are weighted by a constant parameter (conventionally named <c>Blend</c>, held at 1).
        /// VRCFury <em>injects that parameter at VRChat build time</em>, so the authored FX controller
        /// references it in 100s of children but never declares it. After merging into the CVR animator
        /// the reference survives but the parameter is undefined — and Unity evaluates a missing
        /// <c>directBlendParameter</c> as 0, multiplying the whole tree to nothing. The result: clothing
        /// blendshapes never reach their "shown" value (invisible-but-enabled clothing), the species blend
        /// sits at its default (stuck between human and furry) and every toggle does nothing.
        ///
        /// We recreate the missing parameters: a missing <c>directBlendParameter</c> is a multiplicative
        /// weight, so it defaults to 1 ("apply"); a missing 1D/2D <c>blendParameter</c> is a position and
        /// defaults to 0 (neutral). Parameters that already exist (real toggles) are left untouched.
        /// </summary>
        public static int EnsureBlendTreeParametersExist(AnimatorController c, BuildLog log)
        {
            if (c == null) return 0;
            var declared = new HashSet<string>(c.parameters.Select(p => p.name));
            var directWeights = new HashSet<string>();
            var positions = new HashSet<string>();

            void Walk(Motion m)
            {
                if (!(m is BlendTree tree)) return;
                if (!string.IsNullOrEmpty(tree.blendParameter)) positions.Add(tree.blendParameter);
                if (!string.IsNullOrEmpty(tree.blendParameterY)) positions.Add(tree.blendParameterY);
                foreach (var ch in tree.children)
                {
                    if (tree.blendType == BlendTreeType.Direct && !string.IsNullOrEmpty(ch.directBlendParameter))
                        directWeights.Add(ch.directBlendParameter);
                    Walk(ch.motion);
                }
            }
            foreach (var layer in c.layers)
                foreach (var cs in AllStates(layer.stateMachine))
                    Walk(cs.motion);

            var added = new List<string>();
            // Direct-blend constant weights → default 1 so the tree actually applies.
            foreach (var name in directWeights)
            {
                if (declared.Contains(name)) continue;
                EnsureFloatParam(c, name, 1f);
                declared.Add(name);
                added.Add(name + "=1 (Direct Blend weight)");
            }
            // Positional blend parameters → default 0 (neutral).
            foreach (var name in positions)
            {
                if (declared.Contains(name)) continue;
                EnsureFloatParam(c, name, 0f);
                declared.Add(name);
                added.Add(name + "=0 (blend position)");
            }

            if (added.Count > 0)
                log.Info($"Restored {added.Count} blend-tree parameter(s) that were referenced but not " +
                         "declared (VRCFury injects these at build time). Without them a Direct Blend Tree " +
                         "multiplies to zero — the cause of invisible/enabled clothing, a species blend stuck " +
                         "between variants, and dead toggles. Restored: " + string.Join(", ", added));
            return added.Count;
        }

        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states) yield return cs.state;
            foreach (var child in sm.stateMachines)
                foreach (var s in AllStates(child.stateMachine)) yield return s;
        }
    }
}
