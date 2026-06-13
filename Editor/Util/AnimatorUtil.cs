using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Helpers for building animator layers and clips the way ChilloutVR's AAS
    /// expects (synced float/bool parameters driving toggle layers).</summary>
    internal static class AnimatorUtil
    {
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
    }
}
